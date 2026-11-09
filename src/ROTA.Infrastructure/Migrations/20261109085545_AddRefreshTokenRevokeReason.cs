using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenRevokeReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revoked_at",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revoked_reason",
                table: "refresh_tokens",
                type: "integer",
                nullable: true);

            // Rows revoked before this migration never recorded why. Backfill them as Rotated (1), which
            // keeps the old strict behaviour for them: presenting one still revokes the family. Being
            // wrong in that direction costs a re-login; being wrong the other way would let a replayed
            // rotation slip through quietly. These rows age out with the refresh lifetime. revoked_at
            // stays null for them, because the real time is unknown. Must run before the constraint.
            migrationBuilder.Sql(
                "UPDATE refresh_tokens SET revoked_reason = 1 WHERE is_revoked AND revoked_reason IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_tokens_revoked_reason",
                table: "refresh_tokens",
                sql: "(is_revoked AND revoked_reason IS NOT NULL) OR (NOT is_revoked AND revoked_reason IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_tokens_revoked_reason",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "revoked_reason",
                table: "refresh_tokens");
        }
    }
}
