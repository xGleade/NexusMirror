using System.Collections.Immutable;
using System.Numerics;
using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Map;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Map
{
    public class EntityCache : IEntityCache
    {
        public uint GridCount => (uint)entities.Count;
        public uint EntityCount { get; }

        private readonly Dictionary<(uint GridX, uint GridZ), HashSet<EntityModel>> entities = new();
        private readonly Dictionary<uint, HashSet<EntityModel>> entitiesByCreature = new();

        public EntityCache(ImmutableList<EntityModel> models)
        {
            EntityCount = (uint)models.Count;
            foreach (EntityModel model in models)
                AddEntity(model);
        }

        /// <summary>
        /// Add <see cref="EntityModel"/> to be spawned when parent <see cref="IMapGrid"/> is activated.
        /// </summary>
        public void AddEntity(EntityModel model)
        {
            var vector = new Vector3(model.X, model.Y, model.Z);
            (uint GridX, uint GridZ) coord = MapGrid.GetGridCoord(vector);

            if (!entities.ContainsKey(coord))
                entities.Add(coord, new HashSet<EntityModel>());

            entities[coord].Add(model);

            if (model.Creature == 0u)
                return;

            if (!entitiesByCreature.ContainsKey(model.Creature))
                entitiesByCreature.Add(model.Creature, new HashSet<EntityModel>());

            entitiesByCreature[model.Creature].Add(model);
        }

        /// <summary>
        /// Return all <see cref="EntityModel"/>'s to be spawned for parent <see cref="IMapGrid"/>.
        /// </summary>
        public IEnumerable<EntityModel> GetEntities(uint gridX, uint gridZ)
        {
            return entities.TryGetValue((gridX, gridZ), out HashSet<EntityModel> cellEntities) ? cellEntities : Enumerable.Empty<EntityModel>();
        }

        /// <summary>
        /// Return if the supplied creature has at least one world database spawn in this cache.
        /// </summary>
        public bool HasCreature(uint creatureId)
        {
            return creatureId != 0u && entitiesByCreature.ContainsKey(creatureId);
        }

        /// <summary>
        /// Return if any supplied creature has at least one world database spawn in this cache.
        /// </summary>
        public bool HasAnyCreature(IEnumerable<uint> creatureIds)
        {
            return creatureIds?.Any(HasCreature) == true;
        }

        /// <summary>
        /// Return if the supplied creature has a world database spawn inside the supplied world location.
        /// </summary>
        public bool HasCreatureInLocation(uint creatureId, WorldLocation2Entry location)
        {
            if (creatureId == 0u || location == null)
                return false;

            if (!entitiesByCreature.TryGetValue(creatureId, out HashSet<EntityModel> creatureEntities))
                return false;

            return creatureEntities.Any(e => IsEntityInLocation(e, location));
        }

        /// <summary>
        /// Return if any supplied creature has a world database spawn inside any supplied world location.
        /// </summary>
        public bool HasAnyCreatureInLocation(IEnumerable<uint> creatureIds, IEnumerable<WorldLocation2Entry> locations)
        {
            if (creatureIds == null || locations == null)
                return false;

            WorldLocation2Entry[] locationArray = locations.Where(l => l != null).ToArray();
            if (locationArray.Length == 0)
                return false;

            return creatureIds.Any(c => locationArray.Any(l => HasCreatureInLocation(c, l)));
        }

        private static bool IsEntityInLocation(EntityModel entity, WorldLocation2Entry location)
        {
            float radius = location.Radius > 0f ? location.Radius : 128f;
            float x = entity.X - location.Position0;
            float z = entity.Z - location.Position2;

            if (x * x + z * z > radius * radius)
                return false;

            if (location.MaxVerticalDistance <= 0f)
                return true;

            return Math.Abs(entity.Y - location.Position1) <= location.MaxVerticalDistance;
        }
    }
}
