using System;

namespace NexusForever.Database.Character.Model
{
    public class CharacterChallengeModel
    {
        public ulong Id { get; set; }
        public ushort ChallengeId { get; set; }
        public uint CompletionCount { get; set; }
        public uint CurrentCount { get; set; }
        public uint ActiveTimeRemainingMs { get; set; }
        public uint CooldownTimeRemainingMs { get; set; }
        public uint AreaFailTimeRemainingMs { get; set; }
        public DateTime? DateCompleted { get; set; }

        public CharacterModel Character { get; set; }
    }
}
