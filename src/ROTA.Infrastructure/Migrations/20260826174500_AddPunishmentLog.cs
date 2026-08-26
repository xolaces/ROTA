using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <summary>
    /// Creates punishment_log and makes it append-only in the SAME migration.
    ///
    /// Northstar §6, binding: "Every punishment, by any role, against any player, is logged -- actor,
    /// role, target, type, reason, duration/expiry, timestamp. Append-only, like the audit log.
    /// Non-negotiable."
    ///
    /// The triggers ship with the table rather than in a follow-up migration on purpose: split across
    /// two, there is a window in which the table exists and history can be quietly rewritten. The
    /// audit_log equivalent is a separate migration only because that table predated its enforcement.
    ///
    /// The TRUNCATE trigger is not redundant: TRUNCATE does not fire row-level DELETE triggers, so
    /// without it the whole table could be emptied in one statement.
    ///
    /// ESCAPE HATCH, deliberately awkward, mirroring audit_log:
    ///
    ///     ALTER TABLE punishment_log DISABLE TRIGGER punishment_log_append_only;
    ///     -- do the one thing
    ///     ALTER TABLE punishment_log ENABLE  TRIGGER punishment_log_append_only;
    /// </summary>
    public partial class AddPunishmentLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "punishment_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversal_of_id = table.Column<long>(type: "bigint", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_punishment_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_punishment_log_actor",
                table: "punishment_log",
                column: "actor_player_id");

            migrationBuilder.CreateIndex(
                name: "ix_punishment_log_target_created",
                table: "punishment_log",
                columns: new[] { "target_player_id", "created_at" });

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION rota_punishment_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'punishment_log is append-only: % is not permitted', TG_OP
                        USING HINT = 'A reversal is a NEW entry (Unban/Unmute), never an edit. A genuine one-off correction requires the table owner to ALTER TABLE punishment_log DISABLE TRIGGER punishment_log_append_only, act, and re-enable it.';
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS punishment_log_append_only ON punishment_log;
                CREATE TRIGGER punishment_log_append_only
                    BEFORE UPDATE OR DELETE ON punishment_log
                    FOR EACH ROW
                    EXECUTE FUNCTION rota_punishment_log_append_only();
            ");

            // TRUNCATE bypasses row-level triggers entirely, so it needs its own statement-level one.
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS punishment_log_no_truncate ON punishment_log;
                CREATE TRIGGER punishment_log_no_truncate
                    BEFORE TRUNCATE ON punishment_log
                    FOR EACH STATEMENT
                    EXECUTE FUNCTION rota_punishment_log_append_only();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS punishment_log_no_truncate ON punishment_log;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS punishment_log_append_only ON punishment_log;");

            migrationBuilder.DropTable(
                name: "punishment_log");

            // After the table, or the DROP TRIGGER statements above would have nothing to hang on.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rota_punishment_log_append_only();");
        }
    }
}
