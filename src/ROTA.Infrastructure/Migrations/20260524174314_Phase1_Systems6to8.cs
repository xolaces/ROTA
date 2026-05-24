using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_Systems6to8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gem_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    transaction_type = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gem_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_quest_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    completion_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_quest_progress", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gem_transactions_idempotency",
                table: "gem_transactions",
                columns: new[] { "player_id", "transaction_type", "reference_id" },
                unique: true,
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gem_transactions_player_id",
                table: "gem_transactions",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_quest_progress_player_id",
                table: "player_quest_progress",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_quest_progress_player_quest",
                table: "player_quest_progress",
                columns: new[] { "player_id", "quest_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gem_transactions");

            migrationBuilder.DropTable(
                name: "player_quest_progress");
        }
    }
}
