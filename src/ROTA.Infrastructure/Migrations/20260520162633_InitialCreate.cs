using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_hash = table.Column<string>(type: "text", nullable: true),
                    result_summary = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    flagged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    flag_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    experience = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    gold = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_rank = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_banned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ban_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    current_value = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_value = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    regen_per_minute = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_regen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_resources", x => x.id);
                    table.ForeignKey(
                        name: "FK_player_resources_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_stats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_attack = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    base_defense = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    base_max_health = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    current_health = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_stats", x => x.id);
                    table.ForeignKey(
                        name: "FK_player_stats_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_created_at",
                table: "audit_log",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_player_id",
                table: "audit_log",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "IX_player_resources_player_id_resource_type",
                table: "player_resources",
                columns: new[] { "player_id", "resource_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_stats_player_id",
                table: "player_stats",
                column: "player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_players_email",
                table: "players",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_players_username",
                table: "players",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_player_id",
                table: "refresh_tokens",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "player_resources");

            migrationBuilder.DropTable(
                name: "player_stats");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "players");
        }
    }
}
