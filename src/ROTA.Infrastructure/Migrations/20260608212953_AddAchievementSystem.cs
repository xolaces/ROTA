using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievementSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "days_played",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_login_date",
                table: "players",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "achievement_awards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achievement_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievement_awards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "achievement_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achievement_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    progress_value = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievement_progress", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "achievement_progress_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achievement_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievement_progress_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_achievement_awards_player_achievement",
                table: "achievement_awards",
                columns: new[] { "player_id", "achievement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_achievement_awards_player_id",
                table: "achievement_awards",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_achievement_progress_player_achievement",
                table: "achievement_progress",
                columns: new[] { "player_id", "achievement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_achievement_progress_events_idempotency",
                table: "achievement_progress_events",
                columns: new[] { "player_id", "achievement_id", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_achievement_progress_events_player_id",
                table: "achievement_progress_events",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievement_awards");

            migrationBuilder.DropTable(
                name: "achievement_progress");

            migrationBuilder.DropTable(
                name: "achievement_progress_events");

            migrationBuilder.DropColumn(
                name: "days_played",
                table: "players");

            migrationBuilder.DropColumn(
                name: "last_login_date",
                table: "players");
        }
    }
}
