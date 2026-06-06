using System.Collections.Generic;
using NexusForever.Database;
using NexusForever.Database.World;
using NexusForever.Database.World.Model;
using NexusForever.Shared;
using NLog;

namespace NexusForever.Game.PathMission
{
    internal sealed class PathMissionContentManager : Singleton<PathMissionContentManager>
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<uint, PathMissionContentModel> contentByMission = new();
        private bool initialised;

        public PathMissionContentManager()
        {
        }

        public void Initialise()
        {
            contentByMission.Clear();

            WorldDatabase worldDatabase = DatabaseManager.Instance.GetDatabase<WorldDatabase>();
            if (worldDatabase == null)
            {
                initialised = true;
                log.Warn("World database is not available; no path mission server content can be loaded.");
                return;
            }

            foreach (PathMissionContentModel content in worldDatabase.GetPathMissionContent())
            {
                if (content.MissionId == 0u)
                    continue;

                if (!contentByMission.TryAdd(content.MissionId, content))
                    log.Warn($"Duplicate path mission server content for mission {content.MissionId}; ignoring later row.");
            }

            initialised = true;
            log.Info($"Loaded {contentByMission.Count} path mission server content rows.");
        }

        public uint GetCompletionXp(uint missionId)
        {
            EnsureInitialised();
            return contentByMission.TryGetValue(missionId, out PathMissionContentModel content)
                ? content.CompletionXp
                : 0u;
        }

        public uint GetExplorerBeaconCreatureId(uint missionId)
        {
            EnsureInitialised();
            return contentByMission.TryGetValue(missionId, out PathMissionContentModel content)
                ? content.ExplorerBeaconCreatureId
                : 0u;
        }

        private void EnsureInitialised()
        {
            if (!initialised)
                Initialise();
        }
    }
}
