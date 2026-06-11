using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGauntletEventIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "banner_key",
                table: "gauntlet_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "gauntlet_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "lore_blurb",
                table: "gauntlet_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "run_number",
                table: "gauntlet_events",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "highest_stage",
                table: "gauntlet_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "banner_key",
                table: "gauntlet_events");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "gauntlet_events");

            migrationBuilder.DropColumn(
                name: "lore_blurb",
                table: "gauntlet_events");

            migrationBuilder.DropColumn(
                name: "run_number",
                table: "gauntlet_events");

            migrationBuilder.DropColumn(
                name: "highest_stage",
                table: "gauntlet_entries");
        }
    }
}
