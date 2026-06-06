using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Stat;
using NexusForever.Game.Static.Entity;
using NexusForever.Shared.Game;

namespace NexusForever.Game.Entity.Stat.Player
{
    public class WarriorStatUpdater : IStatUpdater<IPlayer>
    {
        private const float WarriorKineticEnergyOutOfCombatDecayMultiplier = 1f / 9f;

        private readonly UpdateTimer builderTimer = new(TimeSpan.FromSeconds(1.5f));
        private readonly UpdateTimer decayTimer = new(TimeSpan.FromSeconds(0.5f));

        private IPlayer player;

        public void Initialise(IPlayer entity)
        {
            player = entity;

            if (player.GetPropertyValue(Property.SpellMechanicEnergyRegenOrDecayMultiplier) <= 0f)
                player.SetBaseProperty(Property.SpellMechanicEnergyRegenOrDecayMultiplier, WarriorKineticEnergyOutOfCombatDecayMultiplier);
        }

        /// <summary>
        /// Invoked each world tick with the delta since the previous tick occurred.
        /// </summary>
        public void Update(double lastTick)
        {
            builderTimer.Update(lastTick);
            if (builderTimer.IsTicking && !builderTimer.HasElapsed)
                return;

            decayTimer.Update(lastTick);
            if (!decayTimer.HasElapsed)
                return;

            decayTimer.Reset();

            if (player.Resource1 <= 0f)
                return;

            float resource1DecayAmount = player.GetPropertyValue(Property.ResourceMax1) *
                player.GetPropertyValue(Property.SpellMechanicEnergyRegenOrDecayMultiplier) *
                (float)decayTimer.Duration;

            player.Resource1 -= resource1DecayAmount;
        }

        /// <summary>
        /// Invoked when a stat value changes.
        /// </summary>
        public void OnStatUpdate(IStatValue value, float previousValue)
        {
            if (value.Stat != Static.Entity.Stat.Resource1)
                return;

            if (value.Value > previousValue)
                builderTimer.Reset();
        }

        /// <summary>
        /// Invoked when the combat state changes.
        /// </summary>
        public void OnCombatStateUpdate(bool inCombat)
        {
            builderTimer.Reset(inCombat);
        }
    }
}
