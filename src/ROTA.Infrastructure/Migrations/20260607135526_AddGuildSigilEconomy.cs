using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildSigilEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guild_currency_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    transaction_type = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guild_currency_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guild_sigil_pool_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    transaction_type = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guild_sigil_pool_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guild_currency_transactions_idempotency",
                table: "guild_currency_transactions",
                columns: new[] { "player_id", "currency", "transaction_type", "reference_id" },
                unique: true,
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_guild_currency_transactions_player_id",
                table: "guild_currency_transactions",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_sigil_pool_transactions_guild_id",
                table: "guild_sigil_pool_transactions",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_sigil_pool_transactions_idempotency",
                table: "guild_sigil_pool_transactions",
                columns: new[] { "guild_id", "transaction_type", "reference_id" },
                unique: true,
                filter: "reference_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_currency_transactions");

            migrationBuilder.DropTable(
                name: "guild_sigil_pool_transactions");
        }
    }
}
