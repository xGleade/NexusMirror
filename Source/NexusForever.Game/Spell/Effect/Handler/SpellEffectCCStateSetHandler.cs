using NexusForever.Game.Abstract.Combat.CrowdControl;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect;
using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Static.Combat.CrowdControl;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Static.Spell.Effect;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Combat;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Spell.Effect.Handler
{
    [SpellEffectHandler(SpellEffectType.CCStateSet)]
    public class SpellEffectCCStateSetHandler : ISpellEffectApplyHandler<ISpellEffectCCStateSetData>, ISpellEffectRemoveHandler<ISpellEffectCCStateSetData>
    {
        #region Dependency Injection

        private readonly ICrowdControlFactory crowdControlFactory;

        public SpellEffectCCStateSetHandler(
            ICrowdControlFactory crowdControlFactory)
        {
            this.crowdControlFactory = crowdControlFactory;
        }

        #endregion

        /// <summary>
        /// Handle <see cref="ISpell"/> effect apply on <see cref="IUnitEntity"/> target.
        /// </summary>
        public SpellEffectExecutionResult Apply(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectCCStateSetData data)
        {
            CombatLogCCState combatLog = ApplyInternal(executionContext, target, info, data);
            executionContext.AddCombatLog(combatLog);
            return combatLog.Result == CCStateApplyRulesResult.Ok ? SpellEffectExecutionResult.Ok : SpellEffectExecutionResult.PreventEffect;
        }

        private CombatLogCCState ApplyInternal(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectCCStateSetData data)
        {
            if (target.CreatureInfo != null)
            {
                int flag = 1 << (int)data.CCState.Id;
                if ((target.CreatureInfo.Entry.CcStateImmunitiesFlags & flag) != 0)
                    return BuildCombatLog(data.CCState, CCStateApplyRulesResult.TargetImmune, executionContext.Spell, target);
            }

            if (target.MaxInterruptArmour == -1)
                return BuildCombatLog(data.CCState, CCStateApplyRulesResult.TargetInfiniteInterruptArmor, executionContext.Spell, target);

            ICrowdControlApplyResult result = target.CrowdControlManager.CanApplyCCEffect(data.CCState.Id);
            if (result.Result != CCStateApplyRulesResult.Ok)
                return BuildCombatLog(data.CCState, result.Result, executionContext.Spell, target);

            if (result.NewDuration != null)
                info.SetDuration(result.NewDuration.Value);

            uint reduction = data.InterruptArmourReduction != 0u ? data.InterruptArmourReduction : 1u;
            uint remainder = reduction;
            target.CrowdControlManager.RemoveInterruptArmour(ref remainder);

            if (remainder == 0)
                return BuildCombatLog(data.CCState, CCStateApplyRulesResult.TargetInterruptArmorReduced, executionContext.Spell, target, reduction - remainder);

            if (target.HasSpell(s => s.IsCasting, out ISpell spell))
                spell.CancelCast(CastResult.CCInterrupt);

            ICrowdControlApplyHandler handler = crowdControlFactory.GetApplyHandler(data.CCState.Id);
            handler?.Apply(target, info, data);

            target.CrowdControlManager.AddCCEffect(data.CCState.Id, info);

            target.EnqueueToVisible(new ServerEntityCCStateSet
            {
                Guid           = target.Guid,
                CCState        = data.CCState.Id,
                EffectUniqueId = info.EffectId
            }, true);

            if (data.CCState.Id == CCState.Knockdown)
                SendKnockdownModelSequence(target, false);

            return BuildCombatLog(data.CCState, CCStateApplyRulesResult.Ok, executionContext.Spell, target, reduction - remainder);
        }

        /// <summary>
        /// Handle <see cref="ISpell"/> effect remove on <see cref="IUnitEntity"/> target.
        /// </summary>
        public void Remove(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectCCStateSetData data)
        {
            ICrowdControlRemoveHandler handler = crowdControlFactory.GetRemoveHandler(data.CCState.Id);
            handler?.Remove(target, info, data);

            target.CrowdControlManager.RemoveCCEffect(data.CCState.Id);

            target.EnqueueToVisible(new ServerCombatLog
            {
                CombatLog = BuildCombatLog(data.CCState, CCStateApplyRulesResult.Ok, spell, target, 0u, true)
            }, true);

            target.EnqueueToVisible(new ServerEntityCCStateRemove
            {
                Guid           = target.Guid,
                CCState        = data.CCState.Id,
                CastingId      = spell.CastingId,
                EffectUniqueId = info.EffectId,
                Removed        = true
            }, true);

            if (data.CCState.Id == CCState.Knockdown)
                SendKnockdownModelSequence(target, true);
        }

        private static void SendKnockdownModelSequence(IUnitEntity target, bool getUp)
        {
            uint modelSequenceId = GetKnockdownModelSequenceId(getUp);
            if (modelSequenceId == 0u)
                return;

            target.EnqueueToVisible(new ServerSetUnitInModelSequence
            {
                UnitId          = target.Guid,
                ModelSequenceId = modelSequenceId,
                StartTime       = 0f,
                Speed           = 1f,
                Layer           = 0u,
                Seed            = (ushort)(modelSequenceId & ushort.MaxValue)
            }, true);
        }

        private static uint GetKnockdownModelSequenceId(bool getUp)
        {
            CCStatesEntry ccStateEntry = GameTableManager.Instance.CCStates?.GetEntry((uint)CCState.Knockdown);
            if (ccStateEntry == null || ccStateEntry.VisualEffectId01 == 0u)
                return 0u;

            VisualEffectEntry visualEffectEntry = GameTableManager.Instance.VisualEffect?.GetEntry(ccStateEntry.VisualEffectId01);
            if (visualEffectEntry == null)
                return 0u;

            return getUp
                ? visualEffectEntry.ModelSequenceIdTarget02
                : visualEffectEntry.ModelSequenceIdTarget00;
        }

        private static CombatLogCCState BuildCombatLog(CCStatesEntry ccStateEntry, CCStateApplyRulesResult result, ISpell spell, IUnitEntity target, uint interruptArmorTaken = 0u, bool removed = false)
        {
            return new CombatLogCCState
            {
                State                = ccStateEntry.Id,
                Result               = result,
                DiminishingReturnsId = (ushort)ccStateEntry.CcStateDiminishingReturnsId,
                InterruptArmorTaken  = interruptArmorTaken,
                BRemoved             = removed,
                CastData             = new CombatLogCastData
                {
                    CasterId     = spell.Caster.Guid,
                    TargetId     = target.Guid,
                    SpellId      = spell.Spell4Id,
                    CombatResult = CombatResult.Hit
                }
            };
        }
    }
}
