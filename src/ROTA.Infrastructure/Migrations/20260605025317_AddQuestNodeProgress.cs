using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestNodeProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_cleared",
                table: "player_quest_progress",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "progress",
                table: "player_quest_progress",
                type: "double precision",
                nullable: false,
                defaultValue: 100.0);

            // Back-compat: nodes already completed under the old "one clear unlocks the next" model
            // are treated as Cleared, so existing players don't lose access to nodes they'd unlocked.
            // Fresh nodes start at 100 and must be depleted under the new rule.
            migrationBuilder.Sql(
                "UPDATE player_quest_progress SET is_cleared = true, progress = 0 WHERE completion_count > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_cleared",
                table: "player_quest_progress");

            migrationBuilder.DropColumn(
                name: "progress",
                table: "player_quest_progress");
        }
    }
}
