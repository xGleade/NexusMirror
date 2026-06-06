namespace NexusForever.Database.World.Model
{
    public class PathMissionContentModel
    {
        public uint MissionId { get; set; }
        public uint CompletionXp { get; set; }
        public uint ExplorerBeaconCreatureId { get; set; }
        public string Source { get; set; }
        public string Confidence { get; set; }
    }
}
