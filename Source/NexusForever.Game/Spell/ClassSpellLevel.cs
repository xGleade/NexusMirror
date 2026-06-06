using NexusForever.GameTable.Model;

namespace NexusForever.Game.Spell
{
    internal static class ClassSpellLevel
    {
        private static readonly IReadOnlyDictionary<uint, uint> spellLevelOverrides = new Dictionary<uint, uint>
        {
            // Starter kit abilities exposed by the class pages as level 2/3 unlocks.
            [58524] = 2, // Warrior - Rampage
            [58591] = 3, // Warrior - Kick
            [41276] = 2, // Engineer - Electrocute
            [41438] = 3, // Engineer - Zap
            [32809] = 2, // Esper - Mind Burst
            [32812] = 3, // Esper - Crush
            [29874] = 2, // Medic - Gamma Rays
            [42352] = 3, // Medic - Paralytic Surge
            [34718] = 2, // Spellslinger - Charged Shot
            [34355] = 3, // Spellslinger - Gate
            [38779] = 2, // Stalker - Impale
            [38791] = 3  // Stalker - Stagger
        };

        public static uint GetUnlockLevel(SpellLevelEntry entry)
        {
            return spellLevelOverrides.TryGetValue(entry.Spell4Id, out uint level)
                ? level
                : entry.CharacterLevel;
        }
    }
}
