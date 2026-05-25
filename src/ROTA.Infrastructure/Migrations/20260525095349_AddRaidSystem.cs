using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRaidSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_raids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    raid_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    summoned_by_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_hp = table.Column<long>(type: "bigint", nullable: false),
                    max_hp = table.Column<long>(type: "bigint", nullable: false),
                    is_defeated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    participant_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_active_raids", x => x.id);
                    table.ForeignKey(
                        name: "FK_active_raids_players_summoned_by_player_id",
                        column: x => x.summoned_by_player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "raid_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    active_raid_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_damage_dealt = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    hit_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raid_participants", x => x.id);
                    table.ForeignKey(
                        name: "FK_raid_participants_active_raids_active_raid_id",
                        column: x => x.active_raid_id,
                        principalTable: "active_raids",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_raid_participants_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_raids_status",
                table: "active_raids",
                columns: new[] { "is_defeated", "is_deleted", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_active_raids_summoned_by_player_id",
                table: "active_raids",
                column: "summoned_by_player_id");

            migrationBuilder.CreateIndex(
                name: "ix_raid_participants_player_id",
                table: "raid_participants",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_raid_participants_raid_id",
                table: "raid_participants",
                column: "active_raid_id");

            migrationBuilder.CreateIndex(
                name: "ix_raid_participants_raid_player",
                table: "raid_participants",
                columns: new[] { "active_raid_id", "player_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "raid_participants");

            migrationBuilder.DropTable(
                name: "active_raids");
        }
    }
}
