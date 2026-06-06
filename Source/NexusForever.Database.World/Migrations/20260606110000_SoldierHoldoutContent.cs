using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusForever.Database.World.Migrations
{
    [DbContext(typeof(WorldContext))]
    [Migration("20260606110000_SoldierHoldoutContent")]
    public partial class SoldierHoldoutContent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "soldier_holdout",
                columns: table => new
                {
                    missionId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    activatedCreatureId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    activatedDisplayInfoId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    activeModelSequenceId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    fallbackFactionId = table.Column<ushort>(type: "smallint(5) unsigned", nullable: false, defaultValue: (ushort)0),
                    source = table.Column<string>(type: "varchar(250)", nullable: false, defaultValue: ""),
                    confidence = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.missionId);
                });

            migrationBuilder.CreateTable(
                name: "soldier_holdout_wave",
                columns: table => new
                {
                    missionId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    waveIndex = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    isBoss = table.Column<bool>(type: "tinyint(1) unsigned", nullable: false, defaultValue: false),
                    spawnRadius = table.Column<float>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.missionId, x.waveIndex });
                    table.ForeignKey(
                        name: "FK__soldier_holdout_wave_missionId__soldier_holdout_missionId",
                        column: x => x.missionId,
                        principalTable: "soldier_holdout",
                        principalColumn: "missionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "soldier_holdout_wave_spawn",
                columns: table => new
                {
                    missionId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    waveIndex = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    spawnIndex = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    creatureId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    count = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 1u),
                    entityId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.missionId, x.waveIndex, x.spawnIndex });
                    table.ForeignKey(
                        name: "FK__soldier_holdout_wave_spawn_wave__soldier_holdout_wave",
                        columns: x => new { x.missionId, x.waveIndex },
                        principalTable: "soldier_holdout_wave",
                        principalColumns: new[] { "missionId", "waveIndex" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_soldier_holdout_wave_spawn_creatureId",
                table: "soldier_holdout_wave_spawn",
                column: "creatureId");

            migrationBuilder.InsertData(
                table: "soldier_holdout",
                columns: new[] { "missionId", "activatedCreatureId", "activatedDisplayInfoId", "activeModelSequenceId", "fallbackFactionId", "source", "confidence" },
                values: new object[] { 156u, 36910u, 22638u, 1113u, (ushort)195, "manual: video/client creature lookup; server-side holdout content not present in decoded client tbl", "medium" });

            migrationBuilder.InsertData(
                table: "soldier_holdout_wave",
                columns: new[] { "missionId", "waveIndex", "isBoss", "spawnRadius" },
                values: new object[,]
                {
                    { 156u, 0u, false, null },
                    { 156u, 1u, false, null },
                    { 156u, 2u, false, null },
                    { 156u, 3u, true, null }
                });

            migrationBuilder.InsertData(
                table: "soldier_holdout_wave_spawn",
                columns: new[] { "missionId", "waveIndex", "spawnIndex", "creatureId", "count", "entityId" },
                values: new object[,]
                {
                    { 156u, 0u, 0u, 36331u, 3u, 0u },
                    { 156u, 1u, 0u, 36331u, 3u, 0u },
                    { 156u, 2u, 0u, 36331u, 3u, 0u },
                    { 156u, 3u, 0u, 15614u, 1u, 0u }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soldier_holdout_wave_spawn");

            migrationBuilder.DropTable(
                name: "soldier_holdout_wave");

            migrationBuilder.DropTable(
                name: "soldier_holdout");
        }
    }
}
