using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBetaKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "beta_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_redeemed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    redeemed_by_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beta_keys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_beta_keys_key",
                table: "beta_keys",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_beta_keys_redeemed_by_player_id",
                table: "beta_keys",
                column: "redeemed_by_player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beta_keys");
        }
    }
}
