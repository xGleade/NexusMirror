## NexusForever AI Proof-of-Concept Branch

[![Discord](https://img.shields.io/discord/499473932131500034.svg?style=flat&logo=discord)](https://discord.gg/8wT3GEQ)

### Status

This is not an official NexusForever branch, release channel, or upstream-supported fork.

This branch is a proof-of-concept development branch based on rapid iteration with AI agents and AI-assisted tooling. It is intended to explore how quickly WildStar emulator features, packet handlers, content fixes, and debugging workflows can be implemented and validated. Expect experimental code, uneven feature depth, and systems that are useful for local testing before they are polished enough for a production-like server.

For official NexusForever information, use the upstream project links:

* [Website](https://emulator.ws)
* [Discord](https://discord.gg/8wT3GEQ)
* [World Database](https://github.com/NexusForever/NexusForever.WorldDatabase)
* [Server Setup Guide](https://www.emulator.ws/installation/server-guide)

### What This Branch Supports

This branch targets the WildStar 16042 client.

Current proof-of-concept support includes:

* STS, authentication, realm, and world-server login flow.
* Account creation through the migration/bootstrap service.
* Character list, character creation, character selection, and world entry.
* Game table loading from extracted WildStar 16042 `tbl` data.
* Base map loading from generated `.nfmap` files.
* Local MySQL/MariaDB-backed auth, character, world, chat, and group databases.
* Optional SQLite world database mode for local world-content iteration.
* RabbitMQ-backed internal server messaging.
* World content scripts and starter-zone gameplay scaffolding.
* Iterating gameplay systems such as combat, class mechanics, spells, vendors, loot, quests, challenges, path missions, movement, chat, groups, guilds, housing, and matching.
* A world-server web console on port `5000` when the world server is running.

This does not mean all retail WildStar content is complete. Many systems are partial, data-dependent, or actively changing. Treat support as "available for local proof-of-concept testing" unless a feature has been specifically verified in your current build.

### Requirements

Install these before starting:

* Visual Studio 2026 or the .NET 10 SDK.
* Docker Desktop, if using the recommended Aspire setup.
* MySQL Server or MariaDB, if running services manually or using the standard database migration/import path.
* RabbitMQ, if running services manually.
* A WildStar 16042 client installation with a valid `Patch` folder.
* A local copy of the NexusForever world database SQL files.

### Recommended Setup: Aspire

The Aspire app host is the easiest local path because it starts MySQL, RabbitMQ, database migrations, and the server processes together.

1. Clone this repository.

   ```powershell
   git clone <repo-url>
   cd NexusForeverRepo\Source
   ```

2. Clone the world database.

   ```powershell
   git clone https://github.com/NexusForever/NexusForever.WorldDatabase C:\nexusforever-database
   ```

3. Restore and build the solution.

   ```powershell
   dotnet restore .\NexusForever.sln
   dotnet build .\NexusForever.sln
   ```

4. Generate the required WildStar data files.

   Replace `C:\Games\WildStar` with the folder that contains your WildStar `Patch` directory.

   ```powershell
   dotnet build .\NexusForever.WorldServer\NexusForever.WorldServer.csproj `
     -p:WsInstallDir="C:\Games\WildStar" `
     -p:ExtractGameTables=true `
     -p:GenerateMapFiles=true
   ```

   This extracts `tbl` data and generates `map` data into the world-server output folder.

5. Create the runtime config files.

   After the first build, copy each example config to the runtime file name expected by the server:

   ```powershell
   Copy-Item .\NexusForever.AuthServer\bin\Debug\net10.0\AuthServer.example.json .\NexusForever.AuthServer\bin\Debug\net10.0\AuthServer.json
   Copy-Item .\NexusForever.StsServer\bin\Debug\net10.0\StsServer.example.json .\NexusForever.StsServer\bin\Debug\net10.0\StsServer.json
   Copy-Item .\NexusForever.WorldServer\bin\Debug\net10.0\WorldServer.example.json .\NexusForever.WorldServer\bin\Debug\net10.0\WorldServer.json
   Copy-Item .\NexusForever.API.Character\bin\Debug\net10.0\CharacterAPI.example.json .\NexusForever.API.Character\bin\Debug\net10.0\CharacterAPI.json
   Copy-Item .\NexusForever.Server.GroupServer\bin\Debug\net10.0\GroupServer.example.json .\NexusForever.Server.GroupServer\bin\Debug\net10.0\GroupServer.json
   Copy-Item .\NexusForever.Server.ChatServer\bin\Debug\net10.0\ChatServer.example.json .\NexusForever.Server.ChatServer\bin\Debug\net10.0\ChatServer.json
   Copy-Item .\NexusForever.Aspire.Database.Migrations\bin\Debug\net10.0\AspireMigrations.example.json .\NexusForever.Aspire.Database.Migrations\bin\Debug\net10.0\AspireMigrations.json
   Copy-Item .\NexusForever.WorldServer\bin\Debug\net10.0\tbl .\NexusForever.Server.ChatServer\bin\Debug\net10.0\tbl -Recurse -Force
   ```

   The chat server uses game-table word-filter data, so it also needs access to the extracted `tbl` files.

6. Edit `AspireMigrations.json`.

   Set `WorldDatabase:Path` to your local world database SQL folder. The default example path is:

   ```json
   {
     "WorldDatabase": {
       "Path": "C:\\nexusforever-database"
     }
   }
   ```

   The same file also creates the initial test account. By default:

   * Username: `nexusforever`
   * Password: `nexusforever`

7. Start Docker Desktop.

8. Start the Aspire host.

   ```powershell
   dotnet run --project .\NexusForever.Aspire.AppHost
   ```

9. Confirm the expected services are running.

   The default local ports are:

   * STS server: `6600`
   * Auth server: `23115`
   * World server: `24000`
   * World web console: `5000`
   * Character API: `4000`
   * RabbitMQ: `5672`

10. Configure your WildStar 16042 client or launcher to use the local server endpoints.

    For local testing, point the client at `127.0.0.1`/`localhost` for STS, auth, and world connections, then log in with the account configured in `AspireMigrations.json`.

### Manual Setup

Use the manual path only if you do not want Aspire to manage containers and service startup.

1. Start MySQL/MariaDB.

2. Create the required databases:

   * `nexus_forever_auth`
   * `nexus_forever_character`
   * `nexus_forever_world`
   * `nexus_forever_chat`
   * `nexus_forever_group`

3. Start RabbitMQ.

4. Build the solution and generate `tbl`/`map` data using the same commands from the Aspire setup.

5. Copy the example config files to non-example runtime names:

   * `AuthServer.example.json` to `AuthServer.json`
   * `StsServer.example.json` to `StsServer.json`
   * `WorldServer.example.json` to `WorldServer.json`
   * `CharacterAPI.example.json` to `CharacterAPI.json`
   * `GroupServer.example.json` to `GroupServer.json`
   * `ChatServer.example.json` to `ChatServer.json`
   * `AspireMigrations.example.json` to `AspireMigrations.json`

   Create these in each project's runtime output folder after the first build.

6. Copy the extracted world-server `tbl` folder into the chat-server output folder, or update `ChatServer.json` so `GameTable:GameTablePath` points at the world-server `tbl` folder.

7. Update every connection string and RabbitMQ connection string in those config files.

   For manual database migrations, `AspireMigrations.json` also needs `ConnectionStrings` entries:

   ```json
   {
     "ConnectionStrings": {
       "authdb": "server=127.0.0.1;port=3306;user=nexusforever;password=nexusforever;database=nexus_forever_auth",
       "characterdb": "server=127.0.0.1;port=3306;user=nexusforever;password=nexusforever;database=nexus_forever_character",
       "worlddb": "server=127.0.0.1;port=3306;user=nexusforever;password=nexusforever;database=nexus_forever_world",
       "groupdb": "server=127.0.0.1;port=3306;user=nexusforever;password=nexusforever;database=nexus_forever_group",
       "chatdb": "server=127.0.0.1;port=3306;user=nexusforever;password=nexusforever;database=nexus_forever_chat"
     }
   }
   ```

8. Run database migrations/bootstrap.

   ```powershell
   dotnet run --project .\NexusForever.Aspire.Database.Migrations
   ```

9. Start the server processes.

   ```powershell
   dotnet run --project .\NexusForever.StsServer
   dotnet run --project .\NexusForever.AuthServer
   dotnet run --project .\NexusForever.API.Character
   dotnet run --project .\NexusForever.Server.GroupServer
   dotnet run --project .\NexusForever.Server.ChatServer
   dotnet run --project .\NexusForever.WorldServer
   ```

10. Configure the WildStar 16042 client to connect to your local endpoints.

### SQLite World Database Mode

The world server can use SQLite for local world-content iteration by setting only the world database provider to `Sqlite`.

This is intended for the world database runtime path. Keep auth, character, chat, and group databases on MySQL/MariaDB unless you have separately verified SQLite for those services.

In `WorldServer.json`, change `Database:World` to use a SQLite file:

```json
{
  "Database": {
    "World": {
      "ConnectionString": "Data Source=.\\Data\\nexus_forever_world.db",
      "Provider": "Sqlite"
    }
  }
}
```

When `WorldDatabase.Migrate()` runs with the SQLite provider, it does not apply the MySQL EF migration chain. Instead it:

* Creates the SQLite database/schema if it does not exist.
* Converts configured MySQL column types to SQLite-compatible types in the EF model.
* Patches older local SQLite world files with missing runtime columns/tables.
* Creates supplemental `entity_script`, `entity_event`, path mission content, challenge content, and Soldier holdout tables if needed.
* Inserts built-in supplemental path/Soldier holdout rows with `INSERT OR IGNORE`.

The standard `AspireMigrations` bootstrap/import path is still MySQL/MariaDB-oriented. Use that path for normal full database import from the NexusForever world database SQL files. SQLite mode is best treated as a local runtime convenience for world-server testing; deleting the SQLite file resets that local world database.

### Development Notes

This branch is intentionally fast-moving. Before relying on a specific feature, verify it against the current code, current game tables, and current database state.

For quick validation:

```powershell
cd Source
dotnet build .\NexusForever.sln
```

For world-server runtime issues, check:

* `WorldServer.json` database, broker, game-table, and map paths.
* Whether `tbl` and `map` exist in the world-server output folder.
* Whether ports `6600`, `23115`, `24000`, and `5000` are already in use.
* Whether the world database SQL files were applied by the migration service.
