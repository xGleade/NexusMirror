using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    [Message(GameMessageOpcode.ServerSpellVisualGroupAdd)]
    public class ServerSpellVisualGroupAdd : IWritable
    {
        public uint UnitId { get; set; }
        public ushort Spell4VisualGroupId { get; set; }
        public uint Unknown { get; set; }
        public List<uint> SpellVisualEffectUniqueIds { get; set; } = new List<uint>();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(UnitId);
            writer.Write(Spell4VisualGroupId);
            writer.Write(Unknown);
            writer.Write(SpellVisualEffectUniqueIds.Count);
            SpellVisualEffectUniqueIds.ForEach(id => writer.Write(id));
        }
    }
}
