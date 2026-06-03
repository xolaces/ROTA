using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaderboardEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leaderboard_entry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board = table.Column<int>(type: "integer", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    period_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false),
                    last_progress_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leaderboard_entry", x => x.id);
                    table.ForeignKey(
                        name: "FK_leaderboard_entry_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_entry_board_period_value",
                table: "leaderboard_entry",
                columns: new[] { "board", "period_key", "value" });

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_entry_player_id",
                table: "leaderboard_entry",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_entry_upsert_key",
                table: "leaderboard_entry",
                columns: new[] { "player_id", "board", "period_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leaderboard_entry");
        }
    }
}
