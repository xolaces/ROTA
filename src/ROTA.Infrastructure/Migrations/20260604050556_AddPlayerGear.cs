using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerGear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_gear",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gear_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_gear", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_gear_player_gear",
                table: "player_gear",
                columns: new[] { "player_id", "gear_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_gear_player_id",
                table: "player_gear",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_gear");
        }
    }
}
