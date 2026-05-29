using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerRolesAndDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "players",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "roles",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Backfill: set display_name = username for all existing players
            migrationBuilder.Sql("UPDATE players SET display_name = username WHERE display_name = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "players");

            migrationBuilder.DropColumn(
                name: "roles",
                table: "players");
        }
    }
}
