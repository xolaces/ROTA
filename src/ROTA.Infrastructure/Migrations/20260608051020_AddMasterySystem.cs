using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterySystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "active_pledge_ancient",
                table: "players",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mastery_activity_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_type = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mastery_activity_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_masteries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ancient = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_masteries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_mastery_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_type = table.Column<int>(type: "integer", nullable: false),
                    counter = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_mastery_activities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mastery_activity_events_idempotency",
                table: "mastery_activity_events",
                columns: new[] { "player_id", "activity_type", "reference_id" },
                unique: true,
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mastery_activity_events_player_id",
                table: "mastery_activity_events",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_masteries_player_ancient",
                table: "player_masteries",
                columns: new[] { "player_id", "ancient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_mastery_activities_player_type",
                table: "player_mastery_activities",
                columns: new[] { "player_id", "activity_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mastery_activity_events");

            migrationBuilder.DropTable(
                name: "player_masteries");

            migrationBuilder.DropTable(
                name: "player_mastery_activities");

            migrationBuilder.DropColumn(
                name: "active_pledge_ancient",
                table: "players");
        }
    }
}
