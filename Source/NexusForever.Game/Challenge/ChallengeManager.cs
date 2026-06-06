using System.Collections.Generic;
using System.Linq;
using NexusForever.GameTable.Model;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Shared;
using NexusForever.Game.Static.Challenges;
using NexusForever.Game.Entity;
using NexusForever.Network.World.Message.Model.Challenges;
using NLog;

namespace NexusForever.Game.Challenge
{
    public class ChallengeManager : IDatabaseCharacter, IUpdate
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly Player owner;
        private readonly Dictionary<uint, ChallengeInstance> challenges = new();

        public ChallengeManager(Player owner, CharacterModel model)
        {
            this.owner = owner;

            foreach (CharacterChallengeModel challengeModel in model.Challenge)
            {
                ChallengeTemplate template = GlobalChallengeManager.Instance.GetTemplate(challengeModel.ChallengeId);
                if (template == null || !template.IsSupported)
                    continue;

                challenges[template.Id] = new ChallengeInstance(owner, template, challengeModel);
            }
        }

        public void Update(double lastTick)
        {
            foreach (ChallengeInstance challenge in challenges.Values.ToList())
                challenge.Update(lastTick);
        }

        public void Save(CharacterContext context)
        {
            foreach (ChallengeInstance challenge in challenges.Values)
                challenge.Save(context);
        }

        public void Activate(uint challengeId)
        {
            ChallengeInstance challenge = GetOrCreateChallenge(challengeId);
            challenge?.Activate();
        }

        public void Abandon(uint challengeId)
        {
            if (challenges.TryGetValue(challengeId, out ChallengeInstance challenge))
                challenge.Abandon();
        }

        public void UpdateObjective(ChallengeObjectiveType objectiveType, uint objectId, uint count)
        {
            UpdateObjective(objectiveType, new[] { objectId }, count);
        }

        public void UpdateKillCreature(INonPlayerEntity nonPlayer, uint count)
        {
            if (nonPlayer == null)
                return;

            var creatureIds = new HashSet<uint>
            {
                nonPlayer.CreatureId,
                nonPlayer.DisplayInfoId
            };

            UpdateObjective(ChallengeObjectiveType.KillCreature, creatureIds.Where(id => id != 0u), count);
        }

        private void UpdateObjective(ChallengeObjectiveType objectiveType, IEnumerable<uint> objectIds, uint count)
        {
            uint? worldZoneId = owner.Zone?.Id;
            List<uint> distinctObjectIds = objectIds.Distinct().ToList();
            Dictionary<uint, (ChallengeTemplate Template, uint ObjectId)> matchedTemplates = new();
            foreach (uint objectId in distinctObjectIds)
            {
                foreach (ChallengeTemplate template in GlobalChallengeManager.Instance.GetMatchingTemplates(worldZoneId, objectiveType, objectId))
                    matchedTemplates.TryAdd(template.Id, (template, objectId));
            }

            if (matchedTemplates.Count != 0)
            {
                log.Info($"Challenge objective matched player={owner.CharacterId} type={objectiveType} objectIds={string.Join(",", distinctObjectIds)} challenges={string.Join(",", matchedTemplates.Keys)}.");
            }

            foreach ((ChallengeTemplate template, _) in matchedTemplates.Values)
            {
                ChallengeInstance challenge = GetOrCreateChallenge(template);
                if (challenge == null)
                    continue;

                if (!challenge.IsComplete && !challenge.IsActive && !challenge.IsOnCooldown)
                    challenge.Activate();
            }

            foreach ((ChallengeTemplate template, uint objectId) in matchedTemplates.Values)
                if (challenges.TryGetValue(template.Id, out ChallengeInstance challenge))
                    challenge.UpdateObjective(objectiveType, objectId, count);
        }

        public void SendUpdate(ChallengeInstance challenge, bool activated = false)
        {
            owner.Session.EnqueueMessageEncrypted(new ServerChallengeUpdate
            {
                ActiveChallenges = new List<ServerChallengeUpdate.Challenge>
                {
                    challenge.Build(activated)
                }
            });
        }

        public void SendInitialState()
        {
            List<ServerChallengeUpdate.Challenge> initialChallenges = challenges.Values
                .Where(c => c.ShouldSendInitialState)
                .Select(c => c.Build(c.IsActive))
                .ToList();

            if (initialChallenges.Count == 0)
                return;

            owner.Session.EnqueueMessageEncrypted(new ServerChallengeUpdate
            {
                ActiveChallenges = initialChallenges
            });
        }

        private ChallengeInstance GetOrCreateChallenge(uint challengeId)
        {
            ChallengeTemplate template = GlobalChallengeManager.Instance.GetTemplate(challengeId);
            return template == null ? null : GetOrCreateChallenge(template);
        }

        private ChallengeInstance GetOrCreateChallenge(ChallengeTemplate template)
        {
            if (!template.IsSupported)
                return null;

            if (challenges.TryGetValue(template.Id, out ChallengeInstance challenge))
                return challenge;

            challenge = new ChallengeInstance(owner, template);
            challenges.Add(template.Id, challenge);
            return challenge;
        }
    }
}
