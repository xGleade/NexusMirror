using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Shared.Game;
using NexusForever.Game.Static.Challenges;
using NexusForever.Game.Entity;
using NexusForever.Network.World.Message.Model.Challenges;
using NLog;

namespace NexusForever.Game.Challenge
{
    public class ChallengeInstance
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        [Flags]
        private enum ChallengeSaveMask
        {
            None            = 0x00,
            Create          = 0x01,
            CompletionCount = 0x02,
            DateCompleted   = 0x04,
            Runtime         = 0x08
        }

        public ChallengeTemplate Template { get; }
        public ulong CharacterId => owner.CharacterId;
        public uint ChallengeId => Template.Id;
        public bool IsActive => activeTimer != null;
        public bool IsOnCooldown => cooldownTimer != null;
        public bool IsComplete => !Template.Repeatable && completionCount != 0u;
        public bool ShouldSendInitialState => IsActive || (!hideInactiveState && (IsOnCooldown || IsComplete || currentCount != 0u));

        private readonly Player owner;

        private UpdateTimer activeTimer;
        private UpdateTimer cooldownTimer;
        private UpdateTimer areaFailTimer;
        private bool hideInactiveState;

        private uint currentCount;
        private uint objectiveCompletion;
        private uint currentTier;
        private uint lastRewardTier;
        private uint completionCount;
        private DateTime? dateCompleted;
        private bool persisted;
        private ChallengeSaveMask saveMask;

        public ChallengeInstance(Player owner, ChallengeTemplate template)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Template   = template ?? throw new ArgumentNullException(nameof(template));
        }

        public ChallengeInstance(Player owner, ChallengeTemplate template, CharacterChallengeModel model)
            : this(owner, template)
        {
            completionCount = model.CompletionCount;
            dateCompleted   = model.DateCompleted;
            persisted       = true;

            if (IsComplete)
            {
                currentCount = Template.GoalCount;
                objectiveCompletion = GetProgressPercent();
                RecalculateTierProgress();
                return;
            }

            currentCount = Math.Min(model.CurrentCount, Template.GoalCount);
            objectiveCompletion = GetProgressPercent();
            RecalculateTierProgress();

            activeTimer = CreateTimer(model.ActiveTimeRemainingMs, Template.ActiveDurationMs);
            cooldownTimer = CreateTimer(model.CooldownTimeRemainingMs, Template.CooldownDurationMs);
            areaFailTimer = activeTimer == null
                ? null
                : CreateTimer(model.AreaFailTimeRemainingMs, Template.AreaFailDurationMs);

            hideInactiveState = Template.Repeatable
                && !IsActive
                && completionCount != 0u
                && (IsOnCooldown || currentCount >= Template.GoalCount);
        }

        public bool Activate()
        {
            if (IsComplete)
            {
                SendResult(ChallengeResult.Completed, currentTier == 0u ? 0 : (int)currentTier - 1);
                SendUpdate();
                return false;
            }

            if (IsActive)
            {
                SendResult(ChallengeResult.TypeAlreadyActive);
                return false;
            }

            if (IsOnCooldown)
            {
                SendResult(ChallengeResult.CooldownActive);
                return false;
            }

            ResetProgress();
            hideInactiveState   = false;
            activeTimer         = new UpdateTimer(TimeSpan.FromMilliseconds(Template.ActiveDurationMs));
            areaFailTimer       = null;
            MarkRuntimeChanged(true);

            SendResult(ChallengeResult.Unlock);
            SendUpdate(true);
            SendResult(ChallengeResult.Activate);
            log.Info($"Activated challenge {ChallengeId} for character {CharacterId}.");
            return true;
        }

        public void Abandon()
        {
            if (!IsActive)
                return;

            activeTimer = null;
            areaFailTimer = null;
            hideInactiveState = false;
            ResetProgress();
            MarkRuntimeChanged();

            SendResult(ChallengeResult.AbandonRemove);
            SendUpdate();
        }

        public void Update(double lastTick)
        {
            if (activeTimer != null)
            {
                activeTimer.Update(lastTick);
                if (activeTimer.HasElapsed)
                {
                    Fail(ChallengeResult.TimerExpired);
                    return;
                }
            }

            if (areaFailTimer != null)
            {
                areaFailTimer.Update(lastTick);
                if (areaFailTimer.HasElapsed)
                {
                    Fail(ChallengeResult.LeftArea);
                    return;
                }
            }

            if (cooldownTimer != null)
            {
                cooldownTimer.Update(lastTick);
                if (cooldownTimer.HasElapsed)
                {
                    cooldownTimer = null;
                    if (hideInactiveState)
                    {
                        ResetProgress();
                        hideInactiveState = false;
                        MarkRuntimeChanged();
                        return;
                    }

                    MarkRuntimeChanged();
                    SendUpdate();
                }
            }
        }

        public bool UpdateObjective(ChallengeObjectiveType objectiveType, uint objectId, uint count)
        {
            if (!IsActive || !Template.Matches(owner.Zone?.Id, objectiveType, objectId))
                return false;

            uint oldCount = currentCount;
            currentCount = Math.Min(Template.GoalCount, currentCount + count);
            objectiveCompletion = GetProgressPercent();

            if (oldCount == currentCount)
                return false;

            CheckTierProgress();
            MarkRuntimeChanged(true);

            if (currentCount >= Template.GoalCount)
                Complete();
            else
                SendUpdate();

            return true;
        }

        public void Save(CharacterContext context)
        {
            bool saveRuntime = (saveMask & ChallengeSaveMask.Runtime) != 0 || HasTimedRuntimeState();
            if (saveMask == ChallengeSaveMask.None && !saveRuntime)
                return;

            if (!persisted || (saveMask & ChallengeSaveMask.Create) != 0)
            {
                context.Add(new CharacterChallengeModel
                {
                    Id                      = CharacterId,
                    ChallengeId             = (ushort)ChallengeId,
                    CompletionCount         = completionCount,
                    CurrentCount            = currentCount,
                    ActiveTimeRemainingMs   = GetRemainingMs(activeTimer, Template.ActiveDurationMs),
                    CooldownTimeRemainingMs = GetRemainingMs(cooldownTimer, Template.CooldownDurationMs),
                    AreaFailTimeRemainingMs = GetRemainingMs(areaFailTimer, Template.AreaFailDurationMs),
                    DateCompleted           = dateCompleted
                });
            }
            else
            {
                var model = new CharacterChallengeModel
                {
                    Id          = CharacterId,
                    ChallengeId = (ushort)ChallengeId
                };

                EntityEntry<CharacterChallengeModel> entity = context.Attach(model);
                if (saveRuntime)
                {
                    model.CurrentCount = currentCount;
                    entity.Property(p => p.CurrentCount).IsModified = true;

                    model.ActiveTimeRemainingMs = GetRemainingMs(activeTimer, Template.ActiveDurationMs);
                    entity.Property(p => p.ActiveTimeRemainingMs).IsModified = true;

                    model.CooldownTimeRemainingMs = GetRemainingMs(cooldownTimer, Template.CooldownDurationMs);
                    entity.Property(p => p.CooldownTimeRemainingMs).IsModified = true;

                    model.AreaFailTimeRemainingMs = GetRemainingMs(areaFailTimer, Template.AreaFailDurationMs);
                    entity.Property(p => p.AreaFailTimeRemainingMs).IsModified = true;
                }

                if ((saveMask & ChallengeSaveMask.CompletionCount) != 0)
                {
                    model.CompletionCount = completionCount;
                    entity.Property(p => p.CompletionCount).IsModified = true;
                }

                if ((saveMask & ChallengeSaveMask.DateCompleted) != 0)
                {
                    model.DateCompleted = dateCompleted;
                    entity.Property(p => p.DateCompleted).IsModified = true;
                }
            }

            persisted = true;
            saveMask = ChallengeSaveMask.None;
        }

        public ServerChallengeUpdate.Challenge Build(bool activated = false)
        {
            return new ServerChallengeUpdate.Challenge
            {
                ChallengeId        = ChallengeId,
                Type               = Template.Type,
                TargetGroupId      = Template.TargetGroupId,
                QualifyCount       = currentCount,
                QualityTotal       = Template.GoalCount,
                CurrentCount       = currentCount,
                GoalCount          = Template.GoalCount,
                ObjectiveCompletion = objectiveCompletion,
                CurrentTier        = currentTier,
                LastRewardTier     = lastRewardTier,
                CompletionCount    = completionCount,
                Unlocked           = true,
                Activated          = IsActive || activated,
                OnCooldown         = IsOnCooldown,
                LeftArea           = areaFailTimer != null,
                TimeActivatedDt    = GetElapsedMs(activeTimer, Template.ActiveDurationMs),
                TimeTotalActive    = Template.ActiveDurationMs,
                TimeCooldownDt     = GetElapsedMs(cooldownTimer, Template.CooldownDurationMs),
                TimeTotalCooldown  = Template.CooldownDurationMs,
                TimeAreaFailDt     = GetElapsedMs(areaFailTimer, Template.AreaFailDurationMs),
                TimeTotalAreaFail  = Template.AreaFailDurationMs,
                TierGoalCount      = Template.TierGoalCount.ToArray()
            };
        }

        private void Fail(ChallengeResult result)
        {
            activeTimer = null;
            areaFailTimer = null;
            ResetProgress();

            SendResult(result);
            StartCooldown();
            if (!persisted && !HasTimedRuntimeState())
                saveMask &= ~ChallengeSaveMask.Create;
            MarkRuntimeChanged(HasTimedRuntimeState());
            SendUpdate();
        }

        private void ResetProgress()
        {
            currentCount        = 0u;
            objectiveCompletion = 0u;
            currentTier         = 0u;
            lastRewardTier      = 0u;
        }

        private void CheckTierProgress()
        {
            for (int i = 0; i < Template.TierGoalCount.Length; i++)
            {
                uint tierGoal = Template.TierGoalCount[i];
                uint tier = (uint)i + 1u;
                if (tierGoal == 0u || currentCount < tierGoal || currentTier >= tier)
                    continue;

                currentTier = tier;
                lastRewardTier = (uint)i;
                SendResult(ChallengeResult.TierAchieved, i);
            }
        }

        private void Complete()
        {
            activeTimer = null;
            areaFailTimer = null;
            completionCount++;
            dateCompleted = DateTime.UtcNow;
            saveMask |= persisted
                ? ChallengeSaveMask.CompletionCount | ChallengeSaveMask.DateCompleted
                : ChallengeSaveMask.Create;
            MarkRuntimeChanged(true);

            SendResult(ChallengeResult.Completed, currentTier == 0u ? 0 : (int)currentTier - 1);
            if (Template.Repeatable)
            {
                StartCooldown();
                hideInactiveState = true;
                SendResult(ChallengeResult.AbandonRemove);
            }
            else
                SendUpdate();
        }

        private void StartCooldown()
        {
            cooldownTimer = Template.CooldownDurationMs == 0u
                ? null
                : new UpdateTimer(TimeSpan.FromMilliseconds(Template.CooldownDurationMs));
        }

        private void SendResult(ChallengeResult result, int data = 0)
        {
            owner.Session.EnqueueMessageEncrypted(new ServerChallengeResult
            {
                ChallengeId = (ushort)ChallengeId,
                Result      = result,
                Data        = data
            });
        }

        private void SendUpdate(bool activated = false)
        {
            owner.ChallengeManager.SendUpdate(this, activated);
        }

        private void MarkRuntimeChanged(bool ensureRow = false)
        {
            if (persisted)
            {
                saveMask |= ChallengeSaveMask.Runtime;
                return;
            }

            if (ensureRow)
                saveMask |= ChallengeSaveMask.Create;
        }

        private bool HasTimedRuntimeState()
        {
            return activeTimer != null || cooldownTimer != null || areaFailTimer != null;
        }

        private void RecalculateTierProgress()
        {
            currentTier = 0u;
            lastRewardTier = 0u;

            for (int i = 0; i < Template.TierGoalCount.Length; i++)
            {
                uint tierGoal = Template.TierGoalCount[i];
                if (tierGoal == 0u || currentCount < tierGoal)
                    continue;

                currentTier = (uint)i + 1u;
                lastRewardTier = (uint)i;
            }
        }

        private static UpdateTimer CreateTimer(uint remainingMs, uint totalMs)
        {
            if (remainingMs == 0u)
                return null;

            uint clampedRemainingMs = totalMs == 0u
                ? remainingMs
                : Math.Min(remainingMs, totalMs);

            return clampedRemainingMs == 0u
                ? null
                : new UpdateTimer(TimeSpan.FromMilliseconds(clampedRemainingMs));
        }

        private static uint GetElapsedMs(UpdateTimer timer, uint totalMs)
        {
            if (timer == null)
                return 0u;

            return totalMs - GetRemainingMs(timer, totalMs);
        }

        private static uint GetRemainingMs(UpdateTimer timer, uint totalMs)
        {
            if (timer == null)
                return 0u;

            double remainingMs = Math.Clamp(timer.Time * 1000d, 0d, totalMs);
            return (uint)remainingMs;
        }

        private uint GetProgressPercent()
        {
            if (Template.GoalCount == 0u)
                return 0u;

            if (currentCount >= Template.GoalCount)
                return 100u;

            return Math.Min(100u, (uint)Math.Floor(currentCount * 100d / Template.GoalCount));
        }
    }
}
