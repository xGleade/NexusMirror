using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using NexusForever.Shared;

namespace NexusForever.Network
{
    public class GamePacketReader : IDisposable
    {
        public uint BytePosition
        {
            get => (uint)(stream?.Position ?? 0u);
            set
            {
                stream.Position = value;
                ResetBits();
            }
        }
        public uint BytesRemaining => stream?.Remaining() ?? 0u;

        private byte currentBitPosition;
        private byte currentBitValue;
        private readonly Stream stream;

        public GamePacketReader(Stream input)
        {
            stream = input;
            ResetBits();
        }

        public void Dispose()
        {
            stream?.Dispose();
        }

        public void ResetBits()
        {
            if (currentBitPosition > 7)
                return;

            currentBitPosition = 8;
            currentBitValue = 0;
        }

        public bool ReadBit()
        {
            currentBitPosition++;
            if (currentBitPosition > 7)
            {
                currentBitPosition = 0;
                currentBitValue = (byte)stream.ReadByte();
            }

            return ((currentBitValue >> currentBitPosition) & 1) != 0;
        }

        private ulong ReadBits(uint bits)
        {
            if (IsByteAligned() && bits % 8u == 0u)
                return ReadAlignedBits(bits);

            ulong value = 0ul;
            for (uint i = 0u; i < bits; i++)
                if (ReadBit())
                    value |= 1ul << (int)i;

            return value;
        }

        private bool IsByteAligned()
        {
            return currentBitPosition >= 7;
        }

        private ulong ReadAlignedBits(uint bits)
        {
            switch (bits)
            {
                case 8:
                    return ReadAlignedByte();
                case 16:
                {
                    Span<byte> buffer = stackalloc byte[2];
                    ReadAlignedBytes(buffer);
                    return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
                }
                case 32:
                {
                    Span<byte> buffer = stackalloc byte[4];
                    ReadAlignedBytes(buffer);
                    return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                }
                case 64:
                {
                    Span<byte> buffer = stackalloc byte[8];
                    ReadAlignedBytes(buffer);
                    return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
                }
                default:
                {
                    ulong value = 0ul;
                    int bytes = (int)(bits / 8u);
                    for (int i = 0; i < bytes; i++)
                        value |= (ulong)ReadAlignedByte() << (i * 8);
                    return value;
                }
            }
        }

        private byte ReadAlignedByte()
        {
            int value = stream.ReadByte();
            if (value == -1)
                throw new EndOfStreamException();

            currentBitPosition = 7;
            return (byte)value;
        }

        private void ReadAlignedBytes(Span<byte> buffer)
        {
            for (int offset = 0; offset < buffer.Length;)
            {
                int read = stream.Read(buffer[offset..]);
                if (read == 0)
                    throw new EndOfStreamException();

                offset += read;
            }

            currentBitPosition = 7;
        }

        public byte ReadByte(uint bits = 8u)
        {
            if (bits > sizeof(byte) * 8)
                throw new ArgumentException();

            return (byte)ReadBits(bits);
        }

        public ushort ReadUShort(uint bits = 16u)
        {
            if (bits > sizeof(ushort) * 8)
                throw new ArgumentException();

            return (ushort)ReadBits(bits);
        }

        public short ReadShort(uint bits = 16u)
        {
            if (bits > sizeof(short) * 8)
                throw new ArgumentException();

            return (short)ReadBits(bits);
        }

        public uint ReadUInt(uint bits = 32u)
        {
            if (bits > sizeof(uint) * 8)
                throw new ArgumentException();

            return (uint)ReadBits(bits);
        }

        public int ReadInt(uint bits = 32u)
        {
            if (bits > sizeof(int) * 8)
                throw new ArgumentException();

            return (int)ReadBits(bits);
        }

        public float ReadSingle(uint bits = 32u)
        {
            if (bits > sizeof(float) * 8)
                throw new ArgumentException();

            int value = (int)ReadBits(bits);
            return BitConverter.Int32BitsToSingle(value);
        }

        public double ReadDouble(uint bits = 64u)
        {
            if (bits > sizeof(double) * 8)
                throw new ArgumentException();

            long value = (long)ReadBits(bits);
            return BitConverter.Int64BitsToDouble(value);
        }

        public ulong ReadULong(uint bits = 64u)
        {
            if (bits > sizeof(ulong) * 8)
                throw new ArgumentException();

            return ReadBits(bits);
        }

        public long ReadLong(uint bits = 64u)
        {
            if (bits > sizeof(long) * 8)
                throw new ArgumentException();

            return (long)ReadBits(bits);
        }

        public T ReadEnum<T>(uint bits = 64u) where T : Enum
        {
            if (bits > sizeof(ulong) * 8)
                throw new ArgumentException();

            return (T)Enum.ToObject(typeof(T), ReadBits(bits));
        }

        public byte[] ReadBytes(uint length)
        {
            byte[] data = new byte[length];
            ReadBytes(data, 0, length);

            return data;
        }

        public void ReadBytes(byte[] destination, int offset, uint length)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (offset < 0 || length > destination.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (IsByteAligned())
            {
                int remaining = (int)length;
                while (remaining > 0)
                {
                    int read = stream.Read(destination, offset, remaining);
                    if (read == 0)
                        throw new EndOfStreamException();

                    offset += read;
                    remaining -= read;
                }

                currentBitPosition = 7;
                return;
            }

            for (uint i = 0u; i < length; i++)
                destination[offset + (int)i] = ReadByte();
        }

        public string ReadWideStringFixed()
        {
            ushort length = ReadUShort();
            byte[] data = ReadBytes(length * 2u);
            return Encoding.Unicode.GetString(data, 0, data.Length - 2);
        }

        public string ReadWideString()
        {
            bool extended = ReadBit();
            ushort length = (ushort)(ReadUShort(extended ? 15u : 7u) << 1);

            byte[] data = ReadBytes(length);
            return Encoding.Unicode.GetString(data);
        }

        public string ReadString()
        {
            bool extended = ReadBit();
            ushort length = ReadUShort(extended ? 15u : 7u);

            byte[] data = ReadBytes(length);
            return Encoding.ASCII.GetString(data);
        }

        public float ReadPackedFloat()
        {
            float UnpackFloat(ushort packed)
            {
                uint v3 = packed & 0xFFFF7FFF;
                uint v4 = (packed & 0xFFFF8000) << 16;

                if ((v3 & 0x7C00) != 0)
                    return BitConverter.Int32BitsToSingle((int)(v4 | ((v3 + 0x1C000) << 13)));
                if ((v3 & 0x3FF) == 0)
                    return BitConverter.Int32BitsToSingle((int) (v4 | v3));

                uint v6 = (v3 & 0x3FF) << 13;
                uint i = 113;
                for (; v6 <= 0x7FFFFF; --i)
                    v6 *= 2;
                return BitConverter.Int32BitsToSingle((int)(v4 | (i << 23) | v6 & 0x7FFFFF));
            }

            return UnpackFloat(ReadUShort());
        }

        public Vector3 ReadVector3()
        {
            return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
        }

        public Vector3 ReadPackedVector3()
        {
            return new Vector3(ReadPackedFloat(), ReadPackedFloat(), ReadPackedFloat());
        }
    }
}
