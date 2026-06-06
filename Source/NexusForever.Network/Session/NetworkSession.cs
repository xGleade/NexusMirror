using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NexusForever.Network.Session.Static;
using NexusForever.Shared.Game.Events;
using NLog;

namespace NexusForever.Network.Session
{
    public abstract class NetworkSession : INetworkSession
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Unique id for <see cref="NetworkSession"/>.
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// <see cref="IEvent"/> queue that will be processed during <see cref="NetworkSession"/> update.
        /// </summary>
        public EventQueue Events { get; } = new();

        /// <summary>
        /// Heartbeat to check if <see cref="NetworkSession"/> is still alive.
        /// </summary>
        /// <remarks>
        /// If <see cref="SocketHeartbeat"/> flatlines the <see cref="NetworkSession"/> will be disconnected.
        /// </remarks>
        public SocketHeartbeat Heartbeat { get; } = new();

        private Socket socket;
        private readonly byte[] buffer = new byte[4096];
        private int bufferOffset;
        private readonly ConcurrentQueue<PendingSend> pendingSends = new();
        private int sendActive;

        private DisconnectState? disconnectState;

        /// <summary>
        /// Initialise <see cref="NetworkSession"/> with new <see cref="Socket"/> and begin listening for data.
        /// </summary>
        public virtual void OnAccept(Socket newSocket)
        {
            if (socket != null)
                throw new InvalidOperationException();

            Id = Guid.NewGuid().ToString();

            socket = newSocket;
            socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveDataCallback, null);

            log.Trace($"New client {Id} connected from {newSocket.RemoteEndPoint}.");
        }

        /// <summary>
        /// Update <see cref="NetworkSession"/> existing id with a new supplied id.
        /// </summary>
        /// <remarks>
        /// This should be used when the default session id can be replaced with a known unique id.
        /// </remarks>
        public void UpdateId(string id)
        {
            log.Trace($"Client {Id} updated id to {id}.");
            Id = id;
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public virtual void Update(double lastTick)
        {
            Events.Update(lastTick);

            if (!disconnectState.HasValue)
                Heartbeat.Update(lastTick);

            // Prevents disconnection process happening again
            if (disconnectState == DisconnectState.Complete || disconnectState == DisconnectState.Processing)
                return;

            if (Heartbeat.Flatline || disconnectState == DisconnectState.Pending)
            {
                // no defibrillator is going to save this session
                if (Heartbeat.Flatline)
                    log.Trace($"Client {Id} has flatlined.");

                disconnectState = DisconnectState.Processing;
                OnDisconnect();
            }
        }

        protected virtual void OnDisconnect()
        {
            pendingSends.Clear();

            try
            {
                EndPoint remoteEndPoint = socket.RemoteEndPoint;
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();

                log.Trace($"Client {Id} disconnected. {remoteEndPoint}");
            }
            catch (Exception e)
            {
                log.Error(e, $"An exception occured for client {Id} during socket close!");
            }

            disconnectState = DisconnectState.Complete;
        }

        /// <summary>
        /// Returns if <see cref="NetworkSession"/> can be disposed.
        /// </summary>
        public virtual bool CanDispose()
        {
            return disconnectState == DisconnectState.Complete && !Events.PendingEvents;
        }

        /// <summary>
        /// Invoked with <see cref="IAsyncResult"/> when data from the <see cref="Socket"/> is received.
        /// </summary>
        private void ReceiveDataCallback(IAsyncResult ar)
        {
            try
            {
                int length = socket.EndReceive(ar);
                if (length == 0)
                {
                    ForceDisconnect();
                    return;
                }

                byte[] data = new byte[length + bufferOffset];
                Buffer.BlockCopy(buffer, 0, data, 0, data.Length);
                bufferOffset = (int)OnData(data);

                // if we have data that wasn't processed move it to the start of the buffer
                // any new data will be amended to it
                if (bufferOffset != 0)
                    Buffer.BlockCopy(buffer, data.Length - bufferOffset, buffer, 0, bufferOffset);

                socket.BeginReceive(buffer, bufferOffset, buffer.Length - bufferOffset, SocketFlags.None, ReceiveDataCallback, null);
            }
            catch (Exception e)
            {
                log.Error(e, $"An exception occured for client {Id} during socket read!");
                ForceDisconnect();
            }
        }

        protected abstract uint OnData(byte[] data);

        /// <summary>
        /// Send supplied data to remote client on <see cref="Socket"/>.
        /// </summary>
        protected void SendRaw(byte[] data)
        {
            if (data == null || data.Length == 0 || disconnectState.HasValue || socket == null)
                return;

            pendingSends.Enqueue(new PendingSend(data));
            StartSendPump();
        }

        private void StartSendPump()
        {
            if (Interlocked.CompareExchange(ref sendActive, 1, 0) == 0)
                BeginNextSend();
        }

        private void BeginNextSend()
        {
            if (disconnectState.HasValue)
            {
                pendingSends.Clear();
                Interlocked.Exchange(ref sendActive, 0);
                return;
            }

            if (!pendingSends.TryDequeue(out PendingSend send))
            {
                Interlocked.Exchange(ref sendActive, 0);
                if (!pendingSends.IsEmpty)
                    StartSendPump();
                return;
            }

            BeginSend(send);
        }

        private void BeginSend(PendingSend send)
        {
            try
            {
                socket.BeginSend(send.Buffer, send.Offset, send.Count, SocketFlags.None, SendDataCallback, send);
            }
            catch (Exception e)
            {
                log.Error(e, $"An exception occured for client {Id} during socket send!");
                Interlocked.Exchange(ref sendActive, 0);
                ForceDisconnect();
            }
        }

        private void SendDataCallback(IAsyncResult ar)
        {
            var send = (PendingSend)ar.AsyncState;

            try
            {
                int length = socket.EndSend(ar);
                if (length <= 0)
                {
                    Interlocked.Exchange(ref sendActive, 0);
                    ForceDisconnect();
                    return;
                }

                send.Offset += length;
                if (send.Count > 0)
                {
                    BeginSend(send);
                    return;
                }

                BeginNextSend();
            }
            catch (Exception e)
            {
                log.Error(e, $"An exception occured for client {Id} during socket send!");
                Interlocked.Exchange(ref sendActive, 0);
                ForceDisconnect();
            }
        }

        /// <summary>
        /// Force disconnect of <see cref="NetworkSession"/>.
        /// </summary>
        public void ForceDisconnect()
        {
            if (disconnectState.HasValue)
                return;

            disconnectState = DisconnectState.Pending;
        }

        private sealed class PendingSend
        {
            public byte[] Buffer { get; }
            public int Offset { get; set; }
            public int Count => Buffer.Length - Offset;

            public PendingSend(byte[] buffer)
            {
                Buffer = buffer;
            }
        }
    }
}
