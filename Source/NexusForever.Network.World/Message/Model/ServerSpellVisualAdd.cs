using NexusForever.Network.Message;
using NexusForever.Network.World.Entity;

namespace NexusForever.Network.World.Message.Model
{
    [Message(GameMessageOpcode.ServerSpellVisualAdd)]
    public class ServerSpellVisualAdd : IWritable
    {
        public uint SpellVisualEffectClientId { get; set; }
        public uint UnitId { get; set; }
        public uint VisualEffectId { get; set; }
        public uint VisualEffectIdSound { get; set; }
        public uint Spell4VisualId { get; set; }
        public uint Unknown14 { get; set; }
        public Position Position { get; set; } = new Position();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(SpellVisualEffectClientId);
            writer.Write(UnitId);
            writer.Write(VisualEffectId, 18u);
            writer.Write(VisualEffectIdSound, 18u);
            writer.Write(Spell4VisualId, 18u);
            writer.Write(Unknown14);
            Position.Write(writer);
        }
    }
}
