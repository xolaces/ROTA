using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    listed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sale_price = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    sale_fee_paid = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_listings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    total_price = table.Column<long>(type: "bigint", nullable: false),
                    sale_fee = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    listing_fee = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    seller_proceeds = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_board",
                table: "market_listings",
                columns: new[] { "kind", "definition_id", "unit_price" },
                filter: "status = 0 AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_buyer_id",
                table: "market_listings",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_expiry",
                table: "market_listings",
                column: "expires_at",
                filter: "status = 0 AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_market_listings_seller_id",
                table: "market_listings",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_transactions_buyer_occurred",
                table: "market_transactions",
                columns: new[] { "buyer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_market_transactions_seller_occurred",
                table: "market_transactions",
                columns: new[] { "seller_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_market_transactions_listing_id",
                table: "market_transactions",
                column: "listing_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_listings");

            migrationBuilder.DropTable(
                name: "market_transactions");
        }
    }
}
