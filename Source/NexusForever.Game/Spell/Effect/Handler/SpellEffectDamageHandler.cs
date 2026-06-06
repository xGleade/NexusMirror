using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect;
using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Abstract.Spell.Proc;
using NexusForever.Game.Abstract.Spell.Target;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Game.Static.Spell;
using NexusForever.Game.Static.Spell.Effect;
using NexusForever.Game.Static.Spell.Proc;
using NexusForever.Shared;

namespace NexusForever.Game.Spell.Effect.Handler
{
    [SpellEffectHandler(SpellEffectType.Damage)]
    public class SpellEffectDamageHandler : ISpellEffectApplyHandler<ISpellEffectDamageData>
    {
        #region Dependency Injection

        private readonly IFactory<IDamageCalculator> damageCalculatorFactory;
        private readonly IFactory<IProcParameters> procParameterFactory;

        public SpellEffectDamageHandler(
            IFactory<IDamageCalculator> damageCalculatorFactory,
            IFactory<IProcParameters> procParameterFactory)
        {
            this.damageCalculatorFactory = damageCalculatorFactory;
            this.procParameterFactory    = procParameterFactory;
        }

        #endregion

        /// <summary>
        /// Handle <see cref="ISpell"/> effect apply on <see cref="IUnitEntity"/> target.
        /// </summary>
        public SpellEffectExecutionResult Apply(ISpellExecutionContext executionContext, IUnitEntity target, ISpellTargetEffectInfo info, ISpellEffectDamageData data)
        {
            IUnitEntity caster = executionContext.Spell.Caster;
            if (target == null || !target.CanAttack(caster))
                return SpellEffectExecutionResult.PreventEffect;

            if (caster is INonPlayerEntity && ReferenceEquals(caster, target))
                return SpellEffectExecutionResult.PreventEffect;

            IDamageCalculator damageCalculator = damageCalculatorFactory.Resolve();
            damageCalculator.CalculateDamage(executionContext, target, info);

            if (info.Damage == null)
                return SpellEffectExecutionResult.Ok;

            if (info.Damage.CombatResult == CombatResult.Critical)
            {
                IProcParameters parameters = procParameterFactory.Resolve();
                parameters.Target = target;
                executionContext.Spell.Caster.ProcManager.TriggerProc(ProcType.CriticalDamage, parameters);
            }

            target.TakeDamage(executionContext.Spell.Caster, info.Damage);

            if (executionContext.Spell.Caster is IPlayer player)
                player.Map.PublicEventManager.UpdateStat(player, PublicEventStat.Damage, info.Damage.AdjustedDamage);

            return SpellEffectExecutionResult.Ok;
        }
    }
}
