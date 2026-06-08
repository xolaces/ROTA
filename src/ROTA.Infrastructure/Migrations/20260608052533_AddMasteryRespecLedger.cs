using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasteryRespecLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mastery_respec_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    from_ancient = table.Column<int>(type: "integer", nullable: true),
                    to_ancient = table.Column<int>(type: "integer", nullable: false),
                    gem_cost = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mastery_respec_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mastery_respec_transactions_idempotency",
                table: "mastery_respec_transactions",
                columns: new[] { "player_id", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mastery_respec_transactions_player_id",
                table: "mastery_respec_transactions",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mastery_respec_transactions");
        }
    }
}
