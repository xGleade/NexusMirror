using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Creature;
using NexusForever.Game.Abstract.Map;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Game;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Map;
using NexusForever.Game.Map.Search;
using NexusForever.Game.PathMission;
using NexusForever.Game.Static;
using NexusForever.Game.Prerequisite;
using NexusForever.Network.World.Message.Model;
using EntityPath = NexusForever.Game.Static.Entity.Path;
using GenericError = NexusForever.Network.World.Message.Static.GenericError;
using ItemUpdateReason = NexusForever.Network.World.Message.Static.ItemUpdateReason;
using PlayerPathSoldierEventMode = NexusForever.Network.World.Message.Model.ServerPathSoldierHoldoutStatus.PlayerPathSoldierEventMode;
using PlayerPathSoldierResult = NexusForever.Network.World.Message.Model.ServerPathSoldierHoldoutEnd.PlayerPathSoldierResult;
using NLog;

namespace NexusForever.Game.Entity
{
    public class PathManager : IPathManager
    {
        private const uint ConquerTheYetiMissionId = 156u;
        private const uint ConquerTheYetiEventId = 2u;
        private const uint NorthernWildsWorldId = 426u;
        private const uint MaxPathCount = 4u;
        private const uint MaxPathLevel = 30u;
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private enum PathMissionType
        {
            SoldierEvent                 = 0,
            ScientistCreatureInfo        = 2,
            ExplorerArea                 = 3,
            SoldierAssassinate           = 4,
            SoldierActivate              = 5,
            SoldierActivateChecklist     = 6,
            SoldierSwat                  = 7,
            ExplorerDoor                 = 12,
            ExplorerScavengerHunt        = 13,
            ScientistScan                = 14,
            ExplorerNode                 = 15,
            ExplorerUnknown              = 16,
            ExplorerActivate             = 17,
            ExplorerPowerMap             = 18,
            SettlerHub                   = 19,
            ScientistFieldStudy          = 20,
            SettlerInfrastructure        = 21,
            ScientistExperimentation     = 22,
            ScientistSpecimenSurvey      = 23,
            ScientistDatacubeDiscovery   = 24,
            SettlerMayor                 = 25,
            SettlerSheriff               = 26,
            SettlerImprovement           = 27
        }

        private enum PathMissionState : byte
        {
            Unlocked = 2,
            Started  = 3,
            Complete = 4
        }

        [Flags]
        private enum PathMissionSaveMask
        {
            None      = 0x00,
            Create    = 0x01,
            Completed = 0x02,
            UserData  = 0x04,
            StateData = 0x08
        }

        private readonly Player player;
        private readonly IEntityFactory entityFactory;
        private readonly ICreatureInfoManager creatureInfoManager;
        private readonly Dictionary<EntityPath, PathEntry> paths = new();
        private readonly Dictionary<uint, PathMissionProgress> activeMissions = new();
        private readonly Dictionary<uint, PathMissionProgress> completedMissions = new();

        private ushort currentEpisodeId;

        /// <summary>
        /// Create a new <see cref="PathManager"/> from <see cref="Player"/> database model.
        /// </summary>
        public PathManager(Player owner, CharacterModel model, IEntityFactory entityFactory, ICreatureInfoManager creatureInfoManager)
        {
            player                   = owner;
            this.entityFactory       = entityFactory;
            this.creatureInfoManager = creatureInfoManager;
            foreach (CharacterPathModel pathModel in model.Path)
                paths.Add((EntityPath)pathModel.Path, new PathEntry(pathModel));

            foreach (CharacterPathMissionModel missionModel in model.PathMission)
            {
                PathMissionEntry entry = GameTableManager.Instance.PathMission?.GetEntry(missionModel.MissionId);
                if (entry == null)
                    continue;

                var mission = new PathMissionProgress(entry, missionModel);
                if (mission.Completed)
                    completedMissions[entry.Id] = mission;
                else
                    activeMissions[entry.Id] = mission;
            }

            Validate();
        }

        private void Validate()
        {
            if (paths.Count != MaxPathCount)
            {
                // sanity checks to make sure a player always has entries for all paths
                if (paths.Count == 0)
                    SetPathEntry(player.Path, PathCreate(player.Path, true));

                for (EntityPath path = EntityPath.Soldier; path <= EntityPath.Explorer; path++)
                    if (GetPathEntry(path) == null)
                        SetPathEntry(path, PathCreate(path));
            }

            // TODO: Check for missing level up rewards.
        }

        /// <summary>
        /// Create a new <see cref="PathEntry"/>.
        /// </summary>
        private PathEntry PathCreate(EntityPath path, bool unlocked = false)
        {
            if (path > EntityPath.Explorer)
                return null;

            if (GetPathEntry(path) != null)
                throw new ArgumentException($"{path} is already added to the player!");

            var pathEntry = new PathEntry(
                player.CharacterId,
                path,
                unlocked
            );
            SetPathEntry(path, pathEntry);
            return pathEntry;
        }

        /// <summary>
        /// Checks to see if a <see cref="Player"/>'s <see cref="Path"/> is active
        /// </summary>
        /// <param name="pathToCheck"></param>
        /// <returns></returns>
        public bool IsPathActive(EntityPath pathToCheck)
        {
            return player.Path == pathToCheck;
        }

        /// <summary>
        /// Attempts to activate a <see cref="Player"/>'s <see cref="Path"/>
        /// </summary>
        /// <param name="pathToActivate"></param>
        /// <returns></returns>
        public void ActivatePath(EntityPath pathToActivate)
        {
            if (pathToActivate > EntityPath.Explorer)
                throw new ArgumentException("Path is not recognised.");

            if (!IsPathUnlocked(pathToActivate))
                throw new ArgumentException("Path is not unlocked.");

            if (IsPathActive(pathToActivate))
                throw new ArgumentException("Path is already active.");

            player.Path = pathToActivate;

            SendServerPathActivateResult(GenericError.Ok);
            SendSetUnitPathTypePacket();
            SendPathLogPacket();
            SendActivePathUpdateXp();
            SendProgressPackets(true);
        }

        /// <summary>
        /// Checks to see if a <see cref="Player"/>'s <see cref="Path"/> is mathced by a corresponding <see cref="PathUnlockedMask"/> flag
        /// </summary>
        /// <param name="pathToUnlock"></param>
        /// <returns></returns>
        public bool IsPathUnlocked(EntityPath pathToUnlock)
        {
            return GetPathEntry(pathToUnlock).Unlocked;
        }

        /// <summary>
        /// Attemps to adjust the <see cref="Player"/>'s <see cref="PathUnlockedMask"/> status
        /// </summary>
        /// <param name="pathToUnlock"></param>
        /// <returns></returns>
        public void UnlockPath(EntityPath pathToUnlock)
        {
            if (pathToUnlock > EntityPath.Explorer)
                throw new ArgumentException("Path is not recognised.");

            if (IsPathUnlocked(pathToUnlock))
                throw new ArgumentException("Path is already unlocked.");

            GetPathEntry(pathToUnlock).Unlocked = true;

            SendServerPathUnlockResult();
            SendPathLogPacket();
        }

        /// <summary>
        /// Add XP to the current <see cref="Path"/>
        /// </summary>
        /// <param name="xp"></param>
        public void AddXp(uint xp)
        {
            if (xp == 0)
                return;

            EntityPath path = player.Path;
            PathEntry entry = GetPathEntry(path);

            if (GetCurrentLevel(path) < MaxPathLevel)
            {
                uint maxXp = GetExperienceForLevel(path, MaxPathLevel);
                if (entry.TotalXp >= maxXp)
                    return;

                uint xpGained = Math.Min(xp, maxXp - entry.TotalXp);
                if (xpGained == 0)
                    return;

                checked
                {
                    entry.TotalXp += xpGained;
                }

                foreach (uint level in CheckForLevelUp(path, entry.TotalXp, xpGained))
                    GrantLevelUpReward(path, level);

                SendServerPathUpdateXp(entry.TotalXp);
                SendPathLogPacket();
            }

            // TODO: Reward Elder XP after achieving rank 30
        }

        /// <summary>
        /// Get the current <see cref="Path"/> level for the <see cref="Player"/>
        /// </summary>
        /// <param name="path">The path being checked</param>
        /// <returns></returns>
        public uint GetCurrentLevel()
        {
            return GetCurrentLevel(player.Path);
        }

        public uint GetCurrentLevel(EntityPath path)
        {
            PathLevelEntry entry = GameTableManager.Instance.PathLevel.Entries
                .Where(x => x.PathXP <= paths[path].TotalXp && x.PathTypeEnum == (uint)path)
                .OrderBy(x => x.PathXP)
                .LastOrDefault();

            return entry?.PathLevel ?? 0u;
        }

        /// <summary>
        /// Get the level based on an amount of XP
        /// </summary>
        /// <param name="xp">The XP value to get the level by</param>
        /// <returns></returns>
        private uint GetLevelByExperience(EntityPath path, uint xp)
        {
            PathLevelEntry entry = GameTableManager.Instance.PathLevel.Entries
                .Where(x => x.PathXP <= xp && x.PathTypeEnum == (uint)path)
                .OrderBy(x => x.PathXP)
                .LastOrDefault();

            return entry?.PathLevel ?? 0u;
        }

        private uint GetExperienceForLevel(EntityPath path, uint level)
        {
            PathLevelEntry entry = GameTableManager.Instance.PathLevel.Entries
                .Where(x => x.PathLevel <= level && x.PathTypeEnum == (uint)path)
                .OrderBy(x => x.PathLevel)
                .LastOrDefault();

            return entry?.PathXP ?? 0u;
        }

        /// <summary>
        /// Check to see if a level up should happen based on current XP and XP just earned.
        /// </summary>
        /// <param name="totalXp">Path XP after XP earned has been applied</param>
        /// <param name="xpGained">XP just earned</param>
        /// <returns></returns>
        private IEnumerable<uint> CheckForLevelUp(EntityPath path, uint totalXp, uint xpGained)
        {
            uint currentLevel = GetLevelByExperience(path, totalXp - xpGained);
            return GameTableManager.Instance.PathLevel.Entries
                .Where(x => x.PathLevel > currentLevel && x.PathXP <= totalXp && x.PathTypeEnum == (uint)path)
                .OrderBy(x => x.PathLevel)
                .Select(e => e.PathLevel);
        }

        /// <summary>
        /// Grants a player a level up reward for a <see cref="Path"/> and level
        /// </summary>
        /// <param name="path">The path to grant the reward for</param>
        /// <param name="level">The level to grant the reward for</param>
        private void GrantLevelUpReward(EntityPath path, uint level, bool castLevelUpSpell = true)
        {
            // TODO: look at this in more in depth, might be a better way to handle
            uint baseRewardObjectId = (uint)path * MaxPathLevel + 7u; // 7 is the base offset
            uint pathRewardObjectId = baseRewardObjectId + (Math.Clamp(level - 2, 0, 29)); // level - 2 is used because the objectIDs start at level 2 and a -2 offset was needed

            IEnumerable<PathRewardEntry> pathRewardEntries = GameTableManager.Instance.PathReward.Entries
                .Where(x => x.ObjectId == pathRewardObjectId);
            foreach (PathRewardEntry pathRewardEntry in pathRewardEntries)
            {
                if (pathRewardEntry.PathRewardFlags > 0)
                    continue;

                if (pathRewardEntry.PathRewardTypeEnum != 0)
                    continue;

                if (pathRewardEntry.Item2Id == 0 && pathRewardEntry.Spell4Id == 0 && pathRewardEntry.CharacterTitleId == 0 && pathRewardEntry.Quest2Id == 0)
                    continue;

                if (pathRewardEntry.PrerequisiteId > 0 && !PrerequisiteManager.Instance.Meets(player, pathRewardEntry.PrerequisiteId))
                    continue;

                GrantPathReward(pathRewardEntry);
            }

            GetPathEntry(path).LevelRewarded = (byte)level;
            if (castLevelUpSpell)
                player.CastSpell(53234, new Spell.SpellParameters());
        }

        /// <summary>
        /// Grant the <see cref="Player"/> rewards from the <see cref="PathRewardEntry"/>
        /// </summary>
        /// <param name="pathRewardEntry">The entry containing items, spells, or titles, to be rewarded"/></param>
        private void GrantPathReward(PathRewardEntry pathRewardEntry)
        {
            if (pathRewardEntry == null)
                throw new ArgumentNullException();

            // TODO: Check if there's bag space. Otherwise queue? Or is there an overflow inventory?
            if (pathRewardEntry.Item2Id > 0)
                player.Inventory.ItemCreate(InventoryLocation.Inventory, pathRewardEntry.Item2Id, pathRewardEntry.Count == 0 ? 1 : pathRewardEntry.Count, ItemUpdateReason.PathReward);

            if (pathRewardEntry.Spell4Id > 0)
            {
                Spell4Entry spell4Entry = GameTableManager.Instance.Spell4.GetEntry(pathRewardEntry.Spell4Id);
                player.SpellManager.AddSpell(spell4Entry.Spell4BaseIdBaseSpell);
            }

            if (pathRewardEntry.Quest2Id > 0)
                player.QuestManager.QuestMention((ushort)pathRewardEntry.Quest2Id);

            if (pathRewardEntry.CharacterTitleId > 0)
                player.TitleManager.AddTitle((ushort)pathRewardEntry.CharacterTitleId);
        }

        private PathUnlockedMask GetPathUnlockedMask()
        {
            PathUnlockedMask mask = PathUnlockedMask.None;
            foreach (PathEntry entry in paths.Values)
                if (entry.Unlocked)
                    mask |= (PathUnlockedMask)(1 << (int)entry.Path);

            return mask;
        }

        /// <summary>
        /// Execute a DB Save of the <see cref="CharacterContext"/>
        /// </summary>
        /// <param name="context"></param>
        public void Save(CharacterContext context)
        {
            foreach (PathEntry pathEntry in paths.Values)
                pathEntry.Save(context);

            foreach (PathMissionProgress mission in activeMissions.Values.Concat(completedMissions.Values))
                mission.Save(player.CharacterId, context);
        }

        public void SendInitialPackets()
        {
            GrantMissingLevelRewards();
            ResetStaleConquerTheYetiProgress();
            SendSetUnitPathTypePacket();
            SendPathLogPacket();
            SendActivePathUpdateXp();
            SendProgressPackets(false);
        }

        public void RefreshLocation()
        {
            SendProgressPackets(true);
        }

        public void RefreshCurrentEpisode()
        {
            RefreshActiveMissions();

            if (currentEpisodeId == 0)
                return;

            SendPathCurrentEpisodePacket();
        }

        public void RefreshEpisodeProgress()
        {
            if (currentEpisodeId == 0)
                RefreshActiveMissions();

            if (currentEpisodeId == 0)
                return;

            SendPathEpisodeProgressPacket();
        }

        public void RefreshEpisodeProgressThenCurrent()
        {
            RefreshActiveMissions();

            if (currentEpisodeId == 0)
                return;

            SendPathEpisodeProgressPacket();
            SendPathCurrentEpisodePacket();
        }

        public void SendLoginPathRefresh()
        {
            bool logConquerTheYeti = ShouldLogConquerTheYetiLoginRefresh();
            bool sent              = DebugSendProgressPackets(false, false, true);

            if (logConquerTheYeti)
                log.Info($"PathMission M156 login-refresh player={player.CharacterId} safeBatch={sent} includeM156=True activate=False");
        }

        private void GrantMissingLevelRewards()
        {
            foreach (PathEntry entry in paths.Values)
            {
                uint currentLevel = GetCurrentLevel(entry.Path);
                for (uint level = Math.Max((uint)entry.LevelRewarded + 1u, 2u); level <= currentLevel; level++)
                    GrantLevelUpReward(entry.Path, level, false);
            }
        }

        private void SendProgressPackets(bool sendRefresh)
        {
            SendProgressPackets(sendRefresh, false);
        }

        private void SendProgressPackets(bool sendRefresh, bool force)
        {
            SendProgressPackets(sendRefresh, force, false);
        }

        private void SendProgressPackets(bool sendRefresh, bool force, bool includeConquerTheYeti)
        {
            if (!force && ShouldSuppressConquerTheYetiAutomaticPathProgress())
                return;

            RefreshActiveMissions(includeConquerTheYeti);

            if (sendRefresh)
                SendPathRefreshPacket();

            if (currentEpisodeId == 0)
                return;

            SendPathEpisodeProgressPacket(includeConquerTheYeti);
            SendPathCurrentEpisodePacket();
            SendPathMissionActivatePacket();
        }

        private void SendPathCurrentEpisodeAndProgressPackets(bool sendRefresh)
        {
            SendPathCurrentEpisodeAndProgressPackets(sendRefresh, false);
        }

        private void SendPathCurrentEpisodeAndProgressPackets(bool sendRefresh, bool includeConquerTheYeti)
        {
            RefreshActiveMissions(includeConquerTheYeti);

            if (sendRefresh)
                SendPathRefreshPacket();

            if (currentEpisodeId == 0)
                return;

            SendPathEpisodeProgressPacket(includeConquerTheYeti);
            SendPathCurrentEpisodePacket();
        }

        private void RefreshActiveMissions(bool includeConquerTheYeti = false)
        {
            PathEpisodeEntry episode = GetCurrentEpisode();
            currentEpisodeId = (ushort)(episode?.Id ?? 0u);
            if (episode == null)
            {
                if (includeConquerTheYeti)
                    LogConquerTheYetiPathRefresh("no episode");
                return;
            }

            if (GameTableManager.Instance.PathMission == null)
            {
                if (includeConquerTheYeti)
                    LogConquerTheYetiPathRefresh("no PathMission table");
                return;
            }

            if (includeConquerTheYeti)
                LogConquerTheYetiPathRefresh($"episode={episode.Id}");

            foreach (PathMissionEntry entry in GameTableManager.Instance.PathMission.Entries
                .Where(m => m.PathEpisodeId == episode.Id && m.PathTypeEnum == (uint)player.Path))
            {
                if (entry.Id == ConquerTheYetiMissionId && !includeConquerTheYeti)
                    continue;

                if (activeMissions.ContainsKey(entry.Id) || completedMissions.ContainsKey(entry.Id))
                {
                    LogConquerTheYetiCandidate(entry, "already tracked");
                    continue;
                }

                if (!CanActivateMission(entry))
                {
                    LogConquerTheYetiCandidate(entry, "CanActivate=false");
                    continue;
                }

                activeMissions.Add(entry.Id, new PathMissionProgress(entry));
                LogConquerTheYetiCandidate(entry, "added");
            }
        }

        private PathEpisodeEntry GetCurrentEpisode()
        {
            if (player.Map == null || GameTableManager.Instance.PathEpisode == null)
                return null;

            PathEpisodeEntry[] episodes = GameTableManager.Instance.PathEpisode.Entries
                .Where(e => e.PathTypeEnum == (uint)player.Path && e.WorldId == player.Map.Entry.Id)
                .ToArray();

            if (episodes.Length == 0)
                return null;

            uint? worldZoneId = GetPlayerWorldZoneId();
            if (worldZoneId.HasValue)
            {
                foreach (uint zoneId in GetWorldZoneHierarchy(worldZoneId.Value))
                {
                    PathEpisodeEntry episode = episodes.FirstOrDefault(e => e.WorldZoneId == zoneId);
                    if (episode != null)
                        return episode;
                }
            }

            PathEpisodeEntry missionZoneEpisode = GetCurrentEpisodeFromMissionZone(episodes, worldZoneId);
            if (missionZoneEpisode != null)
                return missionZoneEpisode;

            return episodes.Length == 1 ? episodes[0] : null;
        }

        private PathEpisodeEntry GetCurrentEpisodeFromMissionZone(IReadOnlyCollection<PathEpisodeEntry> episodes, uint? playerWorldZoneId)
        {
            if (GameTableManager.Instance.PathMission == null || episodes.Count == 0 || !playerWorldZoneId.HasValue)
                return null;

            Dictionary<uint, PathEpisodeEntry> episodesById = episodes.ToDictionary(e => e.Id);
            foreach (PathMissionEntry mission in GameTableManager.Instance.PathMission.Entries
                .Where(m => m.PathTypeEnum == (uint)player.Path && episodesById.ContainsKey(m.PathEpisodeId)))
            {
                List<WorldLocation2Entry> locations = GetMissionWorldLocations(mission).ToList();
                if (locations.Count == 0 || locations.All(l => l.WorldId != player.Map.Entry.Id))
                    continue;

                PathEpisodeEntry episode = episodesById[mission.PathEpisodeId];
                if (!IsPlayerInEpisodeOrMissionZone(playerWorldZoneId.Value, episode, locations))
                    continue;

                if (!activeMissions.ContainsKey(mission.Id) && !completedMissions.ContainsKey(mission.Id) && !CanActivateMission(mission))
                    continue;

                return episode;
            }

            return null;
        }

        private uint? GetPlayerWorldZoneId()
        {
            uint? worldZoneId = player.Zone?.Id;
            return worldZoneId ?? player.Map?.File.GetWorldAreaId(player.Position);
        }

        private bool IsPlayerInEpisodeOrMissionZone(uint playerWorldZoneId, PathEpisodeEntry episode, IEnumerable<WorldLocation2Entry> locations)
        {
            if (IsWorldZoneInHierarchy(playerWorldZoneId, episode.WorldZoneId))
                return true;

            return locations.Any(location =>
                location.WorldZoneId != 0u
                && (location.WorldZoneId == playerWorldZoneId
                    || IsWorldZoneInHierarchy(playerWorldZoneId, location.WorldZoneId)
                    || IsWorldZoneInHierarchy(location.WorldZoneId, playerWorldZoneId)));
        }

        private bool IsEntityInMissionLocation(IWorldEntity entity, WorldLocation2Entry location)
        {
            if (entity == null || location == null || player.Map == null || location.WorldId != player.Map.Entry.Id)
                return false;

            return IsPositionInMissionLocation(entity.Position, location);
        }

        private static bool IsPositionInMissionLocation(Vector3 position, WorldLocation2Entry location)
        {
            float radius = location.Radius > 0f ? location.Radius : 128f;
            float x = position.X - location.Position0;
            float z = position.Z - location.Position2;

            if (x * x + z * z > radius * radius)
                return false;

            if (location.MaxVerticalDistance <= 0f)
                return true;

            return Math.Abs(position.Y - location.Position1) <= location.MaxVerticalDistance;
        }

        private bool IsWorldZoneInHierarchy(uint worldZoneId, uint ancestorWorldZoneId)
        {
            if (ancestorWorldZoneId == 0u)
                return false;

            return GetWorldZoneHierarchy(worldZoneId).Contains(ancestorWorldZoneId);
        }

        private IEnumerable<uint> GetWorldZoneHierarchy(uint worldZoneId)
        {
            var seen = new HashSet<uint>();
            while (worldZoneId != 0u && seen.Add(worldZoneId))
            {
                yield return worldZoneId;

                WorldZoneEntry worldZone = GameTableManager.Instance.WorldZone?.GetEntry(worldZoneId);
                if (worldZone == null)
                    yield break;

                worldZoneId = worldZone.ParentZoneId;
            }
        }

        private bool CanActivateMission(PathMissionEntry entry)
        {
            if (entry.PrerequisiteId != 0u && !PrerequisiteManager.Instance.Meets(player, entry.PrerequisiteId))
                return false;

            if (player.Map == null)
                return false;

            PathEpisodeEntry episode = GameTableManager.Instance.PathEpisode?.GetEntry(entry.PathEpisodeId);
            if (episode == null || episode.WorldId != player.Map.Entry.Id)
                return false;

            List<WorldLocation2Entry> locations = GetMissionWorldLocations(entry).ToList();
            if (locations.Count != 0 && locations.All(l => l.WorldId != player.Map.Entry.Id))
                return false;

            return HasRequiredMissionSpawns(entry, locations);
        }

        private IEnumerable<WorldLocation2Entry> GetMissionWorldLocations(PathMissionEntry entry)
        {
            var locationIds = new HashSet<uint>();
            AddLocationId(locationIds, entry.WorldLocation2Id00);
            AddLocationId(locationIds, entry.WorldLocation2Id01);
            AddLocationId(locationIds, entry.WorldLocation2Id02);
            AddLocationId(locationIds, entry.WorldLocation2Id03);

            switch ((PathMissionType)entry.PathMissionTypeEnum)
            {
                case PathMissionType.SoldierEvent:
                    foreach (PathSoldierTowerDefenseEntry towerDefense in GameTableManager.Instance.PathSoldierTowerDefense?.Entries ?? Enumerable.Empty<PathSoldierTowerDefenseEntry>())
                        if (towerDefense.PathSoldierEventId == entry.ObjectId)
                            AddLocationId(locationIds, towerDefense.WorldLocation2IdDisplay);
                    break;
                case PathMissionType.ExplorerArea:
                case PathMissionType.ExplorerNode:
                    foreach (PathExplorerNodeEntry node in GameTableManager.Instance.PathExplorerNode?.Entries ?? Enumerable.Empty<PathExplorerNodeEntry>())
                        if (node.PathExplorerAreaId == entry.ObjectId)
                            AddLocationId(locationIds, node.WorldLocation2Id);
                    break;
                case PathMissionType.ExplorerDoor:
                    foreach (PathExplorerDoorEntranceEntry entrance in GameTableManager.Instance.PathExplorerDoorEntrance?.Entries ?? Enumerable.Empty<PathExplorerDoorEntranceEntry>())
                        if (entrance.PathExplorerDoorId == entry.ObjectId)
                            AddLocationId(locationIds, entrance.WorldLocation2IdSurfaceRevealed);
                    break;
                case PathMissionType.ExplorerScavengerHunt:
                    AddScavengerHuntLocationIds(locationIds, GameTableManager.Instance.PathExplorerScavengerHunt?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.ExplorerPowerMap:
                    AddLocationId(locationIds, GameTableManager.Instance.PathExplorerPowerMap?.GetEntry(entry.ObjectId)?.WorldLocation2IdVisual ?? 0u);
                    break;
                case PathMissionType.ScientistFieldStudy:
                    AddFieldStudyLocationIds(locationIds, GameTableManager.Instance.PathScientistFieldStudy?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.ScientistSpecimenSurvey:
                    AddSpecimenSurveyLocationIds(locationIds, GameTableManager.Instance.PathScientistSpecimenSurvey?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.ScientistDatacubeDiscovery:
                    AddDatacubeDiscoveryLocationIds(locationIds, GameTableManager.Instance.PathScientistDatacubeDiscovery?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.SettlerHub:
                    AddSettlerHubLocationIds(locationIds, GameTableManager.Instance.PathSettlerHub?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.SettlerMayor:
                    AddSettlerMayorLocationIds(locationIds, GameTableManager.Instance.PathSettlerMayor?.GetEntry(entry.ObjectId));
                    break;
            }

            foreach (uint locationId in locationIds)
            {
                WorldLocation2Entry location = GameTableManager.Instance.WorldLocation2.GetEntry(locationId);
                if (location != null)
                    yield return location;
            }
        }

        private bool HasRequiredMissionSpawns(PathMissionEntry entry, IReadOnlyCollection<WorldLocation2Entry> locations)
        {
            var creatureIds = new HashSet<uint>();
            AddMissionCreatureIds(creatureIds, entry);

            if (creatureIds.Count == 0)
                return true;

            IEntityCache entityCache = EntityCacheManager.Instance.GetEntityCache((ushort)player.Map.Entry.Id);
            if (locations.Count != 0 && entityCache.HasAnyCreatureInLocation(creatureIds, locations))
                return true;

            return entityCache.HasAnyCreature(creatureIds);
        }

        private void AddMissionCreatureIds(HashSet<uint> creatureIds, PathMissionEntry entry)
        {
            foreach (Creature2Entry creature in GameTableManager.Instance.Creature2.Entries.Where(c => c.PathMissionIdSoldier == entry.Id))
                AddCreatureId(creatureIds, creature.Id);

            switch ((PathMissionType)entry.PathMissionTypeEnum)
            {
                case PathMissionType.SoldierAssassinate:
                    AddSoldierAssassinateCreatureIds(creatureIds, GameTableManager.Instance.PathSoldierAssassinate?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.SoldierActivate:
                case PathMissionType.SoldierActivateChecklist:
                    AddSoldierActivateCreatureIds(creatureIds, GameTableManager.Instance.PathSoldierActivate?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.SoldierSwat:
                    AddSoldierSwatCreatureIds(creatureIds, GameTableManager.Instance.PathSoldierSWAT?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.ExplorerActivate:
                    AddExplorerActivateCreatureIds(creatureIds, GameTableManager.Instance.PathExplorerActivate?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.ExplorerDoor:
                    AddExplorerDoorCreatureIds(creatureIds, GameTableManager.Instance.PathExplorerDoor?.GetEntry(entry.ObjectId), entry.ObjectId);
                    break;
                case PathMissionType.ExplorerScavengerHunt:
                    AddScavengerHuntCreatureIds(creatureIds, GameTableManager.Instance.PathExplorerScavengerHunt?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.ScientistFieldStudy:
                    AddScientistFieldStudyCreatureIds(creatureIds, GameTableManager.Instance.PathScientistFieldStudy?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.ScientistScan:
                case PathMissionType.ScientistExperimentation:
                    AddScientistExperimentCreatureIds(creatureIds, entry);
                    break;
                case PathMissionType.ScientistDatacubeDiscovery:
                    AddDatacubeDiscoveryCreatureIds(creatureIds, GameTableManager.Instance.PathScientistDatacubeDiscovery?.GetEntry(entry.ObjectId));
                    break;
                case PathMissionType.SettlerHub:
                    AddSettlerHubCreatureIds(creatureIds, entry.ObjectId);
                    break;
                case PathMissionType.SettlerInfrastructure:
                    AddSettlerInfrastructureCreatureIds(creatureIds, GameTableManager.Instance.PathSettlerInfrastructure?.GetEntry(entry.ObjectId));
                    break;
            }
        }

        private static void AddLocationId(HashSet<uint> locationIds, uint locationId)
        {
            if (locationId != 0u)
                locationIds.Add(locationId);
        }

        private void AddScavengerHuntLocationIds(HashSet<uint> locationIds, PathExplorerScavengerHuntEntry entry)
        {
            foreach (PathExplorerScavengerClueEntry clue in GetScavengerHuntClues(entry))
                AddLocationId(locationIds, clue.WorldLocation2IdMiniMap);
        }

        private static void AddFieldStudyLocationIds(HashSet<uint> locationIds, PathScientistFieldStudyEntry entry)
        {
            if (entry == null)
                return;

            foreach (uint locationId in new[]
            {
                entry.WorldLocation2IdIndicator00,
                entry.WorldLocation2IdIndicator01,
                entry.WorldLocation2IdIndicator02,
                entry.WorldLocation2IdIndicator03,
                entry.WorldLocation2IdIndicator04,
                entry.WorldLocation2IdIndicator05,
                entry.WorldLocation2IdIndicator06,
                entry.WorldLocation2IdIndicator07
            })
                AddLocationId(locationIds, locationId);
        }

        private static void AddSpecimenSurveyLocationIds(HashSet<uint> locationIds, PathScientistSpecimenSurveyEntry entry)
        {
            if (entry == null)
                return;

            foreach (uint locationId in new[]
            {
                entry.WorldLocation2Id00,
                entry.WorldLocation2Id01,
                entry.WorldLocation2Id02,
                entry.WorldLocation2Id03,
                entry.WorldLocation2Id04,
                entry.WorldLocation2Id05,
                entry.WorldLocation2Id06,
                entry.WorldLocation2Id07,
                entry.WorldLocation2Id08,
                entry.WorldLocation2Id09
            })
                AddLocationId(locationIds, locationId);
        }

        private void AddDatacubeDiscoveryLocationIds(HashSet<uint> locationIds, PathScientistDatacubeDiscoveryEntry entry)
        {
            if (entry == null)
                return;

            foreach (DatacubeEntry datacube in GameTableManager.Instance.Datacube?.Entries ?? Enumerable.Empty<DatacubeEntry>())
                if (datacube.WorldZoneId == entry.WorldZoneId)
                    AddLocationId(locationIds, datacube.WorldLocation2Id);
        }

        private void AddSettlerHubLocationIds(HashSet<uint> locationIds, PathSettlerHubEntry entry)
        {
            if (entry == null)
                return;

            foreach (uint locationId in new[]
            {
                entry.WorldLocation2IdMapResource00Loc00,
                entry.WorldLocation2IdMapResource00Loc01,
                entry.WorldLocation2IdMapResource00Loc02,
                entry.WorldLocation2IdMapResource00Loc03,
                entry.WorldLocation2IdMapResource01Loc00,
                entry.WorldLocation2IdMapResource01Loc01,
                entry.WorldLocation2IdMapResource01Loc02,
                entry.WorldLocation2IdMapResource01Loc03,
                entry.WorldLocation2IdMapResource02Loc00,
                entry.WorldLocation2IdMapResource02Loc01,
                entry.WorldLocation2IdMapResource02Loc02,
                entry.WorldLocation2IdMapResource02Loc03
            })
                AddLocationId(locationIds, locationId);

            foreach (PathSettlerImprovementGroupEntry group in GameTableManager.Instance.PathSettlerImprovementGroup?.Entries ?? Enumerable.Empty<PathSettlerImprovementGroupEntry>())
                if (group.PathSettlerHubId == entry.Id)
                    AddLocationId(locationIds, group.WorldLocation2IdDisplayPoint);
        }

        private static void AddSettlerMayorLocationIds(HashSet<uint> locationIds, PathSettlerMayorEntry entry)
        {
            if (entry == null)
                return;

            foreach (uint locationId in new[]
            {
                entry.WorldLocation2Id00,
                entry.WorldLocation2Id01,
                entry.WorldLocation2Id02,
                entry.WorldLocation2Id03,
                entry.WorldLocation2Id04,
                entry.WorldLocation2Id05,
                entry.WorldLocation2Id06,
                entry.WorldLocation2Id07
            })
                AddLocationId(locationIds, locationId);
        }

        private static void AddCreatureId(HashSet<uint> creatureIds, uint creatureId)
        {
            if (creatureId != 0u)
                creatureIds.Add(creatureId);
        }

        private static void AddTargetGroupCreatureIds(HashSet<uint> creatureIds, uint targetGroupId)
        {
            if (targetGroupId == 0u)
                return;

            foreach (uint creatureId in AssetManager.Instance.GetCreatureIdsForTargetGroup(targetGroupId))
                AddCreatureId(creatureIds, creatureId);
        }

        private static void AddSoldierActivateCreatureIds(HashSet<uint> creatureIds, PathSoldierActivateEntry entry)
        {
            if (entry == null)
                return;

            AddCreatureId(creatureIds, entry.Creature2Id);
            AddTargetGroupCreatureIds(creatureIds, entry.TargetGroupId);
        }

        private static void AddSoldierAssassinateCreatureIds(HashSet<uint> creatureIds, PathSoldierAssassinateEntry entry)
        {
            if (entry == null)
                return;

            AddCreatureId(creatureIds, entry.Creature2Id);
            AddTargetGroupCreatureIds(creatureIds, entry.TargetGroupId);
        }

        private static void AddSoldierSwatCreatureIds(HashSet<uint> creatureIds, PathSoldierSWATEntry entry)
        {
            if (entry == null)
                return;

            AddCreatureId(creatureIds, entry.Creature2Id);
            AddTargetGroupCreatureIds(creatureIds, entry.TargetGroupId);
        }

        private static void AddExplorerActivateCreatureIds(HashSet<uint> creatureIds, PathExplorerActivateEntry entry)
        {
            if (entry == null)
                return;

            AddCreatureId(creatureIds, entry.Creature2Id);
            AddTargetGroupCreatureIds(creatureIds, entry.TargetGroupId);
        }

        private void AddExplorerDoorCreatureIds(HashSet<uint> creatureIds, PathExplorerDoorEntry entry, uint doorId)
        {
            if (entry != null)
            {
                AddTargetGroupCreatureIds(creatureIds, entry.TargetGroupIdActivate);
                AddTargetGroupCreatureIds(creatureIds, entry.TargetGroupIdKill);
            }

            foreach (PathExplorerDoorEntranceEntry entrance in GameTableManager.Instance.PathExplorerDoorEntrance?.Entries ?? Enumerable.Empty<PathExplorerDoorEntranceEntry>())
            {
                if (entrance.PathExplorerDoorId != doorId)
                    continue;

                AddCreatureId(creatureIds, entrance.Creature2IdSurface);
                AddCreatureId(creatureIds, entrance.Creature2IdMicro);
            }
        }

        private void AddScavengerHuntCreatureIds(HashSet<uint> creatureIds, PathExplorerScavengerHuntEntry entry)
        {
            if (entry == null)
                return;

            AddCreatureId(creatureIds, entry.Creature2IdStart);
            foreach (PathExplorerScavengerClueEntry clue in GetScavengerHuntClues(entry))
            {
                AddCreatureId(creatureIds, clue.Creature2Id);
                AddTargetGroupCreatureIds(creatureIds, clue.TargetGroupId);
            }
        }

        private IEnumerable<PathExplorerScavengerClueEntry> GetScavengerHuntClues(PathExplorerScavengerHuntEntry entry)
        {
            if (entry == null)
                yield break;

            foreach (uint clueId in new[]
            {
                entry.PathExplorerScavengerClueId00,
                entry.PathExplorerScavengerClueId01,
                entry.PathExplorerScavengerClueId02,
                entry.PathExplorerScavengerClueId03,
                entry.PathExplorerScavengerClueId04,
                entry.PathExplorerScavengerClueId05,
                entry.PathExplorerScavengerClueId06
            })
            {
                PathExplorerScavengerClueEntry clue = GameTableManager.Instance.PathExplorerScavengerClue?.GetEntry(clueId);
                if (clue != null)
                    yield return clue;
            }
        }

        private static void AddScientistFieldStudyCreatureIds(HashSet<uint> creatureIds, PathScientistFieldStudyEntry entry)
        {
            if (entry == null)
                return;

            AddCreatureId(creatureIds, entry.Creature2Id);
            AddTargetGroupCreatureIds(creatureIds, entry.TargetGroupId);
        }

        private void AddScientistExperimentCreatureIds(HashSet<uint> creatureIds, PathMissionEntry entry)
        {
            var experimentationIds = new HashSet<uint>();
            if ((PathMissionType)entry.PathMissionTypeEnum == PathMissionType.ScientistExperimentation)
                experimentationIds.Add(entry.ObjectId);

            foreach (PathScientistExperimentationPatternEntry pattern in GameTableManager.Instance.PathScientistExperimentationPattern?.Entries ?? Enumerable.Empty<PathScientistExperimentationPatternEntry>())
                if (pattern.PathMissionId == entry.Id)
                    experimentationIds.Add(pattern.PathScientistExperimentationId);

            foreach (Creature2Entry creature in GameTableManager.Instance.Creature2.Entries.Where(c => experimentationIds.Contains(c.PathScientistExperimentationId)))
                AddCreatureId(creatureIds, creature.Id);
        }

        private void AddDatacubeDiscoveryCreatureIds(HashSet<uint> creatureIds, PathScientistDatacubeDiscoveryEntry entry)
        {
            if (entry == null)
                return;

            HashSet<uint> datacubeIds = GameTableManager.Instance.Datacube?.Entries
                .Where(d => d.WorldZoneId == entry.WorldZoneId)
                .Select(d => d.Id)
                .ToHashSet() ?? new HashSet<uint>();

            foreach (Creature2Entry creature in GameTableManager.Instance.Creature2.Entries.Where(c => datacubeIds.Contains(c.DatacubeId)))
                AddCreatureId(creatureIds, creature.Id);
        }

        private void AddSettlerHubCreatureIds(HashSet<uint> creatureIds, uint hubId)
        {
            foreach (PathSettlerImprovementGroupEntry group in GameTableManager.Instance.PathSettlerImprovementGroup?.Entries ?? Enumerable.Empty<PathSettlerImprovementGroupEntry>())
                if (group.PathSettlerHubId == hubId)
                    AddCreatureId(creatureIds, group.Creature2IdDepot);

            foreach (PathSettlerInfrastructureEntry infrastructure in GameTableManager.Instance.PathSettlerInfrastructure?.Entries ?? Enumerable.Empty<PathSettlerInfrastructureEntry>())
            {
                if (infrastructure.PathSettlerHubId00 != hubId && infrastructure.PathSettlerHubId01 != hubId)
                    continue;

                AddSettlerInfrastructureCreatureIds(creatureIds, infrastructure);
            }
        }

        private static void AddSettlerInfrastructureCreatureIds(HashSet<uint> creatureIds, PathSettlerInfrastructureEntry entry)
        {
            if (entry == null)
                return;

            AddCreatureId(creatureIds, entry.Creature2IdDepot);
            AddCreatureId(creatureIds, entry.Creature2IdResource00);
            AddCreatureId(creatureIds, entry.Creature2IdResource01);
            AddCreatureId(creatureIds, entry.Creature2IdResource02);
        }

        public bool HasCompletedMission(uint missionId)
        {
            return completedMissions.ContainsKey(missionId);
        }

        public bool HasStartedMission(uint missionId)
        {
            if (completedMissions.ContainsKey(missionId))
                return true;

            return activeMissions.TryGetValue(missionId, out PathMissionProgress mission)
                && (mission.UserData != 0u || mission.StateData != 0u);
        }

        public bool TryStartMission(uint missionId, uint stateData = 1u, uint giverUnitId = 0u)
        {
            if (HasStartedMission(missionId))
                return false;

            PathMissionProgress mission = GetOrCreateMission(missionId, false);
            if (mission == null || mission.Completed)
                return false;

            SendPathMissionActivatePacket(mission, giverUnitId);

            if (stateData != 0u)
                mission.StateData = stateData;

            SendPathMissionUpdatePacket(mission);
            return true;
        }

        public uint GetCompletedMissionCount(EntityPath? path = null)
        {
            return (uint)completedMissions.Values
                .Count(m => path == null || m.Entry.PathTypeEnum == (uint)path.Value);
        }

        public uint GetCompletedMissionCountForEpisode(uint episodeId)
        {
            return (uint)completedMissions.Values
                .Count(m => m.Entry.PathEpisodeId == episodeId);
        }

        public void IncrementMission(uint missionId, uint amount = 1u, uint stateData = 0u, bool complete = false)
        {
            PathMissionProgress mission = GetOrCreateMission(missionId);
            if (mission == null || mission.Completed)
                return;

            if (amount != 0u)
                mission.UserData += amount;

            if (stateData != 0u)
                mission.StateData = stateData;

            if (complete)
                mission.Completed = true;

            SendPathMissionUpdatePacket(mission);

            if (!mission.Completed)
            {
                if (ShouldRefreshEpisodeProgressAfterMissionUpdate(mission))
                    RefreshEpisodeProgress();
                return;
            }

            activeMissions.Remove(mission.Entry.Id);
            completedMissions[mission.Entry.Id] = mission;
            GrantMissionCompletionReward(mission);
            if (ShouldRefreshEpisodeProgressThenCurrentAfterMissionCompletion(mission))
                RefreshEpisodeProgressThenCurrent();
            else if (ShouldRefreshEpisodeProgressAfterMissionUpdate(mission))
                RefreshEpisodeProgress();
        }

        public void CompleteMission(uint missionId)
        {
            IncrementMission(missionId, 0u, 0u, true);
        }

        public bool ReportExplorerProgress(uint missionId, uint explorerNodeIndex)
        {
            PathMissionProgress mission = GetOrCreateMission(missionId);
            if (mission == null || mission.Completed)
                return false;

            PathMissionType missionType = (PathMissionType)mission.Entry.PathMissionTypeEnum;
            if (missionType == PathMissionType.ExplorerUnknown)
                return ReportCartographyProgress(mission);

            if (missionType != PathMissionType.ExplorerNode)
                return false;

            PathExplorerNodeEntry node = GetExplorerNode(mission.Entry, explorerNodeIndex);
            if (node == null)
                return false;

            WorldLocation2Entry location = GameTableManager.Instance.WorldLocation2?.GetEntry(node.WorldLocation2Id);
            if (!CanReportExplorerNode(location))
                return false;

            bool spawned = SpawnExplorerBeacon(mission.Entry.Id, location);
            IncrementMission(missionId, 100u, explorerNodeIndex + 1u, true);

            log.Info($"PathExplorerProgress player={player.CharacterId} mission={missionId} nodeIndex={explorerNodeIndex} node={node.Id} spawnedBeacon={spawned}");
            return true;
        }

        private bool ReportCartographyProgress(PathMissionProgress mission)
        {
            if (!CanReportCartographyMission(mission.Entry))
                return false;

            IncrementMission(mission.Entry.Id, 100u, 1u, true);

            uint zoneId = GetPlayerWorldZoneId().GetValueOrDefault();
            log.Info($"PathCartographyProgress player={player.CharacterId} mission={mission.Entry.Id} zone={zoneId}");
            return true;
        }

        private bool CanReportCartographyMission(PathMissionEntry entry)
        {
            if (player.Map == null)
                return false;

            PathEpisodeEntry episode = GameTableManager.Instance.PathEpisode?.GetEntry(entry.PathEpisodeId);
            if (episode == null || episode.WorldId != player.Map.Entry.Id)
                return false;

            uint? worldZoneId = GetPlayerWorldZoneId();
            return worldZoneId.HasValue
                && IsWorldZoneInHierarchy(worldZoneId.Value, episode.WorldZoneId);
        }

        private static PathExplorerNodeEntry GetExplorerNode(PathMissionEntry entry, uint explorerNodeIndex)
        {
            if (GameTableManager.Instance.PathExplorerNode == null)
                return null;

            PathExplorerNodeEntry[] nodes = GameTableManager.Instance.PathExplorerNode.Entries
                .Where(n => n.PathExplorerAreaId == entry.ObjectId)
                .OrderBy(n => n.Id)
                .ToArray();

            return explorerNodeIndex < (uint)nodes.Length ? nodes[(int)explorerNodeIndex] : null;
        }

        private bool CanReportExplorerNode(WorldLocation2Entry location)
        {
            if (location == null || player.Map == null)
                return false;

            return location.WorldId == player.Map.Entry.Id
                && IsPositionInMissionLocation(player.Position, location);
        }

        private bool SpawnExplorerBeacon(uint missionId, WorldLocation2Entry location)
        {
            uint beaconCreatureId = PathMissionContentManager.Instance.GetExplorerBeaconCreatureId(missionId);
            if (player.Map == null || beaconCreatureId == 0u || GameTableManager.Instance.Creature2.GetEntry(beaconCreatureId) == null)
                return false;

            ISimpleEntity entity = entityFactory.CreateEntity<ISimpleEntity>();
            if (entity == null)
                return false;

            Vector3 position = new(location.Position0, location.Position1, location.Position2);
            EntityModel model = BuildExplorerBeaconModel(location, position, beaconCreatureId);
            ICreatureInfo creatureInfo = creatureInfoManager.GetCreatureInfo(beaconCreatureId);
            if (creatureInfo == null)
                return false;

            entity.Initialise(creatureInfo, model);
            entity.CreateFlags |= EntityCreateFlag.SpawnAnimation;
            player.Map.EnqueueAdd(entity, position);
            return true;
        }

        private EntityModel BuildExplorerBeaconModel(WorldLocation2Entry location, Vector3 position, uint beaconCreatureId)
        {
            return new EntityModel
            {
                Type        = EntityType.Simple,
                Creature    = beaconCreatureId,
                World       = (ushort)player.Map.Entry.Id,
                Area        = (ushort)(location.WorldZoneId != 0u ? location.WorldZoneId : GetPlayerWorldZoneId().GetValueOrDefault()),
                X           = position.X,
                Y           = position.Y,
                Z           = position.Z,
                DisplayInfo = GetDisplayInfoId(beaconCreatureId),
                Faction1    = GetFactionId(beaconCreatureId),
                Faction2    = GetFactionId(beaconCreatureId)
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

        private static ushort GetFactionId(uint creatureId)
        {
            Creature2Entry entry = GameTableManager.Instance.Creature2.GetEntry(creatureId);
            return entry != null && (uint)entry.FactionId <= ushort.MaxValue ? (ushort)entry.FactionId : (ushort)0;
        }

        private void GrantMissionCompletionReward(PathMissionProgress mission)
        {
            uint xp = GetMissionCompletionXp(mission.Entry.Id);
            if (xp == 0u)
                return;

            AddXp(xp);
        }

        private static uint GetMissionCompletionXp(uint missionId)
        {
            return PathMissionContentManager.Instance.GetCompletionXp(missionId);
        }

        private PathMissionProgress GetOrCreateMission(uint missionId, bool sendActivate = true)
        {
            if (activeMissions.TryGetValue(missionId, out PathMissionProgress mission))
                return CanActivateMission(mission.Entry) ? mission : null;

            if (completedMissions.TryGetValue(missionId, out mission))
                return mission;

            PathMissionEntry entry = GameTableManager.Instance.PathMission?.GetEntry(missionId);
            if (entry == null)
                return null;

            if (entry.PathTypeEnum != (uint)player.Path)
                return null;

            if (!CanActivateMission(entry))
                return null;

            mission = new PathMissionProgress(entry);
            activeMissions.Add(entry.Id, mission);
            if (sendActivate)
                SendPathMissionActivatePacket(mission);

            return mission;
        }

        /// <summary>
        /// Used to update the Player's Path Log.
        /// </summary>
        private void SendPathLogPacket()
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathInitialise
            {
                ActivePath = player.Path,
                PathProgress = Enumerable.Range(0, (int)MaxPathCount)
                    .Select(p => GetPathEntry((EntityPath)p)?.TotalXp ?? 0u)
                    .ToArray(),
                PathUnlockedMask = GetPathUnlockedMask(),
                TimeSinceLastActivateInDays = GetCooldownTime() // TODO: Need to figure out timestamp calculations necessary for this value to update the client appropriately
            });
        }

        private void SendActivePathUpdateXp()
        {
            SendServerPathUpdateXp(GetPathEntry(player.Path)?.TotalXp ?? 0u);
        }

        private void SendPathRefreshPacket()
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathRefresh());
        }

        private void SendPathCurrentEpisodePacket()
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathSetCurrentEpisode
            {
                Unknown0       = 0,
                PathEpisodeId = currentEpisodeId
            });
        }

        private void SendPathEpisodeProgressPacket()
        {
            SendPathEpisodeProgressPacket(false);
        }

        private void SendPathEpisodeProgressPacket(bool includeSuppressedMissions)
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathEpisodeProgress
            {
                EpisodeId = currentEpisodeId,
                Missions = BuildPathEpisodeProgressMissions(includeSuppressedMissions)
            });
        }

        private List<ServerPathEpisodeProgress.Mission> BuildPathEpisodeProgressMissions(bool includeSuppressedMissions)
        {
            return activeMissions.Values
                .Where(m => (includeSuppressedMissions || ShouldIncludeMissionInEpisodeProgress(m)) && m.Entry.PathEpisodeId == currentEpisodeId && CanActivateMission(m.Entry))
                .Concat(completedMissions.Values.Where(m => (includeSuppressedMissions || ShouldIncludeMissionInEpisodeProgress(m)) && m.Entry.PathEpisodeId == currentEpisodeId))
                .Select(CreateEpisodeProgressMission)
                .ToList();
        }

        private static ServerPathEpisodeProgress.Mission CreateEpisodeProgressMission(PathMissionProgress mission)
        {
            return new ServerPathEpisodeProgress.Mission
            {
                PathMissionId           = (ushort)mission.Entry.Id,
                Completed               = mission.Completed,
                ObjectiveCompletionFlags = mission.UserData,
                StateFlags              = mission.StateData
            };
        }

        private void SendPathMissionActivatePacket()
        {
            SendPathMissionActivatePacket(false, false);
        }

        private void SendPathMissionActivatePacket(bool includeSuppressedMissions)
        {
            SendPathMissionActivatePacket(includeSuppressedMissions, false);
        }

        private void SendPathMissionActivatePacket(bool includeSuppressedMissions, bool includeConquerTheYeti)
        {
            if (activeMissions.Count == 0)
            {
                LogConquerTheYetiActivatePacket(new List<ServerPathMissionActivate.Mission>());
                return;
            }

            List<ServerPathMissionActivate.Mission> missions = activeMissions.Values
                .Where(m => (includeConquerTheYeti || m.Entry.Id != ConquerTheYetiMissionId) && (includeSuppressedMissions || ShouldIncludeMissionInActivatePacket(m)) && m.Entry.PathEpisodeId == currentEpisodeId && CanActivateMission(m.Entry))
                .Select(CreateMissionActivatePacket)
                .ToList();

            LogConquerTheYetiActivatePacket(missions);
            if (missions.Count == 0)
                return;

            player.Session.EnqueueMessageEncrypted(new ServerPathMissionActivate
            {
                Missions = missions
            });
        }

        private void SendPathMissionActivatePacket(PathMissionProgress mission)
        {
            SendPathMissionActivatePacket(mission, GetMissionGiverUnitId(mission.Entry));
        }

        private void SendPathMissionActivatePacket(PathMissionProgress mission, uint giverUnitId)
        {
            List<ServerPathMissionActivate.Mission> missions = new()
            {
                CreateMissionActivatePacket(mission, giverUnitId)
            };

            LogConquerTheYetiActivatePacket(missions);
            player.Session.EnqueueMessageEncrypted(new ServerPathMissionActivate
            {
                Missions = missions
            });
        }

        private ServerPathMissionActivate.Mission CreateMissionActivatePacket(PathMissionProgress mission)
        {
            return CreateMissionActivatePacket(mission, GetMissionGiverUnitId(mission.Entry));
        }

        private ServerPathMissionActivate.Mission CreateMissionActivatePacket(PathMissionProgress mission, uint giverUnitId)
        {
            return new ServerPathMissionActivate.Mission
            {
                PathMissionId            = mission.Entry.Id,
                Completed                = mission.Completed,
                ObjectiveCompletionFlags = mission.UserData,
                StateFlags               = mission.StateData,
                State                    = (ServerPathMissionActivate.Mission.PathMissionState)GetMissionState(mission),
                GiverUnitId              = giverUnitId
            };
        }

        private uint GetMissionGiverUnitId(PathMissionEntry entry)
        {
            if (player.Map == null)
                return 0u;

            if ((PathMissionType)entry.PathMissionTypeEnum == PathMissionType.SoldierEvent)
                return 0u;

            HashSet<uint> creatureIds = GetMissionGiverCreatureIds(entry);
            if (creatureIds.Count == 0)
                return 0u;

            List<WorldLocation2Entry> locations = GetMissionWorldLocations(entry)
                .Where(l => l.WorldId == player.Map.Entry.Id)
                .ToList();

            var searchCheck = new SearchCheckRange<IWorldEntity>();
            searchCheck.Initialise(player.Position, null);

            return player.Map.Search(player.Position, null, searchCheck)
                .Where(e => creatureIds.Contains(e.CreatureId))
                .Where(e => locations.Count == 0 || locations.Any(l => IsEntityInMissionLocation(e, l)))
                .OrderBy(e => GetDistanceSquared(e.Position, player.Position))
                .Select(e => e.Guid)
                .FirstOrDefault();
        }

        private static HashSet<uint> GetMissionGiverCreatureIds(PathMissionEntry entry)
        {
            var creatureIds = new HashSet<uint>();

            if (GameTableManager.Instance.Creature2 != null)
            {
                foreach (Creature2Entry creature in GameTableManager.Instance.Creature2.Entries.Where(c => c.PathMissionIdSoldier == entry.Id))
                    AddCreatureId(creatureIds, creature.Id);
            }

            AddCreatureId(creatureIds, entry.Creature2IdUnlock);
            AddCreatureId(creatureIds, entry.Creature2IdContactOverride);
            return creatureIds;
        }

        private static float GetDistanceSquared(Vector3 left, Vector3 right)
        {
            float x = left.X - right.X;
            float y = left.Y - right.Y;
            float z = left.Z - right.Z;
            return x * x + y * y + z * z;
        }

        private void LogConquerTheYetiPathRefresh(string result)
        {
            if (!ShouldLogConquerTheYetiDiagnostics())
                return;

            log.Info($"PathMission M156 refresh player={player.CharacterId} path={player.Path} world={player.Map?.Entry?.Id ?? 0u} pos=({player.Position.X:0.00},{player.Position.Y:0.00},{player.Position.Z:0.00}) currentEpisode={currentEpisodeId} active={activeMissions.ContainsKey(ConquerTheYetiMissionId)} completed={completedMissions.ContainsKey(ConquerTheYetiMissionId)} result={result}");
        }

        private void LogConquerTheYetiCandidate(PathMissionEntry entry, string result)
        {
            if (entry.Id != ConquerTheYetiMissionId || !ShouldLogConquerTheYetiDiagnostics())
                return;

            log.Info($"PathMission M156 candidate player={player.CharacterId} result={result} path={player.Path} episode={entry.PathEpisodeId} type={entry.PathMissionTypeEnum} object={entry.ObjectId}");
        }

        private void LogConquerTheYetiActivatePacket(IReadOnlyCollection<ServerPathMissionActivate.Mission> missions)
        {
            if (!ShouldLogConquerTheYetiDiagnostics())
                return;

            ServerPathMissionActivate.Mission mission = missions.FirstOrDefault(m => m.PathMissionId == ConquerTheYetiMissionId);
            log.Info($"PathMission M156 activate-packet player={player.CharacterId} currentEpisode={currentEpisodeId} packetCount={missions.Count} containsM156={mission != null} giver={mission?.GiverUnitId ?? 0u} state={(mission != null ? (int)mission.State : 0)} active={activeMissions.ContainsKey(ConquerTheYetiMissionId)} completed={completedMissions.ContainsKey(ConquerTheYetiMissionId)}");
        }

        private bool ShouldLogConquerTheYetiDiagnostics()
        {
            return player.Map?.Entry?.Id == NorthernWildsWorldId && player.Path == EntityPath.Soldier;
        }

        private bool ShouldSuppressConquerTheYetiAutomaticPathProgress()
        {
            return player.Map?.Entry?.Id == NorthernWildsWorldId && player.Path == EntityPath.Soldier;
        }

        private bool ShouldLogConquerTheYetiLoginRefresh()
        {
            return player.Map?.Entry?.Id == NorthernWildsWorldId && player.Path == EntityPath.Soldier;
        }

        private static bool ShouldIncludeMissionInEpisodeProgress(PathMissionProgress mission)
        {
            if (mission.Entry.Id != ConquerTheYetiMissionId)
                return true;

            return false;
        }

        private static bool ShouldIncludeMissionInActivatePacket(PathMissionProgress mission)
        {
            if (mission.Entry.Id != ConquerTheYetiMissionId)
                return true;

            return !mission.Completed && mission.UserData == 0u && mission.StateData == 0u;
        }

        private static bool ShouldRefreshEpisodeProgressAfterMissionUpdate(PathMissionProgress mission)
        {
            return mission.Entry.Id != ConquerTheYetiMissionId;
        }

        private static bool ShouldRefreshEpisodeProgressThenCurrentAfterMissionCompletion(PathMissionProgress mission)
        {
            return mission.Entry.PathTypeEnum == (uint)EntityPath.Soldier;
        }

        private void ResetStaleConquerTheYetiProgress()
        {
            if (!activeMissions.TryGetValue(ConquerTheYetiMissionId, out PathMissionProgress mission) || mission.Completed)
                return;

            log.Info($"PathMission M156 reset-stale player={player.CharacterId} userData={mission.UserData} stateData={mission.StateData}");
            mission.UserData  = 0u;
            mission.StateData = 0u;
        }

        private static PathMissionState GetMissionState(PathMissionProgress mission)
        {
            if (mission.Completed)
                return PathMissionState.Complete;

            return mission.UserData != 0u || mission.StateData != 0u
                ? PathMissionState.Started
                : PathMissionState.Unlocked;
        }

        private void SendPathMissionUpdatePacket(PathMissionProgress mission)
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathMissionUpdate
            {
                PathMissionId            = (ushort)mission.Entry.Id,
                Completed                = mission.Completed,
                ObjectiveCompletionFlags = mission.UserData,
                StateFlags               = mission.StateData
            });
        }

        public string DebugConquerTheYetiStatus()
        {
            RefreshActiveMissions();
            return BuildConquerTheYetiStatus();
        }

        public string DebugRefreshConquerTheYetiMissions()
        {
            RefreshActiveMissions(true);
            return BuildConquerTheYetiStatus();
        }

        private string BuildConquerTheYetiStatus()
        {
            activeMissions.TryGetValue(ConquerTheYetiMissionId, out PathMissionProgress activeMission);
            completedMissions.TryGetValue(ConquerTheYetiMissionId, out PathMissionProgress completedMission);
            PathMissionProgress mission = activeMission ?? completedMission;

            if (mission == null)
                return $"M156 currentEpisode={currentEpisodeId} active=False completed=False episodeProgress=False missionActivate=False.";

            bool canActivate = CanActivateMission(mission.Entry);
            bool inCurrentEpisode = mission.Entry.PathEpisodeId == currentEpisodeId;
            bool inEpisodeProgress = inCurrentEpisode && ShouldIncludeMissionInEpisodeProgress(mission) && ((activeMission != null && canActivate) || completedMission != null || mission.Completed);
            bool inMissionActivate = activeMission != null && inCurrentEpisode && mission.Entry.Id != ConquerTheYetiMissionId && ShouldIncludeMissionInActivatePacket(mission) && canActivate;

            return $"M156 currentEpisode={currentEpisodeId} missionEpisode={mission.Entry.PathEpisodeId} active={activeMission != null} completed={completedMission != null || mission.Completed} userData={mission.UserData} stateData={mission.StateData} reason={(byte)GetMissionState(mission)} episodeProgress={inEpisodeProgress} missionActivate={inMissionActivate} canActivate={canActivate}.";
        }

        public bool DebugSendProgressPackets(bool sendRefresh, bool sendMissionActivate)
        {
            return DebugSendProgressPackets(sendRefresh, sendMissionActivate, false);
        }

        public bool DebugSendProgressPackets(bool sendRefresh, bool sendMissionActivate, bool includeConquerTheYeti)
        {
            if (sendMissionActivate)
                SendProgressPackets(sendRefresh, true, includeConquerTheYeti);
            else
                SendPathCurrentEpisodeAndProgressPackets(sendRefresh, includeConquerTheYeti);

            return currentEpisodeId != 0;
        }

        public void DebugResetConquerTheYeti()
        {
            ResetStaleConquerTheYetiProgress();
        }

        public bool DebugSetConquerTheYetiState(uint userData, uint stateData)
        {
            PathMissionProgress mission = GetOrCreateMission(ConquerTheYetiMissionId, false);
            if (mission == null || mission.Completed)
                return false;

            mission.UserData  = userData;
            mission.StateData = stateData;
            return true;
        }

        public bool DebugSendCurrentEpisode()
        {
            RefreshActiveMissions();
            if (currentEpisodeId == 0)
                return false;

            SendPathCurrentEpisodePacket();
            return true;
        }

        public bool DebugSendEpisodeProgress(bool includeSuppressedMissions, bool sendConquerTheYetiAvailable = false)
        {
            RefreshActiveMissions(includeSuppressedMissions);
            if (currentEpisodeId == 0)
                return false;

            SendPathEpisodeProgressPacket(includeSuppressedMissions);
            if (sendConquerTheYetiAvailable)
                DebugSendConquerTheYetiAvailable();

            return true;
        }

        public string DebugEpisodeProgressSummary(bool includeSuppressedMissions)
        {
            RefreshActiveMissions(includeSuppressedMissions);
            if (currentEpisodeId == 0)
                return "ServerPathEpisodeProgress currentEpisode=0 missions=0.";

            List<ServerPathEpisodeProgress.Mission> missions = BuildPathEpisodeProgressMissions(includeSuppressedMissions);
            string missionText = string.Join(", ", missions.Select(m => $"{m.PathMissionId}:c={m.Completed}:u={m.ObjectiveCompletionFlags}:s={m.StateFlags}"));
            if (missionText.Length == 0)
                missionText = "none";

            return $"ServerPathEpisodeProgress episode={currentEpisodeId} includeSuppressed={includeSuppressedMissions} count={missions.Count} missions=[{missionText}]";
        }

        public bool DebugSendEmptyEpisodeProgress()
        {
            RefreshActiveMissions();
            if (currentEpisodeId == 0)
                return false;

            player.Session.EnqueueMessageEncrypted(new ServerPathEpisodeProgress
            {
                EpisodeId = currentEpisodeId
            });
            return true;
        }

        public bool DebugSendSingleEpisodeProgress(uint missionId, bool completed, uint userData, uint stateData)
        {
            RefreshActiveMissions();
            if (currentEpisodeId == 0)
                return false;

            player.Session.EnqueueMessageEncrypted(new ServerPathEpisodeProgress
            {
                EpisodeId = currentEpisodeId,
                Missions = new List<ServerPathEpisodeProgress.Mission>
                {
                    new()
                    {
                        PathMissionId            = (ushort)missionId,
                        Completed                = completed,
                        ObjectiveCompletionFlags = userData,
                        StateFlags               = stateData
                    }
                }
            });
            return true;
        }

        public bool DebugSendMissionActivate(bool includeSuppressedMissions)
        {
            RefreshActiveMissions();
            if (currentEpisodeId == 0)
                return false;

            SendPathMissionActivatePacket(includeSuppressedMissions, includeSuppressedMissions);
            return true;
        }

        public string DebugMissionActivateSummary(bool includeSuppressedMissions)
        {
            RefreshActiveMissions();
            if (currentEpisodeId == 0)
                return "ServerPathMissionActivate currentEpisode=0 missions=0.";

            List<ServerPathMissionActivate.Mission> missions = activeMissions.Values
                .Where(m => (includeSuppressedMissions || m.Entry.Id != ConquerTheYetiMissionId) && (includeSuppressedMissions || ShouldIncludeMissionInActivatePacket(m)) && m.Entry.PathEpisodeId == currentEpisodeId && CanActivateMission(m.Entry))
                .Select(CreateMissionActivatePacket)
                .ToList();

            string missionText = string.Join(", ", missions.Select(m => $"{m.PathMissionId}:r={m.State}:c={m.Completed}:u={m.ObjectiveCompletionFlags}:s={m.StateFlags}:g={m.GiverUnitId}"));
            if (missionText.Length == 0)
                missionText = "none";

            return $"ServerPathMissionActivate episode={currentEpisodeId} includeSuppressed={includeSuppressedMissions} count={missions.Count} missions=[{missionText}]";
        }

        public bool DebugSendConquerTheYetiMissionActivate(uint giverUnitId = 0u)
        {
            PathMissionProgress mission = GetOrCreateMission(ConquerTheYetiMissionId, false);
            if (mission == null)
                return false;

            SendPathMissionActivatePacket(mission, giverUnitId);
            return true;
        }

        public bool DebugSendConquerTheYetiAvailable(uint giverUnitId = 0u)
        {
            RefreshActiveMissions(true);

            if (!activeMissions.TryGetValue(ConquerTheYetiMissionId, out PathMissionProgress mission))
                return false;

            if (mission.Entry.PathEpisodeId != currentEpisodeId || !CanActivateMission(mission.Entry) || !ShouldIncludeMissionInActivatePacket(mission))
                return false;

            SendPathMissionActivatePacket(mission, giverUnitId);
            return true;
        }

        public bool DebugSendConquerTheYetiMissionUpdate()
        {
            PathMissionProgress mission = GetOrCreateMission(ConquerTheYetiMissionId, false);
            if (mission == null)
                return false;

            SendPathMissionUpdatePacket(mission);
            return true;
        }

        public bool DebugSendConquerTheYetiHoldoutStatus(uint unitId, PlayerPathSoldierEventMode mode, int delayTime, int waveIndex, bool isBoss)
        {
            if (player.Map == null)
                return false;

            player.Session.EnqueueMessageEncrypted(new ServerPathSoldierHoldoutStatus
            {
                PathSoldierEventId = ConquerTheYetiEventId,
                UnitInfo           = [],
                UnitId             = unitId,
                IsBoss             = isBoss,
                Mode               = mode,
                DelayTime          = delayTime,
                WaveIndex          = waveIndex,
                MaxDefendHealth    = 0f,
                MaxAuxiliaryHealth = 0f,
                StartTimeOffset    = 0
            });
            return true;
        }

        public bool DebugSendConquerTheYetiHoldoutNextWave(uint waveIndex, bool isBoss)
        {
            if (player.Map == null)
                return false;

            player.Session.EnqueueMessageEncrypted(new ServerPathSoldierHoldOutNextWave
            {
                PathSoldierEventId = (ushort)ConquerTheYetiEventId,
                WaveIndex          = waveIndex,
                IsBoss             = isBoss
            });
            return true;
        }

        public bool DebugSendConquerTheYetiHoldoutEnd(PlayerPathSoldierResult result)
        {
            if (player.Map == null)
                return false;

            player.Session.EnqueueMessageEncrypted(new ServerPathSoldierHoldoutEnd
            {
                PathSoldierEventId = (ushort)ConquerTheYetiEventId,
                Reason             = result
            });
            return true;
        }

        private float GetCooldownTime()
        {
            return (float)DateTime.UtcNow.Subtract(player.PathActivatedTime).TotalDays * -1;
        }

        /// <summary>
        /// Used to tell the world (and the player) which Path Type this Player is.
        /// </summary>
        public void SendSetUnitPathTypePacket()
        {
            player.EnqueueToVisible(new ServerSetUnitPathType
            {
                UnitId = player.Guid,
                Path = player.Path,
            }, true);
        }

        /// <summary>
        /// Sends a response to the player's <see cref="Path"/> activate request
        /// </summary>
        /// <param name="result">Used for success or error values</param>
        public void SendServerPathActivateResult(GenericError result = GenericError.Ok)
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathChangeResult
            {
                Result = result
            });
        }

        /// <summary>
        /// Sends a response to the player's request for unlocking a <see cref="Path"/>
        /// </summary>
        /// <param name="result">Used for success or error values</param>
        public void SendServerPathUnlockResult(GenericError result = GenericError.Ok)
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathUnlockResult
            {
                Result = result,
                UnlockedPathMask = GetPathUnlockedMask()
            });
        }

        /// <summary>
        /// Sends total XP for the activate path to the player
        /// </summary>
        /// <param name="totalXp">Total Path XP to be sent</param>
        private void SendServerPathUpdateXp(uint totalXp)
        {
            player.Session.EnqueueMessageEncrypted(new ServerPathUpdateXP
            {
                TotalXP = totalXp
            });
        }

        private class PathMissionProgress
        {
            public PathMissionEntry Entry { get; }
            public bool PendingCreate => (saveMask & PathMissionSaveMask.Create) != 0;

            public uint UserData
            {
                get => userData;
                set
                {
                    if (value == userData)
                        return;

                    userData = value;
                    saveMask |= persisted ? PathMissionSaveMask.UserData : PathMissionSaveMask.Create;
                }
            }

            private uint userData;

            public uint StateData
            {
                get => stateData;
                set
                {
                    if (value == stateData)
                        return;

                    stateData = value;
                    saveMask |= persisted ? PathMissionSaveMask.StateData : PathMissionSaveMask.Create;
                }
            }

            private uint stateData;

            public bool Completed
            {
                get => completed;
                set
                {
                    if (value == completed)
                        return;

                    completed = value;
                    saveMask |= persisted ? PathMissionSaveMask.Completed : PathMissionSaveMask.Create;
                }
            }

            private bool completed;
            private bool persisted;
            private PathMissionSaveMask saveMask;

            public PathMissionProgress(PathMissionEntry entry)
            {
                Entry = entry;
            }

            public PathMissionProgress(PathMissionEntry entry, CharacterPathMissionModel model)
            {
                Entry = entry;
                userData = model.UserData;
                stateData = model.StateData;
                completed = Convert.ToBoolean(model.Completed);
                persisted = true;
            }

            public void Save(ulong characterId, CharacterContext context)
            {
                if (saveMask == PathMissionSaveMask.None)
                    return;

                if ((saveMask & PathMissionSaveMask.Create) != 0)
                {
                    context.Add(new CharacterPathMissionModel
                    {
                        Id        = characterId,
                        MissionId = (ushort)Entry.Id,
                        Completed = Convert.ToByte(Completed),
                        UserData  = UserData,
                        StateData = StateData
                    });
                }
                else
                {
                    var model = new CharacterPathMissionModel
                    {
                        Id        = characterId,
                        MissionId = (ushort)Entry.Id
                    };

                    EntityEntry<CharacterPathMissionModel> entity = context.Attach(model);
                    if ((saveMask & PathMissionSaveMask.Completed) != 0)
                    {
                        model.Completed = Convert.ToByte(Completed);
                        entity.Property(p => p.Completed).IsModified = true;
                    }

                    if ((saveMask & PathMissionSaveMask.UserData) != 0)
                    {
                        model.UserData = UserData;
                        entity.Property(p => p.UserData).IsModified = true;
                    }

                    if ((saveMask & PathMissionSaveMask.StateData) != 0)
                    {
                        model.StateData = StateData;
                        entity.Property(p => p.StateData).IsModified = true;
                    }
                }

                persisted = true;
                saveMask = PathMissionSaveMask.None;
            }
        }

        private PathEntry GetPathEntry(EntityPath path)
        {
            paths.TryGetValue(path, out PathEntry pathEntry);
            return pathEntry;
        }

        private void SetPathEntry(EntityPath path, PathEntry entry)
        {
            paths[path] = entry;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<IPathEntry> GetEnumerator()
        {
            return paths.Values.Cast<IPathEntry>().GetEnumerator();
        }
    }
}
