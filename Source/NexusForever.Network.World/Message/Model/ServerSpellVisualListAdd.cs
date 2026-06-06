using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model
{
    [Message(GameMessageOpcode.ServerSpellVisualListAdd)]
    public class ServerSpellVisualListAdd : IWritable
    {
        public List<ServerSpellVisualAdd> SpellVisualList { get; set; } = new List<ServerSpellVisualAdd>();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(SpellVisualList.Count);
            SpellVisualList.ForEach(v => v.Write(writer));
        }
    }
}
