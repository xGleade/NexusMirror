using System;
using System.Linq;
using NexusForever.Database.World.Model;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Game.Static.Challenges;

namespace NexusForever.Game.Challenge
{
    public class ChallengeTemplate
    {
        private const uint DefaultActiveDurationMs = 300000u;
        private const uint DefaultCooldownDurationMs = 1800000u;
        private const uint DefaultAreaFailDurationMs = 10000u;

        public ChallengeEntry Entry { get; private set; }
        public uint Id => Entry.Id;
        public ChallengeType Type => (ChallengeType)Entry.ChallengeTypeEnum;
        public ChallengeObjectiveType ObjectiveType { get; private set; }
        public uint ObjectiveObjectId { get; private set; }
        public uint TargetGroupId { get; private set; }
        public uint GoalCount { get; private set; }
        public uint ActiveDurationMs { get; private set; }
        public uint CooldownDurationMs { get; private set; }
        public uint AreaFailDurationMs { get; private set; }
        public uint[] TierGoalCount { get; private set; } = new uint[3];
        public bool Repeatable { get; private set; }
        public bool IsSupported { get; private set; }

        public void Initialise(ChallengeEntry entry, ChallengeContentModel content = null)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));

            ObjectiveType     = GetObjectiveType(Type);
            ObjectiveObjectId = entry.Target;
            TargetGroupId     = entry.Target;
            TierGoalCount     = BuildTierGoalCount(entry);
            GoalCount         = Math.Max(GetCompletionCount(entry), TierGoalCount.Where(t => t > 0u).DefaultIfEmpty(0u).Max());

            ActiveDurationMs   = content?.ActiveDurationMs ?? DefaultActiveDurationMs;
            CooldownDurationMs = content?.CooldownDurationMs ?? DefaultCooldownDurationMs;
            AreaFailDurationMs = content?.AreaFailDurationMs ?? DefaultAreaFailDurationMs;
            Repeatable         = content?.Repeatable ?? true;

            RefreshSupported();
        }

        public bool Matches(uint? worldZoneId, ChallengeObjectiveType objectiveType, uint objectId)
        {
            if (!IsSupported)
                return false;

            if (ObjectiveType != objectiveType)
                return false;

            if (ObjectiveObjectId != objectId)
                return false;

            uint requiredZoneId = Entry.WorldZoneIdRestriction != 0u
                ? Entry.WorldZoneIdRestriction
                : Entry.WorldZoneId;
            if (requiredZoneId != 0u && worldZoneId != requiredZoneId)
                return false;

            return true;
        }

        private void RefreshSupported()
        {
            IsSupported = ObjectiveType != ChallengeObjectiveType.Script
                && ObjectiveObjectId != 0u
                && TargetGroupId != 0u
                && GoalCount != 0u;
        }

        private static ChallengeObjectiveType GetObjectiveType(ChallengeType type)
        {
            return type switch
            {
                ChallengeType.Combat            => ChallengeObjectiveType.KillTargetGroup,
                ChallengeType.Item              => ChallengeObjectiveType.Collect,
                ChallengeType.Collect           => ChallengeObjectiveType.Collect,
                ChallengeType.ChecklistActivate => ChallengeObjectiveType.ChecklistActivate,
                _                               => ChallengeObjectiveType.Script
            };
        }

        private static uint GetCompletionCount(ChallengeEntry entry)
        {
            return entry.CompletionCount == uint.MaxValue ? 0u : entry.CompletionCount;
        }

        private static uint[] BuildTierGoalCount(ChallengeEntry entry)
        {
            return new[]
            {
                GetTierCount(entry.ChallengeTierId00),
                GetTierCount(entry.ChallengeTierId01),
                GetTierCount(entry.ChallengeTierId02)
            };
        }

        private static uint GetTierCount(uint tierId)
        {
            return tierId == 0u
                ? 0u
                : GameTableManager.Instance.ChallengeTier.GetEntry(tierId)?.Count ?? 0u;
        }
    }
}
