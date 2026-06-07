using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGauntletSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "gauntlet_event_id",
                table: "active_raids",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gauntlet_currency_transactions",
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
                    table.PrimaryKey("PK_gauntlet_currency_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gauntlet_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gauntlet_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_gauntlet_trophies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gauntlet_trophy_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_gauntlet_trophies", x => x.id);
                    table.ForeignKey(
                        name: "FK_player_gauntlet_trophies_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_magic_honors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    magic_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_magic_honors", x => x.id);
                    table.ForeignKey(
                        name: "FK_player_magic_honors_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "strike_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    transaction_type = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strike_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gauntlet_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    gauntlet_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    league = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    tie_break_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_rank = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gauntlet_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_gauntlet_entries_gauntlet_events_gauntlet_event_id",
                        column: x => x.gauntlet_event_id,
                        principalTable: "gauntlet_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gauntlet_entries_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "player_event_magics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gauntlet_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    magic_definition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_event_magics", x => x.id);
                    table.ForeignKey(
                        name: "FK_player_event_magics_gauntlet_events_gauntlet_event_id",
                        column: x => x.gauntlet_event_id,
                        principalTable: "gauntlet_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_event_magics_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_raids_gauntlet_event_id",
                table: "active_raids",
                column: "gauntlet_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_gauntlet_currency_transactions_idempotency",
                table: "gauntlet_currency_transactions",
                columns: new[] { "player_id", "currency", "transaction_type", "reference_id" },
                unique: true,
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gauntlet_currency_transactions_player_id",
                table: "gauntlet_currency_transactions",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_gauntlet_entries_event_league_score",
                table: "gauntlet_entries",
                columns: new[] { "gauntlet_event_id", "league", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_gauntlet_entries_event_player",
                table: "gauntlet_entries",
                columns: new[] { "gauntlet_event_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gauntlet_entries_gauntlet_event_id",
                table: "gauntlet_entries",
                column: "gauntlet_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_gauntlet_entries_player_id",
                table: "gauntlet_entries",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_gauntlet_events_state",
                table: "gauntlet_events",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_player_event_magics_gauntlet_event_id",
                table: "player_event_magics",
                column: "gauntlet_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_event_magics_player_event_magic",
                table: "player_event_magics",
                columns: new[] { "player_id", "gauntlet_event_id", "magic_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_event_magics_player_id",
                table: "player_event_magics",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_gauntlet_trophies_player_id",
                table: "player_gauntlet_trophies",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_gauntlet_trophies_player_trophy",
                table: "player_gauntlet_trophies",
                columns: new[] { "player_id", "gauntlet_trophy_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_magic_honors_player_id",
                table: "player_magic_honors",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_magic_honors_player_magic",
                table: "player_magic_honors",
                columns: new[] { "player_id", "magic_definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strike_transactions_idempotency",
                table: "strike_transactions",
                columns: new[] { "player_id", "transaction_type", "reference_id" },
                unique: true,
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_strike_transactions_player_id",
                table: "strike_transactions",
                column: "player_id");

            migrationBuilder.AddForeignKey(
                name: "FK_active_raids_gauntlet_events_gauntlet_event_id",
                table: "active_raids",
                column: "gauntlet_event_id",
                principalTable: "gauntlet_events",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_active_raids_gauntlet_events_gauntlet_event_id",
                table: "active_raids");

            migrationBuilder.DropTable(
                name: "gauntlet_currency_transactions");

            migrationBuilder.DropTable(
                name: "gauntlet_entries");

            migrationBuilder.DropTable(
                name: "player_event_magics");

            migrationBuilder.DropTable(
                name: "player_gauntlet_trophies");

            migrationBuilder.DropTable(
                name: "player_magic_honors");

            migrationBuilder.DropTable(
                name: "strike_transactions");

            migrationBuilder.DropTable(
                name: "gauntlet_events");

            migrationBuilder.DropIndex(
                name: "ix_active_raids_gauntlet_event_id",
                table: "active_raids");

            migrationBuilder.DropColumn(
                name: "gauntlet_event_id",
                table: "active_raids");
        }
    }
}
