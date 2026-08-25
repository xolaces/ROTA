using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <summary>
    /// Makes audit_log append-only at the DATABASE, not just in application code.
    ///
    /// CLAUDE.md has always stated the rule; nothing enforced it. RotaDbContext now refuses to
    /// UPDATE or DELETE an audit row, but the API connects as the schema owner, so anything that does
    /// not come through EF -- a psql session, a future service, a migration written in haste -- could
    /// still rewrite history. A tamperable audit trail is worth less than no audit trail, because it
    /// still looks authoritative.
    ///
    /// The TRUNCATE trigger is not redundant: TRUNCATE does not fire row-level DELETE triggers, so
    /// without it the whole table could be emptied in one statement.
    ///
    /// ESCAPE HATCH, deliberately awkward: a genuine one-off correction (a data-protection erasure,
    /// say) needs the table owner to disable the trigger, act, and re-enable it. That is the point --
    /// the disable/enable is a deliberate, visible act rather than a quiet UPDATE.
    ///
    ///     ALTER TABLE audit_log DISABLE TRIGGER audit_log_append_only;
    ///     -- do the one thing
    ///     ALTER TABLE audit_log ENABLE  TRIGGER audit_log_append_only;
    /// </summary>
    public partial class EnforceAuditLogAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION rota_audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log is append-only: % is not permitted', TG_OP
                        USING HINT = 'Append a correcting entry instead of editing history. A genuine one-off correction requires the table owner to ALTER TABLE audit_log DISABLE TRIGGER audit_log_append_only, act, and re-enable it.';
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS audit_log_append_only ON audit_log;
                CREATE TRIGGER audit_log_append_only
                    BEFORE UPDATE OR DELETE ON audit_log
                    FOR EACH ROW
                    EXECUTE FUNCTION rota_audit_log_append_only();
            ");

            // TRUNCATE bypasses row-level triggers entirely, so it needs its own statement-level one.
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit_log;
                CREATE TRIGGER audit_log_no_truncate
                    BEFORE TRUNCATE ON audit_log
                    FOR EACH STATEMENT
                    EXECUTE FUNCTION rota_audit_log_append_only();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit_log;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_append_only ON audit_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rota_audit_log_append_only();");
        }
    }
}
