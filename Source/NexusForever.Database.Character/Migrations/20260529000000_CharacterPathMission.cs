using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusForever.Database.Character.Migrations
{
    [DbContext(typeof(CharacterContext))]
    [Migration("20260529000000_CharacterPathMission")]
    public partial class CharacterPathMission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_path_mission",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul),
                    missionId = table.Column<ushort>(type: "smallint(5) unsigned", nullable: false, defaultValue: (ushort)0),
                    completed = table.Column<byte>(type: "tinyint(1) unsigned", nullable: false, defaultValue: (byte)0),
                    userData = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    stateData = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.id, x.missionId });
                    table.ForeignKey(
                        name: "FK__character_path_mission_id__character_id",
                        column: x => x.id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_path_mission");
        }
    }
}
