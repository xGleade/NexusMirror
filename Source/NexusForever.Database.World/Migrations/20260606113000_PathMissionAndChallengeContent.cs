using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusForever.Database.World.Migrations
{
    [DbContext(typeof(WorldContext))]
    [Migration("20260606113000_PathMissionAndChallengeContent")]
    public partial class PathMissionAndChallengeContent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "path_mission_content",
                columns: table => new
                {
                    missionId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    completionXp = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    explorerBeaconCreatureId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    source = table.Column<string>(type: "varchar(250)", nullable: false, defaultValue: ""),
                    confidence = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.missionId);
                });

            migrationBuilder.CreateTable(
                name: "challenge_content",
                columns: table => new
                {
                    challengeId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    activeDurationMs = table.Column<uint>(type: "int(10) unsigned", nullable: true),
                    cooldownDurationMs = table.Column<uint>(type: "int(10) unsigned", nullable: true),
                    areaFailDurationMs = table.Column<uint>(type: "int(10) unsigned", nullable: true),
                    repeatable = table.Column<bool>(type: "tinyint(1) unsigned", nullable: true),
                    source = table.Column<string>(type: "varchar(250)", nullable: false, defaultValue: ""),
                    confidence = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.challengeId);
                });

            migrationBuilder.InsertData(
                table: "path_mission_content",
                columns: new[] { "missionId", "completionXp", "explorerBeaconCreatureId", "source", "confidence" },
                values: new object[,]
                {
                    { 33u, 25u, 0u, "manual: Northern Wilds Soldier holdout completion reward supplement", "medium" },
                    { 34u, 25u, 0u, "manual: Northern Wilds Soldier holdout completion reward supplement", "medium" },
                    { 35u, 0u, 58721u, "manual: Explorer VISTA beacon creature supplement; not present in decoded client tbl", "medium" },
                    { 156u, 25u, 0u, "manual: Northern Wilds Soldier holdout completion reward supplement", "medium" }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge_content");

            migrationBuilder.DropTable(
                name: "path_mission_content");
        }
    }
}
