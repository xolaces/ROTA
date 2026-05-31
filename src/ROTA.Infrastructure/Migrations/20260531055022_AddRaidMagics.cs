using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRaidMagics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "raid_magics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    active_raid_id = table.Column<Guid>(type: "uuid", nullable: false),
                    magic_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    applied_by_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raid_magics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_raid_magics_active_raid_id",
                table: "raid_magics",
                column: "active_raid_id");

            migrationBuilder.CreateIndex(
                name: "ix_raid_magics_applied_by_player_id",
                table: "raid_magics",
                column: "applied_by_player_id");

            migrationBuilder.CreateIndex(
                name: "ix_raid_magics_raid_magic_def",
                table: "raid_magics",
                columns: new[] { "active_raid_id", "magic_definition_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "raid_magics");
        }
    }
}
