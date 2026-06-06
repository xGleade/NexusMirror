using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusForever.Database.Character.Migrations
{
    [DbContext(typeof(CharacterContext))]
    [Migration("20260602120000_CharacterChallengeRuntimeState")]
    public partial class CharacterChallengeRuntimeState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "currentCount",
                table: "character_challenge",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "activeTimeRemainingMs",
                table: "character_challenge",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "cooldownTimeRemainingMs",
                table: "character_challenge",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "areaFailTimeRemainingMs",
                table: "character_challenge",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "areaFailTimeRemainingMs",
                table: "character_challenge");

            migrationBuilder.DropColumn(
                name: "cooldownTimeRemainingMs",
                table: "character_challenge");

            migrationBuilder.DropColumn(
                name: "activeTimeRemainingMs",
                table: "character_challenge");

            migrationBuilder.DropColumn(
                name: "currentCount",
                table: "character_challenge");
        }
    }
}
