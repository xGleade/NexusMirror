using System;
using System.Collections.Generic;
using System.Linq;
using NexusForever.Database;
using NexusForever.Database.World;
using NexusForever.Database.World.Model;
using NexusForever.Shared;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NLog;
using PlayerPath = NexusForever.Game.Static.Entity.Path;

namespace NexusForever.Game.Soldier
{
    internal sealed class SoldierHoldoutDefinitionManager : Singleton<SoldierHoldoutDefinitionManager>
    {
        private const uint SoldierEventMissionType = 0u;
        private const double DefaultInitialSpawnDelaySeconds = 7d;
        private const double DefaultBetweenWaveDelaySeconds = 4d;
        private const double DefaultCompletedCleanupDelaySeconds = 8d;
        private const float DefaultSpawnRadius = 7f;
        private const float DefaultSpawnRadiusStep = 1.5f;

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<(uint WorldId, uint ActivatorCreatureId), List<SoldierHoldoutDefinition>> definitionsByActivator = new();
        private readonly HashSet<uint> worlds = new();
        private bool initialised;

        public SoldierHoldoutDefinitionManager()
        {
        }

        public void Initialise()
        {
            log.Info("Loading Soldier holdout definitions...");

            definitionsByActivator.Clear();
            worlds.Clear();

            Dictionary<uint, SoldierHoldoutModel> holdoutContent = LoadHoldoutContent();
            if (holdoutContent.Count == 0)
            {
                initialised = true;
                log.Info("Loaded 0 Soldier holdout definitions.");
                return;
            }

            foreach (PathMissionEntry mission in GameTableManager.Instance.PathMission?.Entries ?? Enumerable.Empty<PathMissionEntry>())
            {
                if (mission.PathTypeEnum != (uint)PlayerPath.Soldier || mission.PathMissionTypeEnum != SoldierEventMissionType)
                    continue;

                if (!holdoutContent.TryGetValue(mission.Id, out SoldierHoldoutModel content))
                    continue;

                if (!TryBuildDefinition(mission, content, out SoldierHoldoutDefinition definition))
                    continue;

                AddDefinition(definition);
            }

            initialised = true;
            log.Info($"Loaded {definitionsByActivator.Values.Sum(d => d.Count)} Soldier holdout definitions from {holdoutContent.Count} server content rows.");
        }

        public IEnumerable<SoldierHoldoutDefinition> GetDefinitions(uint worldId, uint activatorCreatureId)
        {
            EnsureInitialised();
            return definitionsByActivator.TryGetValue((worldId, activatorCreatureId), out List<SoldierHoldoutDefinition> definitions)
                ? definitions
                : Enumerable.Empty<SoldierHoldoutDefinition>();
        }

        public bool HasDefinitionsForWorld(uint worldId)
        {
            EnsureInitialised();
            return worlds.Contains(worldId);
        }

        private void EnsureInitialised()
        {
            if (!initialised)
                Initialise();
        }

        private void AddDefinition(SoldierHoldoutDefinition definition)
        {
            var key = (definition.WorldId, definition.ActivatorCreatureId);
            if (!definitionsByActivator.TryGetValue(key, out List<SoldierHoldoutDefinition> definitions))
            {
                definitions = new List<SoldierHoldoutDefinition>();
                definitionsByActivator[key] = definitions;
            }

            definitions.Add(definition);
            worlds.Add(definition.WorldId);
        }

        private bool TryBuildDefinition(PathMissionEntry mission, SoldierHoldoutModel content, out SoldierHoldoutDefinition definition)
        {
            definition = null;

            PathSoldierEventEntry soldierEvent = GameTableManager.Instance.PathSoldierEvent?.GetEntry(mission.ObjectId);
            if (soldierEvent == null)
            {
                log.Warn($"Skipping Soldier holdout mission {mission.Id}: missing PathSoldierEvent {mission.ObjectId}.");
                return false;
            }

            PathEpisodeEntry episode = GameTableManager.Instance.PathEpisode?.GetEntry(mission.PathEpisodeId);
            if (episode == null)
            {
                log.Warn($"Skipping Soldier holdout mission {mission.Id}: missing PathEpisode {mission.PathEpisodeId}.");
                return false;
            }

            uint activatorCreatureId = ResolveActivatorCreatureId(mission);
            if (activatorCreatureId == 0u)
            {
                log.Warn($"Skipping Soldier holdout mission {mission.Id}: no activator creature could be resolved.");
                return false;
            }

            List<SoldierHoldoutWaveDefinition> waves = BuildWaves(content);
            if (waves.Count == 0)
            {
                log.Warn($"Skipping Soldier holdout mission {mission.Id}: no server-side wave composition configured.");
                return false;
            }

            definition = new SoldierHoldoutDefinition
            {
                WorldId                      = episode.WorldId,
                MissionId                    = mission.Id,
                SoldierEventId               = soldierEvent.Id,
                ActivatorCreatureId          = activatorCreatureId,
                ZoneId                       = ResolveZoneId(mission, episode),
                ActivatedCreatureId          = content.ActivatedCreatureId,
                ActivatedDisplayInfoId       = content.ActivatedDisplayInfoId,
                ActiveModelSequenceId        = content.ActiveModelSequenceId,
                FallbackFactionId            = content.FallbackFactionId,
                InitialSpawnDelaySeconds     = ResolveInitialSpawnDelaySeconds(soldierEvent),
                BetweenWaveDelaySeconds      = ResolveBetweenWaveDelaySeconds(soldierEvent),
                CompletedCleanupDelaySeconds = DefaultCompletedCleanupDelaySeconds,
                SpawnRadius                  = DefaultSpawnRadius,
                SpawnRadiusStep              = DefaultSpawnRadiusStep,
                Waves                        = waves
            };

            return true;
        }

        private static List<SoldierHoldoutWaveDefinition> BuildWaves(SoldierHoldoutModel content)
        {
            var waves = new List<SoldierHoldoutWaveDefinition>();
            List<SoldierHoldoutWaveModel> configuredWaves = content.Waves?
                .OrderBy(w => w.WaveIndex)
                .ToList() ?? new List<SoldierHoldoutWaveModel>();

            foreach (SoldierHoldoutWaveModel wave in configuredWaves)
            {
                List<uint> creatureIds = BuildWaveCreatureIds(wave);

                if (creatureIds.Count == 0)
                    continue;

                waves.Add(new SoldierHoldoutWaveDefinition
                {
                    CreatureIds = creatureIds,
                    IsBoss      = wave.IsBoss,
                    SpawnRadius = wave.SpawnRadius.HasValue && wave.SpawnRadius.Value > 0f ? wave.SpawnRadius : null
                });
            }

            return waves;
        }

        private static List<uint> BuildWaveCreatureIds(SoldierHoldoutWaveModel wave)
        {
            var creatureIds = new List<uint>();
            foreach (SoldierHoldoutWaveSpawnModel spawn in wave.Spawns?.OrderBy(s => s.SpawnIndex) ?? Enumerable.Empty<SoldierHoldoutWaveSpawnModel>())
            {
                if (spawn.CreatureId == 0u)
                    continue;

                uint count = Math.Max(1u, spawn.Count);
                for (uint i = 0u; i < count; i++)
                    creatureIds.Add(spawn.CreatureId);
            }

            return creatureIds;
        }

        private static uint ResolveActivatorCreatureId(PathMissionEntry mission)
        {
            uint creatureId = GameTableManager.Instance.Creature2?.Entries
                .Where(c => c.PathMissionIdSoldier == mission.Id)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .FirstOrDefault() ?? 0u;
            if (creatureId != 0u)
                return creatureId;

            if (mission.Creature2IdUnlock != 0u)
                return mission.Creature2IdUnlock;

            return mission.Creature2IdContactOverride;
        }

        private static ushort ResolveZoneId(PathMissionEntry mission, PathEpisodeEntry episode)
        {
            foreach (uint locationId in GetMissionLocationIds(mission))
            {
                WorldLocation2Entry location = GameTableManager.Instance.WorldLocation2?.GetEntry(locationId);
                if (location?.WorldZoneId > 0u)
                    return ToUShort(location.WorldZoneId);
            }

            return ToUShort(episode.WorldZoneId);
        }

        private static IEnumerable<uint> GetMissionLocationIds(PathMissionEntry mission)
        {
            if (mission.WorldLocation2Id00 != 0u)
                yield return mission.WorldLocation2Id00;
            if (mission.WorldLocation2Id01 != 0u)
                yield return mission.WorldLocation2Id01;
            if (mission.WorldLocation2Id02 != 0u)
                yield return mission.WorldLocation2Id02;
            if (mission.WorldLocation2Id03 != 0u)
                yield return mission.WorldLocation2Id03;
        }

        private static double ResolveInitialSpawnDelaySeconds(PathSoldierEventEntry soldierEvent)
        {
            if (soldierEvent.InitialSpawnTime > 0u)
                return soldierEvent.InitialSpawnTime / 1000d;

            return DefaultInitialSpawnDelaySeconds;
        }

        private static double ResolveBetweenWaveDelaySeconds(PathSoldierEventEntry soldierEvent)
        {
            if (soldierEvent.MaxTimeBetweenWaves > 0u)
                return soldierEvent.MaxTimeBetweenWaves / 1000d;

            return DefaultBetweenWaveDelaySeconds;
        }

        private static ushort ToUShort(uint value)
        {
            return value <= ushort.MaxValue ? (ushort)value : ushort.MaxValue;
        }

        private static Dictionary<uint, SoldierHoldoutModel> LoadHoldoutContent()
        {
            WorldDatabase worldDatabase = DatabaseManager.Instance.GetDatabase<WorldDatabase>();
            if (worldDatabase == null)
            {
                log.Warn("World database is not available; no Soldier holdout server content can be loaded.");
                return new Dictionary<uint, SoldierHoldoutModel>();
            }

            var content = new Dictionary<uint, SoldierHoldoutModel>();
            foreach (SoldierHoldoutModel holdout in worldDatabase.GetSoldierHoldouts())
            {
                if (holdout.MissionId == 0u)
                    continue;

                if (!content.TryAdd(holdout.MissionId, holdout))
                    log.Warn($"Duplicate Soldier holdout server content for mission {holdout.MissionId}; ignoring later row.");
            }

            return content;
        }
    }
}
