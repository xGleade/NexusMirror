using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusForever.Database.Character.Migrations
{
    [DbContext(typeof(CharacterContext))]
    [Migration("20260602000000_CharacterChallenge")]
    public partial class CharacterChallenge : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_challenge",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul),
                    challengeId = table.Column<ushort>(type: "smallint(5) unsigned", nullable: false, defaultValue: (ushort)0),
                    completionCount = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    dateCompleted = table.Column<DateTime>(type: "datetime", nullable: true, defaultValue: null)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.id, x.challengeId });
                    table.ForeignKey(
                        name: "FK__character_challenge_id__character_id",
                        column: x => x.id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_challenge");
        }
    }
}
