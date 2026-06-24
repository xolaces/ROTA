using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestProgressDifficulty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_player_quest_progress_player_quest",
                table: "player_quest_progress");

            migrationBuilder.AddColumn<int>(
                name: "difficulty",
                table: "player_quest_progress",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_player_quest_progress_player_quest_difficulty",
                table: "player_quest_progress",
                columns: new[] { "player_id", "quest_id", "difficulty" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_player_quest_progress_player_quest_difficulty",
                table: "player_quest_progress");

            migrationBuilder.DropColumn(
                name: "difficulty",
                table: "player_quest_progress");

            migrationBuilder.CreateIndex(
                name: "ix_player_quest_progress_player_quest",
                table: "player_quest_progress",
                columns: new[] { "player_id", "quest_id" },
                unique: true);
        }
    }
}
