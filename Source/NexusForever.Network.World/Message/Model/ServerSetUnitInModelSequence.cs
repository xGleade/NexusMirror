using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    [Message(GameMessageOpcode.ServerSetUnitInModelSequence)]
    public class ServerSetUnitInModelSequence : IWritable
    {
        public uint UnitId { get; set; }
        public uint ModelSequenceId { get; set; }
        public float StartTime { get; set; }
        public float Speed { get; set; }
        public uint Layer { get; set; }
        public ushort Seed { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(UnitId);
            writer.Write(ModelSequenceId);
            writer.Write(StartTime);
            writer.Write(Speed);
            writer.Write(Layer);
            writer.Write(Seed);
        }
    }
}
