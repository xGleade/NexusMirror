using NexusForever.Database.World.Model;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Abstract.Map
{
    public interface IEntityCache
    {
        uint GridCount { get; }
        uint EntityCount { get; }

        /// <summary>
        /// Add <see cref="EntityModel"/> to be spawned when parent <see cref="IMapGrid"/> is activated.
        /// </summary>
        void AddEntity(EntityModel model);

        /// <summary>
        /// Return all <see cref="EntityModel"/>'s to be spawned for parent <see cref="IMapGrid"/>.
        /// </summary>
        IEnumerable<EntityModel> GetEntities(uint gridX, uint gridZ);

        /// <summary>
        /// Return if the supplied creature has at least one world database spawn in this cache.
        /// </summary>
        bool HasCreature(uint creatureId);

        /// <summary>
        /// Return if any supplied creature has at least one world database spawn in this cache.
        /// </summary>
        bool HasAnyCreature(IEnumerable<uint> creatureIds);

        /// <summary>
        /// Return if the supplied creature has a world database spawn inside the supplied world location.
        /// </summary>
        bool HasCreatureInLocation(uint creatureId, WorldLocation2Entry location);

        /// <summary>
        /// Return if any supplied creature has a world database spawn inside any supplied world location.
        /// </summary>
        bool HasAnyCreatureInLocation(IEnumerable<uint> creatureIds, IEnumerable<WorldLocation2Entry> locations);
    }
}
