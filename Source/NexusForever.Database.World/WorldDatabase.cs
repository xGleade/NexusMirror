using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NexusForever.Database.Configuration.Model;
using NexusForever.Database.World.Model;
using NLog;

namespace NexusForever.Database.World
{
    [Database(DatabaseType.World)]
    public class WorldDatabase : IDatabase
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private IConnectionString config;

        public void Initialise(IConnectionString connectionString)
        {
            config = connectionString;
        }

        public void Migrate()
        {
            using var context = new WorldContext(config);

            if (config.Provider == DatabaseProvider.Sqlite)
            {
                log.Info("Creating SQLite world database schema if it does not exist...");
                context.Database.EnsureCreated();
                EnsureSqliteSchema(context);
                return;
            }

            List<string> migrations = context.Database.GetPendingMigrations().ToList();
            if (migrations.Count > 0)
            {
                log.Info($"Applying {migrations.Count} world database migration(s)...");
                foreach (string migration in migrations)
                    log.Info(migration);

                context.Database.Migrate();
            }
        }

        private IQueryable<EntityModel> EntitiesInclude(IQueryable<EntityModel> entities)
        {
            return entities
                .Include(e => e.EntityEvent)
                .Include(e => e.EntityProperty)
                .Include(e => e.EntityScript)
                .Include(e => e.EntitySpline)
                .Include(e => e.EntityStat)
                .Include(e => e.EntityVendor)
                .Include(e => e.EntityVendorCategory)
                .Include(e => e.EntityVendorItem);
        }

        public ImmutableList<EntityModel> GetEntities(ushort world)
        {
            using var context = new WorldContext(config);
            return EntitiesInclude(context.Entity.Where(e => e.World == world && e.EntityEvent == null))
                .AsSplitQuery()
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<EntityModel> GetEntitiesPublicEvent(uint publicEventId)
        {
            using var context = new WorldContext(config);
            return EntitiesInclude(context.Entity.Where(e => e.EntityEvent != null && e.EntityEvent.EventId == publicEventId))
                .AsSplitQuery()
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<EntityModel> GetEntitiesWithSpline()
        {
            using var context = new WorldContext(config);
            return context.Entity.Where(e => e.EntitySpline != null)
                .Include(e => e.EntitySpline)
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<EntityModel> GetEntitiesWithoutArea()
        {
            using var context = new WorldContext(config);
            return context.Entity.Where(e => e.Area == 0)
                .AsNoTracking()
                .ToImmutableList();
        }

        public void UpdateEntities(IEnumerable<EntityModel> models)
        {
            using var context = new WorldContext(config);
            foreach (EntityModel model in models)
            {
                EntityEntry<EntityModel> entity = context.Attach(model);
                entity.State = EntityState.Modified;
            }

            context.SaveChanges();
        }

        public ImmutableList<TutorialModel> GetTutorialTriggers()
        {
            using var context = new WorldContext(config);
            return context.Tutorial.ToImmutableList();
        }

        public ImmutableList<DisableModel> GetDisables()
        {
            using var context = new WorldContext(config);
            return context.Disable.ToImmutableList();
        }

        public ImmutableList<SoldierHoldoutModel> GetSoldierHoldouts()
        {
            using var context = new WorldContext(config);
            return context.SoldierHoldout
                .Include(e => e.Waves)
                    .ThenInclude(e => e.Spawns)
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<PathMissionContentModel> GetPathMissionContent()
        {
            using var context = new WorldContext(config);
            return context.PathMissionContent
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<ChallengeContentModel> GetChallengeContent()
        {
            using var context = new WorldContext(config);
            return context.ChallengeContent
                .AsNoTracking()
                .ToImmutableList();
        }

        private void EnsureSqliteSchema(WorldContext context)
        {
            if (!SqliteTableHasColumn(context, "entity", "mode"))
                context.Database.ExecuteSqlRaw("ALTER TABLE entity ADD COLUMN mode INTEGER NULL;");

            if (!SqliteTableHasColumn(context, "entity", "navmeshSpawnX"))
                context.Database.ExecuteSqlRaw("ALTER TABLE entity ADD COLUMN navmeshSpawnX REAL NULL;");

            if (!SqliteTableHasColumn(context, "entity", "navmeshSpawnY"))
                context.Database.ExecuteSqlRaw("ALTER TABLE entity ADD COLUMN navmeshSpawnY REAL NULL;");

            if (!SqliteTableHasColumn(context, "entity", "navmeshSpawnZ"))
                context.Database.ExecuteSqlRaw("ALTER TABLE entity ADD COLUMN navmeshSpawnZ REAL NULL;");

            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS entity_script (" +
                "id INTEGER NOT NULL DEFAULT 0, " +
                "scriptName TEXT NOT NULL DEFAULT '', " +
                "CONSTRAINT PRIMARY_KEY_entity_script PRIMARY KEY (id, scriptName), " +
                "CONSTRAINT FK__entity_script_id__entity_id FOREIGN KEY (id) REFERENCES entity(id) ON DELETE CASCADE);");

            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS entity_event (" +
                "id INTEGER NOT NULL DEFAULT 0, " +
                "eventId INTEGER NOT NULL DEFAULT 0, " +
                "phase INTEGER NOT NULL DEFAULT 0, " +
                "CONSTRAINT PRIMARY_KEY_entity_event PRIMARY KEY (id, eventId, phase), " +
                "CONSTRAINT FK__entity_event_id__entity_id FOREIGN KEY (id) REFERENCES entity(id) ON DELETE CASCADE);");

            context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_entity_event_eventId ON entity_event (eventId);");
            context.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_entity_event_id ON entity_event (id);");

            EnsureSqlitePathMissionContentSchema(context);
            EnsureSqliteChallengeContentSchema(context);
            EnsureSqliteSoldierHoldoutSchema(context);
        }

        private void EnsureSqlitePathMissionContentSchema(WorldContext context)
        {
            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS path_mission_content (" +
                "missionId INTEGER NOT NULL DEFAULT 0, " +
                "completionXp INTEGER NOT NULL DEFAULT 0, " +
                "explorerBeaconCreatureId INTEGER NOT NULL DEFAULT 0, " +
                "source TEXT NOT NULL DEFAULT '', " +
                "confidence TEXT NOT NULL DEFAULT '', " +
                "CONSTRAINT PRIMARY_KEY_path_mission_content PRIMARY KEY (missionId));");

            SeedSqlitePathMissionContent(context);
        }

        private void SeedSqlitePathMissionContent(WorldContext context)
        {
            context.Database.ExecuteSqlRaw(
                "INSERT OR IGNORE INTO path_mission_content " +
                "(missionId, completionXp, explorerBeaconCreatureId, source, confidence) VALUES " +
                "(33, 25, 0, 'manual: Northern Wilds Soldier holdout completion reward supplement', 'medium'), " +
                "(34, 25, 0, 'manual: Northern Wilds Soldier holdout completion reward supplement', 'medium'), " +
                "(35, 0, 58721, 'manual: Explorer VISTA beacon creature supplement; not present in decoded client tbl', 'medium'), " +
                "(156, 25, 0, 'manual: Northern Wilds Soldier holdout completion reward supplement', 'medium');");
        }

        private void EnsureSqliteChallengeContentSchema(WorldContext context)
        {
            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS challenge_content (" +
                "challengeId INTEGER NOT NULL DEFAULT 0, " +
                "activeDurationMs INTEGER NULL, " +
                "cooldownDurationMs INTEGER NULL, " +
                "areaFailDurationMs INTEGER NULL, " +
                "repeatable INTEGER NULL, " +
                "source TEXT NOT NULL DEFAULT '', " +
                "confidence TEXT NOT NULL DEFAULT '', " +
                "CONSTRAINT PRIMARY_KEY_challenge_content PRIMARY KEY (challengeId));");
        }

        private void EnsureSqliteSoldierHoldoutSchema(WorldContext context)
        {
            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS soldier_holdout (" +
                "missionId INTEGER NOT NULL DEFAULT 0, " +
                "activatedCreatureId INTEGER NOT NULL DEFAULT 0, " +
                "activatedDisplayInfoId INTEGER NOT NULL DEFAULT 0, " +
                "activeModelSequenceId INTEGER NOT NULL DEFAULT 0, " +
                "fallbackFactionId INTEGER NOT NULL DEFAULT 0, " +
                "source TEXT NOT NULL DEFAULT '', " +
                "confidence TEXT NOT NULL DEFAULT '', " +
                "CONSTRAINT PRIMARY_KEY_soldier_holdout PRIMARY KEY (missionId));");

            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS soldier_holdout_wave (" +
                "missionId INTEGER NOT NULL DEFAULT 0, " +
                "waveIndex INTEGER NOT NULL DEFAULT 0, " +
                "isBoss INTEGER NOT NULL DEFAULT 0, " +
                "spawnRadius REAL NULL, " +
                "CONSTRAINT PRIMARY_KEY_soldier_holdout_wave PRIMARY KEY (missionId, waveIndex), " +
                "CONSTRAINT FK__soldier_holdout_wave_missionId__soldier_holdout_missionId FOREIGN KEY (missionId) REFERENCES soldier_holdout(missionId) ON DELETE CASCADE);");

            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS soldier_holdout_wave_spawn (" +
                "missionId INTEGER NOT NULL DEFAULT 0, " +
                "waveIndex INTEGER NOT NULL DEFAULT 0, " +
                "spawnIndex INTEGER NOT NULL DEFAULT 0, " +
                "creatureId INTEGER NOT NULL DEFAULT 0, " +
                "count INTEGER NOT NULL DEFAULT 1, " +
                "entityId INTEGER NOT NULL DEFAULT 0, " +
                "CONSTRAINT PRIMARY_KEY_soldier_holdout_wave_spawn PRIMARY KEY (missionId, waveIndex, spawnIndex), " +
                "CONSTRAINT FK__soldier_holdout_wave_spawn_wave__soldier_holdout_wave FOREIGN KEY (missionId, waveIndex) REFERENCES soldier_holdout_wave(missionId, waveIndex) ON DELETE CASCADE);");

            context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_soldier_holdout_wave_spawn_creatureId ON soldier_holdout_wave_spawn (creatureId);");

            SeedSqliteSoldierHoldoutContent(context);
        }

        private void SeedSqliteSoldierHoldoutContent(WorldContext context)
        {
            context.Database.ExecuteSqlRaw(
                "INSERT OR IGNORE INTO soldier_holdout " +
                "(missionId, activatedCreatureId, activatedDisplayInfoId, activeModelSequenceId, fallbackFactionId, source, confidence) " +
                "VALUES (156, 36910, 22638, 1113, 195, 'manual: video/client creature lookup; server-side holdout content not present in decoded client tbl', 'medium');");

            context.Database.ExecuteSqlRaw(
                "INSERT OR IGNORE INTO soldier_holdout_wave (missionId, waveIndex, isBoss, spawnRadius) VALUES " +
                "(156, 0, 0, NULL), " +
                "(156, 1, 0, NULL), " +
                "(156, 2, 0, NULL), " +
                "(156, 3, 1, NULL);");

            context.Database.ExecuteSqlRaw(
                "INSERT OR IGNORE INTO soldier_holdout_wave_spawn (missionId, waveIndex, spawnIndex, creatureId, count, entityId) VALUES " +
                "(156, 0, 0, 36331, 3, 0), " +
                "(156, 1, 0, 36331, 3, 0), " +
                "(156, 2, 0, 36331, 3, 0), " +
                "(156, 3, 0, 15614, 1, 0);");
        }

        private bool SqliteTableHasColumn(WorldContext context, string table, string column)
        {
            DbConnection connection = context.Database.GetDbConnection();
            bool closeConnection = connection.State != System.Data.ConnectionState.Open;
            if (closeConnection)
                connection.Open();

            try
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info('{table}');";

                using DbDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    if (reader.GetString(1) == column)
                        return true;

                return false;
            }
            finally
            {
                if (closeConnection)
                    connection.Close();
            }
        }

        public ImmutableList<StoreCategoryModel> GetStoreCategories()
        {
            using var context = new WorldContext(config);
            return context.StoreCategory
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<StoreOfferGroupModel> GetStoreOfferGroups()
        {
            using var context = new WorldContext(config);
            return context.StoreOfferGroup
                .Include(e => e.StoreOfferGroupCategory)
                .Include(e => e.StoreOfferItem)
                    .ThenInclude(e => e.StoreOfferItemData)
                .Include(e => e.StoreOfferItem)
                    .ThenInclude(e => e.StoreOfferItemPrice)
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<MapEntranceModel> GetMapEntrances()
        {
            using var context = new WorldContext(config);
            return context.MapEntrance
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<CreatureInfoPropertyModel> GetCreateInfoProperties()
        {
            using var context = new WorldContext(config);
            return context.CreatureInfoProperty
                .AsNoTracking()
                .ToImmutableList();
        }

        public ImmutableList<CreatureInfoStatModel> GetCreateInfoStats()
        {
            using var context = new WorldContext(config);
            return context.CreatureInfoStat
                .AsNoTracking()
                .ToImmutableList();
        }
    }
}
