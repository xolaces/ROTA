using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStatInvestmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "discernment_investment",
                table: "player_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "energy_investment",
                table: "player_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "skill_points",
                table: "player_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "stamina_investment",
                table: "player_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "difficulty",
                table: "active_raids",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "player_inventory_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_used = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_inventory_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_quest_difficulty_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    difficulty = table.Column<int>(type: "integer", nullable: false),
                    completion_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    first_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_sigil_dropped = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_quest_difficulty_progress", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_inventory_items_player_id",
                table: "player_inventory_items",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_inventory_items_player_item",
                table: "player_inventory_items",
                columns: new[] { "player_id", "item_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_quest_difficulty_progress_player_id",
                table: "player_quest_difficulty_progress",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_quest_difficulty_progress_unique",
                table: "player_quest_difficulty_progress",
                columns: new[] { "player_id", "quest_id", "difficulty" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_inventory_items");

            migrationBuilder.DropTable(
                name: "player_quest_difficulty_progress");

            migrationBuilder.DropColumn(
                name: "discernment_investment",
                table: "player_stats");

            migrationBuilder.DropColumn(
                name: "energy_investment",
                table: "player_stats");

            migrationBuilder.DropColumn(
                name: "skill_points",
                table: "player_stats");

            migrationBuilder.DropColumn(
                name: "stamina_investment",
                table: "player_stats");

            migrationBuilder.DropColumn(
                name: "difficulty",
                table: "active_raids");
        }
    }
}
