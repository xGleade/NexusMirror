namespace NexusForever.Database.World.Model
{
    public class ChallengeContentModel
    {
        public uint ChallengeId { get; set; }
        public uint? ActiveDurationMs { get; set; }
        public uint? CooldownDurationMs { get; set; }
        public uint? AreaFailDurationMs { get; set; }
        public bool? Repeatable { get; set; }
        public string Source { get; set; }
        public string Confidence { get; set; }
    }
}
