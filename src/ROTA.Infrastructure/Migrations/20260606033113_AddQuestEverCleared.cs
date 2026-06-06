using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestEverCleared : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_ever_cleared",
                table: "player_quest_progress",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill (T26): any node already cleared has, by definition, ever been cleared. Without
            // this, existing players' unlocked chains would re-lock after deploy (unlock now keys off
            // has_ever_cleared, not is_cleared).
            migrationBuilder.Sql(
                "UPDATE player_quest_progress SET has_ever_cleared = TRUE WHERE is_cleared = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_ever_cleared",
                table: "player_quest_progress");
        }
    }
}
