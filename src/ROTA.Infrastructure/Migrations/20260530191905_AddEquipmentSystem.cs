using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_equipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    gear_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    equipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_equipment", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_equipment_player_id",
                table: "player_equipment",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_equipment_player_slot",
                table: "player_equipment",
                columns: new[] { "player_id", "slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_equipment");
        }
    }
}
