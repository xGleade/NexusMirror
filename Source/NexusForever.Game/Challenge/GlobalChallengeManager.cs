using System.Collections.Generic;
using System.Linq;
using NexusForever.Database;
using NexusForever.Database.World;
using NexusForever.Database.World.Model;
using NexusForever.Shared;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Game.Static.Challenges;
using NLog;

namespace NexusForever.Game.Challenge
{
    public sealed class GlobalChallengeManager : Singleton<GlobalChallengeManager>
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<uint, ChallengeTemplate> templates = new();
        private bool initialised;

        public GlobalChallengeManager()
        {
        }

        public void Initialise()
        {
            log.Info("Loading challenges...");

            templates.Clear();
            Dictionary<uint, ChallengeContentModel> contentByChallenge = LoadChallengeContent();
            foreach (ChallengeEntry entry in GameTableManager.Instance.Challenge?.Entries ?? Enumerable.Empty<ChallengeEntry>())
            {
                var template = new ChallengeTemplate();
                contentByChallenge.TryGetValue(entry.Id, out ChallengeContentModel content);
                template.Initialise(entry, content);
                templates[entry.Id] = template;
            }

            log.Info($"Loaded {templates.Count} challenges with {contentByChallenge.Count} server content override rows.");
            initialised = true;
        }

        public ChallengeTemplate GetTemplate(uint id)
        {
            EnsureInitialised();
            return templates.TryGetValue(id, out ChallengeTemplate template) ? template : null;
        }

        public IEnumerable<ChallengeTemplate> GetMatchingTemplates(uint? worldZoneId, ChallengeObjectiveType objectiveType, uint objectId)
        {
            EnsureInitialised();
            return templates.Values.Where(t => t.Matches(worldZoneId, objectiveType, objectId));
        }

        private void EnsureInitialised()
        {
            if (!initialised)
                Initialise();
        }

        private static Dictionary<uint, ChallengeContentModel> LoadChallengeContent()
        {
            var contentByChallenge = new Dictionary<uint, ChallengeContentModel>();
            WorldDatabase worldDatabase = DatabaseManager.Instance.GetDatabase<WorldDatabase>();
            if (worldDatabase == null)
            {
                log.Warn("World database is not available; no challenge server content can be loaded.");
                return contentByChallenge;
            }

            foreach (ChallengeContentModel content in worldDatabase.GetChallengeContent())
            {
                if (content.ChallengeId == 0u)
                    continue;

                if (!contentByChallenge.TryAdd(content.ChallengeId, content))
                    log.Warn($"Duplicate challenge server content for challenge {content.ChallengeId}; ignoring later row.");
            }

            return contentByChallenge;
        }
    }
}
