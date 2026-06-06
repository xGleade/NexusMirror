using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NexusForever.Database.Configuration.Model;

namespace NexusForever.Database
{
    public static class Extensions
    {
        public static DbContextOptionsBuilder UseConfiguration(this DbContextOptionsBuilder optionsBuilder, IConnectionString connectionString)
        {
            switch (connectionString.Provider)
            {
                case DatabaseProvider.Sqlite:
                    EnsureSqliteDirectory(connectionString.ConnectionString);
                    optionsBuilder.UseSqlite(connectionString.ConnectionString);
                    break;
                case DatabaseProvider.MySql:
                    optionsBuilder.UseMySql(connectionString.ConnectionString, ServerVersion.AutoDetect(connectionString.ConnectionString), b =>
                    {
                        b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                    });
                    break;
                default:
                    throw new NotSupportedException($"The requested database provider: {connectionString.Provider:G} is not supported.");
            }

            return optionsBuilder;
        }

        public static void UseSqliteCompatibility(this ModelBuilder modelBuilder, IConnectionString connectionString)
        {
            if (connectionString?.Provider != DatabaseProvider.Sqlite)
                return;

            foreach (IMutableProperty property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                string columnType = property.GetColumnType();
                if (!string.IsNullOrWhiteSpace(columnType))
                    property.SetColumnType(GetSqliteColumnType(columnType));

                if (string.Equals(property.GetDefaultValueSql(), "current_timestamp()", StringComparison.OrdinalIgnoreCase))
                    property.SetDefaultValueSql("CURRENT_TIMESTAMP");
            }
        }

        private static string GetSqliteColumnType(string columnType)
        {
            string type = columnType.Trim().ToLowerInvariant();
            if (type.StartsWith("varchar") || type == "datetime")
                return "TEXT";

            if (type == "float" || type == "double")
                return "REAL";

            if (type.StartsWith("tinyint")
                || type.StartsWith("smallint")
                || type.StartsWith("int")
                || type.StartsWith("bigint"))
                return "INTEGER";

            return columnType;
        }

        private static void EnsureSqliteDirectory(string connectionString)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
                return;

            string directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }
    }
}
