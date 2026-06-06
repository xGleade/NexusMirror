using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Abstract.Spell.Effect.Data;
using NexusForever.Game.Abstract.Spell.Event;
using NexusForever.Game.Prerequisite;
using NexusForever.Game.Spell.Event;
using NexusForever.Game.Static.Spell;

namespace NexusForever.Game.Spell
{
    public class Proxy : IProxy
    {
        public IUnitEntity Target { get; }
        public ISpellEffectProxyData Data { get; }
        public ISpell ParentSpell { get; }
        public bool CanCast { get; private set; } = false;

        private ISpellParameters proxyParameters;

        public Proxy(IUnitEntity target, ISpellEffectProxyData data, ISpell parentSpell, ISpellParameters parameters)
        {
            Target = target;
            Data = data;
            ParentSpell = parentSpell;

            proxyParameters = new SpellParameters
            {
                ParentSpellInfo = parameters.SpellInfo,
                RootSpellInfo = parameters.RootSpellInfo,
                PrimaryTargetId = GetProxyPrimaryTargetId(parameters, target),
                UserInitiatedSpellCast = parameters.UserInitiatedSpellCast,
                IsProxy = true
            };
        }

        private static uint GetProxyPrimaryTargetId(ISpellParameters parameters, IUnitEntity target)
        {
            if (parameters.PrimaryTargetId > 0u)
                return parameters.PrimaryTargetId;

            return target?.Guid ?? 0u;
        }

        public void Evaluate()
        {
            if (Target is not IPlayer)
                CanCast = true;

            if (Data.PrerequisiteId == 0)
                CanCast = true;

            if (CanCast)
                return;

            if (PrerequisiteManager.Instance.Meets(Target as IPlayer, Data.PrerequisiteId))
                CanCast = true;
        }

        public void Cast(IUnitEntity caster, ISpellEventManager events)
        {
            if (!CanCast)
                return;

            void CastProxySpell(uint spell4Id)
            {
                bool castStarted = caster.CastSpell(spell4Id, proxyParameters);
                if (castStarted)
                    ParentSpell.SendProxyPhaseSpellVisuals(caster, spell4Id);
            }

            if (ParentSpell.CastMethod == CastMethod.Aura && Data.Entry.TickTime > 0)
            {
                CastProxySpell(Data.PeriodicSpellId);
                return;
            }

            if (Data.Entry.TickTime > 0)
            {
                double tickTime = Data.Entry.TickTime;
                if (Data.Entry.DurationTime > 0)
                {
                    for (int i = 1; i >= Data.Entry.DurationTime / tickTime; i++)
                    {
                        events.EnqueueEvent(new SpellEvent(tickTime * i / 1000d, () =>
                        {
                            CastProxySpell(Data.PeriodicSpellId);
                        }));
                    }
                }
                else
                    events.EnqueueEvent(TickingEvent(tickTime, () =>
                    {
                        CastProxySpell(Data.PeriodicSpellId);
                    }));
            }
            else
                CastProxySpell(Data.SpellId);
        }

        private SpellEvent TickingEvent(double tickTime, Action action)
        {
            return new SpellEvent(tickTime / 1000d, () =>
            {
                action.Invoke();
                TickingEvent(tickTime, action);
            });
        }
    }
}
