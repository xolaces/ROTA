using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegionSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_legion_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legion_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slot_family = table.Column<string>(type: "text", nullable: false),
                    slot_index = table.Column<int>(type: "integer", nullable: false),
                    unit_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_legion_slots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_legion_slots_player_id",
                table: "player_legion_slots",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_legion_slots_player_legion_slot",
                table: "player_legion_slots",
                columns: new[] { "player_id", "legion_definition_id", "slot_family", "slot_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_legion_slots");
        }
    }
}
