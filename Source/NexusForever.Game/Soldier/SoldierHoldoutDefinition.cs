using System;
using System.Collections.Generic;

namespace NexusForever.Game.Soldier
{
    internal sealed class SoldierHoldoutDefinition
    {
        public uint WorldId { get; init; }
        public uint MissionId { get; init; }
        public uint SoldierEventId { get; init; }
        public uint ActivatorCreatureId { get; init; }
        public ushort ZoneId { get; init; }
        public uint ActivatedCreatureId { get; init; }
        public uint ActivatedDisplayInfoId { get; init; }
        public uint ActiveModelSequenceId { get; init; }
        public ushort FallbackFactionId { get; init; }
        public double InitialSpawnDelaySeconds { get; init; }
        public double BetweenWaveDelaySeconds { get; init; }
        public double CompletedCleanupDelaySeconds { get; init; }
        public float SpawnRadius { get; init; }
        public float SpawnRadiusStep { get; init; }
        public IReadOnlyList<SoldierHoldoutWaveDefinition> Waves { get; init; } = Array.Empty<SoldierHoldoutWaveDefinition>();
    }

    internal sealed class SoldierHoldoutWaveDefinition
    {
        public IReadOnlyList<uint> CreatureIds { get; init; } = Array.Empty<uint>();
        public bool IsBoss { get; init; }
        public float? SpawnRadius { get; init; }
    }
}
