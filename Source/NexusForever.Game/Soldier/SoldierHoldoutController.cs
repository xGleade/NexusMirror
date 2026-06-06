using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Map;
using NexusForever.Shared;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Game.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Map;
using NexusForever.Network.World.Message.Model;
using EntityPath = NexusForever.Game.Static.Entity.Path;
using PlayerPathSoldierEventMode = NexusForever.Network.World.Message.Model.ServerPathSoldierHoldoutStatus.PlayerPathSoldierEventMode;
using PlayerPathSoldierResult = NexusForever.Network.World.Message.Model.ServerPathSoldierHoldoutEnd.PlayerPathSoldierResult;

namespace NexusForever.Game.Soldier
{
    public static class SoldierHoldoutController
    {
        private static readonly Dictionary<IBaseMap, MapState> mapStates = new();

        public static void OnInteract(Player player, IWorldEntity entity)
        {
            if (!CanStart(player, entity, out SoldierHoldoutDefinition definition))
                return;

            MapState mapState = GetState(player.Map);
            if (mapState.ActiveByCharacterId.ContainsKey(player.CharacterId))
                return;

            bool missionAlreadyStarted = player.PathManager.HasStartedMission(definition.MissionId);
            if (!missionAlreadyStarted && !player.PathManager.TryStartMission(definition.MissionId, giverUnitId: entity.Guid))
                return;

            if (missionAlreadyStarted)
                player.PathManager.RefreshCurrentEpisode();

            ActivateBeacon(entity, definition);
            PlayBeaconActiveAnimation(entity, definition);

            var state = new HoldoutState(definition, player.CharacterId, player.Guid, entity.Guid, entity.Position)
            {
                PendingDelaySeconds = definition.InitialSpawnDelaySeconds
            };

            mapState.ActiveByCharacterId[player.CharacterId] = state;
            SendHoldoutStatus(player, state, PlayerPathSoldierEventMode.InitialDelay, state.PendingDelaySeconds);
        }

        public static void OnAddToMap(IGridEntity entity)
        {
            if (entity?.Map == null)
                return;

            if (!mapStates.TryGetValue(entity.Map, out MapState mapState))
                return;

            if (entity is INonPlayerEntity nonPlayer && mapState.PendingSpawnOwners.TryGetValue(nonPlayer, out HoldoutState state))
            {
                mapState.PendingSpawnOwners.Remove(nonPlayer);
                state.LiveSpawnGuids.Add(nonPlayer.Guid);
                mapState.SpawnOwnersByGuid[nonPlayer.Guid] = state;
            }
        }

        public static void OnRemoveFromMap(IGridEntity entity)
        {
            if (entity?.Map == null)
                return;

            if (!mapStates.TryGetValue(entity.Map, out MapState mapState))
                return;

            if (entity is Player player && mapState.ActiveByCharacterId.TryGetValue(player.CharacterId, out HoldoutState playerState))
            {
                RemoveState(entity.Map, mapState, playerState, true);
                return;
            }

            if (entity is INonPlayerEntity nonPlayer)
            {
                mapState.PendingSpawnOwners.Remove(nonPlayer);
                if (mapState.SpawnOwnersByGuid.TryGetValue(nonPlayer.Guid, out HoldoutState spawnState))
                {
                    spawnState.LiveSpawnGuids.Remove(nonPlayer.Guid);
                    mapState.SpawnOwnersByGuid.Remove(nonPlayer.Guid);
                }
            }
        }

        public static void OnUnitDeath(IUnitEntity unit, IUnitEntity attacker)
        {
            if (unit?.Map == null)
                return;

            if (!mapStates.TryGetValue(unit.Map, out MapState mapState))
                return;

            if (!mapState.SpawnOwnersByGuid.TryGetValue(unit.Guid, out HoldoutState state))
                return;

            state.LiveSpawnGuids.Remove(unit.Guid);
            state.DeadSpawnGuids.Add(unit.Guid);
            mapState.SpawnOwnersByGuid.Remove(unit.Guid);
        }

        public static void Update(IBaseMap map, double lastTick)
        {
            if (map == null)
                return;

            if (!mapStates.TryGetValue(map, out MapState mapState))
                return;

            foreach (HoldoutState state in mapState.ActiveByCharacterId.Values.ToList())
                UpdateState(map, mapState, state, lastTick);

            if (mapState.IsEmpty)
                mapStates.Remove(map);
        }

        private static bool CanStart(Player player, IWorldEntity entity, out SoldierHoldoutDefinition definition)
        {
            definition = null;

            if (player?.Map == null || entity == null || player.Path != EntityPath.Soldier)
                return false;

            definition = SoldierHoldoutDefinitionManager.Instance
                .GetDefinitions(player.Map.Entry.Id, entity.CreatureId)
                .FirstOrDefault(d => !player.PathManager.HasCompletedMission(d.MissionId));

            return definition != null;
        }

        private static void UpdateState(IBaseMap map, MapState mapState, HoldoutState state, double lastTick)
        {
            Player owner = map.GetEntity<Player>(state.PlayerGuid);
            if (owner == null || owner.PathManager.HasCompletedMission(state.Definition.MissionId))
            {
                RemoveState(map, mapState, state, true);
                return;
            }

            if (state.CleanupDelaySeconds.HasValue)
            {
                state.CleanupDelaySeconds -= lastTick;
                if (state.CleanupDelaySeconds <= 0d)
                    RemoveState(map, mapState, state, true);

                return;
            }

            if (state.LiveSpawnGuids.Count == 0 && state.IsWaveActive)
            {
                CompleteWave(map, mapState, state, owner);
                return;
            }

            if (state.PendingDelaySeconds <= 0d || state.IsWaveActive)
                return;

            state.PendingDelaySeconds -= lastTick;
            if (state.PendingDelaySeconds > 0d)
                return;

            SpawnWave(map, mapState, state);
        }

        private static void CompleteWave(IBaseMap map, MapState mapState, HoldoutState state, Player owner)
        {
            state.IsWaveActive = false;
            RemoveDeadSpawns(map, mapState, state);
            state.CurrentWaveIndex++;

            bool complete = state.CurrentWaveIndex >= state.Definition.Waves.Count;
            uint progressAmount = (uint)Math.Max(1, 100 / state.Definition.Waves.Count);
            uint stateData = (uint)Math.Min(state.Definition.Waves.Count, state.CurrentWaveIndex + 1);

            owner.PathManager.IncrementMission(
                state.Definition.MissionId,
                complete ? 100u - state.Progress : progressAmount,
                stateData,
                complete);

            state.Progress = Math.Min(100u, state.Progress + progressAmount);

            if (complete)
            {
                SendHoldoutEnd(owner, state.Definition, PlayerPathSoldierResult.Success);
                state.CleanupDelaySeconds = state.Definition.CompletedCleanupDelaySeconds;
                return;
            }

            state.PendingDelaySeconds = state.Definition.BetweenWaveDelaySeconds;
        }

        private static void RemoveDeadSpawns(IBaseMap map, MapState mapState, HoldoutState state)
        {
            foreach (uint guid in state.DeadSpawnGuids.ToList())
            {
                mapState.SpawnOwnersByGuid.Remove(guid);
                map.GetEntity<INonPlayerEntity>(guid)?.RemoveFromMap();
                state.DeadSpawnGuids.Remove(guid);
            }
        }

        private static void SpawnWave(IBaseMap map, MapState mapState, HoldoutState state)
        {
            if (state.CurrentWaveIndex >= state.Definition.Waves.Count)
                return;

            SoldierHoldoutWaveDefinition wave = state.Definition.Waves[state.CurrentWaveIndex];
            Vector3 origin = state.Origin;
            float radius = wave.SpawnRadius ?? state.Definition.SpawnRadius + Math.Min(state.CurrentWaveIndex, 2) * state.Definition.SpawnRadiusStep;
            Player owner = map.GetEntity<Player>(state.PlayerGuid);

            ActivateBeacon(map.GetEntity<IWorldEntity>(state.ActivatorGuid), state.Definition);
            SendHoldoutNextWave(owner, state);
            SendHoldoutStatus(owner, state, PlayerPathSoldierEventMode.Active, 0d);

            for (int i = 0; i < wave.CreatureIds.Count; i++)
            {
                float angle = ((360f / wave.CreatureIds.Count) * i + (state.CurrentWaveIndex * 37f)).ToRadians();
                Vector3 position = origin.GetPoint2D(angle, radius);
                position.Y = map.GetTerrainHeight(position.X, position.Z) ?? position.Y;

                INonPlayerEntity nonPlayer = SpawnNonPlayer(map, state.Definition, wave.CreatureIds[i], position, origin);
                if (nonPlayer != null)
                    mapState.PendingSpawnOwners[nonPlayer] = state;
            }

            state.IsWaveActive = true;
        }

        private static void SendHoldoutStatus(Player player, HoldoutState state, PlayerPathSoldierEventMode mode, double delaySeconds)
        {
            if (player == null)
                return;

            player.Session.EnqueueMessageEncrypted(new ServerPathSoldierHoldoutStatus
            {
                PathSoldierEventId  = state.Definition.SoldierEventId,
                UnitInfo            = [],
                UnitId              = state.ActivatorGuid,
                IsBoss              = IsBossWave(state),
                Mode                = mode,
                DelayTime           = (int)Math.Max(0d, delaySeconds * 1000d),
                WaveIndex           = state.CurrentWaveIndex,
                MaxDefendHealth     = 0f,
                MaxAuxiliaryHealth  = 0f,
                StartTimeOffset     = 0
            });
        }

        private static void SendHoldoutNextWave(Player player, HoldoutState state)
        {
            if (player == null)
                return;

            player.Session.EnqueueMessageEncrypted(new ServerPathSoldierHoldOutNextWave
            {
                PathSoldierEventId = (ushort)state.Definition.SoldierEventId,
                WaveIndex          = (uint)state.CurrentWaveIndex,
                IsBoss             = IsBossWave(state)
            });
        }

        private static void SendHoldoutEnd(Player player, SoldierHoldoutDefinition definition, PlayerPathSoldierResult result)
        {
            player?.Session.EnqueueMessageEncrypted(new ServerPathSoldierHoldoutEnd
            {
                PathSoldierEventId = (ushort)definition.SoldierEventId,
                Reason             = result
            });
        }

        private static bool IsBossWave(HoldoutState state)
        {
            return state.CurrentWaveIndex < state.Definition.Waves.Count
                && state.Definition.Waves[state.CurrentWaveIndex].IsBoss;
        }

        private static INonPlayerEntity SpawnNonPlayer(IBaseMap map, SoldierHoldoutDefinition definition, uint creatureId, Vector3 position, Vector3 origin)
        {
            if (GameTableManager.Instance.Creature2.GetEntry(creatureId) == null)
                return null;

            IEntityFactory entityFactory = LegacyServiceProvider.Provider.GetService<IEntityFactory>();
            ICreatureInfoManager creatureInfoManager = LegacyServiceProvider.Provider.GetService<ICreatureInfoManager>();
            ICreatureInfo creatureInfo = creatureInfoManager?.GetCreatureInfo(creatureId);
            if (entityFactory == null || creatureInfo == null)
                return null;

            INonPlayerEntity entity = entityFactory.CreateEntity<INonPlayerEntity>();
            if (entity == null)
                return null;

            entity.Initialise(creatureInfo, BuildNonPlayerModel(map, definition, creatureId, position, origin));
            entity.CreateFlags |= EntityCreateFlag.SpawnAnimation;

            map.EnqueueAdd(entity, position);
            return entity;
        }

        private static EntityModel BuildNonPlayerModel(IBaseMap map, SoldierHoldoutDefinition definition, uint creatureId, Vector3 position, Vector3 origin)
        {
            Vector3 direction = Vector3.Normalize(origin - position);
            float rotation = MathF.Atan2(direction.X, direction.Z);

            return new EntityModel
            {
                Type     = EntityType.NonPlayer,
                Creature = creatureId,
                World    = (ushort)map.Entry.Id,
                Area     = definition.ZoneId,
                X        = position.X,
                Y        = position.Y,
                Z        = position.Z,
                Rx          = rotation,
                DisplayInfo = GetDisplayInfoId(creatureId),
                Faction1    = GetFactionId(definition, creatureId),
                Faction2    = GetFactionId(definition, creatureId)
            };
        }

        private static uint GetDisplayInfoId(uint creatureId)
        {
            Creature2Entry entry = GameTableManager.Instance.Creature2.GetEntry(creatureId);
            if (entry == null)
                return 0u;

            Creature2DisplayGroupEntryEntry displayGroupEntry = GameTableManager.Instance.Creature2DisplayGroupEntry.Entries
                .Where(e => e.Creature2DisplayGroupId == entry.Creature2DisplayGroupId)
                .OrderByDescending(e => e.Weight)
                .ThenBy(e => e.Id)
                .FirstOrDefault();

            return displayGroupEntry?.Creature2DisplayInfoId ?? 0u;
        }

        private static ushort GetFactionId(SoldierHoldoutDefinition definition, uint creatureId)
        {
            Creature2Entry entry = GameTableManager.Instance.Creature2.GetEntry(creatureId);
            if (entry != null && (uint)entry.FactionId > 0u && (uint)entry.FactionId <= ushort.MaxValue)
                return (ushort)entry.FactionId;

            return definition.FallbackFactionId;
        }

        private static void ActivateBeacon(IWorldEntity entity, SoldierHoldoutDefinition definition)
        {
            if (entity == null || definition.ActivatedCreatureId == 0u || definition.ActivatedDisplayInfoId == 0u)
                return;

            if (entity.CreatureId == definition.ActivatedCreatureId && entity.DisplayInfoId == definition.ActivatedDisplayInfoId)
                return;

            ICreatureInfoManager creatureInfoManager = LegacyServiceProvider.Provider.GetService<ICreatureInfoManager>();
            ICreatureInfo creatureInfo = creatureInfoManager?.GetCreatureInfo(definition.ActivatedCreatureId);
            Creature2DisplayInfoEntry displayInfo = GameTableManager.Instance.Creature2DisplayInfo.GetEntry(definition.ActivatedDisplayInfoId);
            if (creatureInfo == null || displayInfo == null)
                return;

            entity.CreatureInfo = creatureInfo;
            entity.CreatureDisplayEntry = displayInfo;
            entity.VisibilityUpdate();
        }

        private static void PlayBeaconActiveAnimation(IWorldEntity entity, SoldierHoldoutDefinition definition)
        {
            if (entity == null || definition.ActiveModelSequenceId == 0u)
                return;

            entity.EnqueueToVisible(new ServerSetUnitInModelSequence
            {
                UnitId          = entity.Guid,
                ModelSequenceId = definition.ActiveModelSequenceId,
                StartTime       = 0f,
                Speed           = 1f,
                Layer           = 0u,
                Seed            = (ushort)(definition.ActiveModelSequenceId & ushort.MaxValue)
            }, true);
        }

        private static MapState GetState(IBaseMap map)
        {
            if (!mapStates.TryGetValue(map, out MapState state))
            {
                state = new MapState();
                mapStates[map] = state;
            }

            return state;
        }

        private static void RemoveState(IBaseMap map, MapState mapState, HoldoutState state, bool removeSpawns)
        {
            mapState.ActiveByCharacterId.Remove(state.CharacterId);

            foreach (INonPlayerEntity nonPlayer in mapState.PendingSpawnOwners
                .Where(pair => ReferenceEquals(pair.Value, state))
                .Select(pair => pair.Key)
                .ToList())
            {
                mapState.PendingSpawnOwners.Remove(nonPlayer);
            }

            foreach (uint guid in state.LiveSpawnGuids.Concat(state.DeadSpawnGuids).ToList())
            {
                mapState.SpawnOwnersByGuid.Remove(guid);
                if (removeSpawns)
                    map.GetEntity<INonPlayerEntity>(guid)?.RemoveFromMap();
            }

            state.LiveSpawnGuids.Clear();
            state.DeadSpawnGuids.Clear();
        }

        private sealed class MapState
        {
            public Dictionary<ulong, HoldoutState> ActiveByCharacterId { get; } = new();
            public Dictionary<uint, HoldoutState> SpawnOwnersByGuid { get; } = new();
            public Dictionary<INonPlayerEntity, HoldoutState> PendingSpawnOwners { get; } = new();

            public bool IsEmpty => ActiveByCharacterId.Count == 0
                && SpawnOwnersByGuid.Count == 0
                && PendingSpawnOwners.Count == 0;
        }

        private sealed class HoldoutState
        {
            public SoldierHoldoutDefinition Definition { get; }
            public ulong CharacterId { get; }
            public uint PlayerGuid { get; }
            public uint ActivatorGuid { get; }
            public Vector3 Origin { get; }
            public int CurrentWaveIndex { get; set; }
            public uint Progress { get; set; }
            public double PendingDelaySeconds { get; set; }
            public double? CleanupDelaySeconds { get; set; }
            public bool IsWaveActive { get; set; }
            public HashSet<uint> LiveSpawnGuids { get; } = new();
            public HashSet<uint> DeadSpawnGuids { get; } = new();

            public HoldoutState(SoldierHoldoutDefinition definition, ulong characterId, uint playerGuid, uint activatorGuid, Vector3 origin)
            {
                Definition    = definition;
                CharacterId   = characterId;
                PlayerGuid    = playerGuid;
                ActivatorGuid = activatorGuid;
                Origin        = origin;
            }
        }
    }
}
