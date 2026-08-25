-- =============================================================================
-- ROTA -- production schema verification
--
-- WHY THIS EXISTS: docs tell you how to APPLY migrations, but nothing told you
-- whether production actually HAS them. A 2026-06-23 triage note and the old
-- PROJECT_STATE.md disagreed about whether three migrations had been applied,
-- and one of them widens int columns to bigint -- so if it is missing, the live
-- database is carrying an overflow risk nobody can see from the app.
--
-- This script answers the question from the DATABASE, not from a document.
--
-- HOW TO RUN (read-only; it writes nothing):
--
--   psql "$PROD_CONNECTION" -f scripts/verify-prod-schema.sql
--
-- or, against the compose stack on the droplet:
--
--   docker exec -i rota-postgres-prod psql -U rota_user -d rota \
--     < scripts/verify-prod-schema.sql
--
-- Read the VERDICT column of each section. Anything that is not OK means the
-- database is behind the code in this repository.
-- =============================================================================

\echo ''
\echo '=== 1. Migrations the database believes it has ==============================='
\echo ''

SELECT "MigrationId"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC
LIMIT 10;

\echo ''
\echo 'Total recorded:'
SELECT count(*) AS applied_migrations FROM "__EFMigrationsHistory";
\echo '(Compare against the file count: ls src/ROTA.Infrastructure/Migrations/*.cs'
\echo ' | grep -v Designer | grep -v Snapshot | wc -l)'

\echo ''
\echo '=== 2. The disputed migrations, by NAME ======================================'
\echo ''
\echo 'History can lie if someone ever applied SQL by hand, so section 3 checks the'
\echo 'actual column types. Treat section 3 as the ground truth.'
\echo ''

WITH expected(migration_id, what_it_does) AS (
    VALUES
        ('20260623071433_AddQuestProgressDifficulty',
         'per-difficulty quest progress + its unique index'),
        ('20260623072425_WidenGemAmountToBigint',
         'gem_transactions.amount int -> bigint'),
        ('20260623075824_WidenStatAndRewardFieldsToBigint',
         'player_stats + raid_participants int -> bigint'),
        ('20260825121651_AddBannedUntil',
         'players.banned_until, for temporary bans')
)
SELECT
    e.migration_id,
    e.what_it_does,
    CASE WHEN h."MigrationId" IS NULL THEN 'MISSING' ELSE 'OK' END AS verdict
FROM expected e
LEFT JOIN "__EFMigrationsHistory" h ON h."MigrationId" = e.migration_id
ORDER BY e.migration_id;

\echo ''
\echo '=== 3. GROUND TRUTH -- are the columns actually the right type? ==============='
\echo ''
\echo 'int64 overflow is the risk that matters: a player past ~2.1 billion in any of'
\echo 'these fields would break on write if the column is still integer.'
\echo ''

WITH expected(tbl, col, want_type, why) AS (
    VALUES
        ('gem_transactions', 'amount',                'bigint',
         'gem ledger amount'),
        ('raid_participants', 'xp_earned',            'bigint',
         'raid XP reward'),
        ('raid_participants', 'stat_points_earned',   'bigint',
         'raid stat-point reward'),
        ('raid_participants', 'gems_earned',          'bigint',
         'raid gem reward'),
        ('player_stats', 'base_attack',               'bigint',
         'core stat -- highest overflow risk'),
        ('player_stats', 'base_defense',              'bigint',
         'core stat -- highest overflow risk'),
        ('player_stats', 'skill_points',              'bigint',
         'unspent skill points'),
        ('player_stats', 'energy_investment',         'bigint',
         'energy investment'),
        ('player_stats', 'stamina_investment',        'bigint',
         'stamina investment'),
        ('player_stats', 'discernment_investment',    'bigint',
         'discernment investment')
)
SELECT
    e.tbl || '.' || e.col          AS column_name,
    e.why,
    coalesce(c.data_type, '(no such column)') AS actual_type,
    CASE
        WHEN c.data_type IS NULL        THEN 'MISSING COLUMN'
        WHEN c.data_type = e.want_type  THEN 'OK'
        ELSE 'NOT WIDENED -- OVERFLOW RISK'
    END AS verdict
FROM expected e
LEFT JOIN information_schema.columns c
       ON c.table_name = e.tbl
      AND c.column_name = e.col
      AND c.table_schema = 'public'
ORDER BY verdict DESC, column_name;

\echo ''
\echo '=== 4. Columns added by later migrations ====================================='
\echo ''

WITH expected(tbl, col, why) AS (
    VALUES
        ('player_quest_progress', 'difficulty',
         'per-difficulty quest progress (AddQuestProgressDifficulty)'),
        ('players', 'banned_until',
         'temporary bans (AddBannedUntil) -- REQUIRED before deploying that release')
)
SELECT
    e.tbl || '.' || e.col AS column_name,
    e.why,
    CASE WHEN c.column_name IS NULL THEN 'MISSING' ELSE 'OK' END AS verdict
FROM expected e
LEFT JOIN information_schema.columns c
       ON c.table_name = e.tbl
      AND c.column_name = e.col
      AND c.table_schema = 'public'
ORDER BY column_name;

\echo ''
\echo '=== 4b. Is audit_log actually append-only? ==================================='
\echo ''
\echo 'CLAUDE.md has always said audit_log is append-only. Until the'
\echo 'EnforceAuditLogAppendOnly migration, nothing enforced it, and the API connects'
\echo 'as the schema owner -- so any psql session could rewrite history.'
\echo ''
\echo 'audit_log_no_truncate is NOT redundant: TRUNCATE does not fire row-level'
\echo 'DELETE triggers, so without it the whole table goes in one statement.'
\echo ''

WITH expected(trigger_name, why) AS (
    VALUES
        ('audit_log_append_only',
         'blocks UPDATE and DELETE on audit_log'),
        ('audit_log_no_truncate',
         'blocks TRUNCATE, which row-level triggers never see')
)
SELECT
    e.trigger_name,
    e.why,
    CASE
        WHEN t.tgname IS NULL   THEN 'MISSING -- history is rewritable'
        WHEN t.tgenabled = 'D'  THEN 'DISABLED -- someone left the escape hatch open'
        ELSE 'OK'
    END AS verdict
FROM expected e
LEFT JOIN pg_trigger t
       ON t.tgname = e.trigger_name
      AND t.tgrelid = 'audit_log'::regclass
      AND NOT t.tgisinternal
ORDER BY e.trigger_name;

\echo ''
\echo '=== 5. How close is any player to the int32 ceiling? ========================='
\echo ''
\echo 'Only meaningful if section 3 says a stat column was NOT widened. A value'
\echo 'approaching 2147483647 means the overflow is imminent, not theoretical.'
\echo ''

SELECT
    max(base_attack)   AS max_base_attack,
    max(base_defense)  AS max_base_defense,
    max(skill_points)  AS max_skill_points,
    2147483647         AS int32_ceiling
FROM player_stats;

\echo ''
\echo '=== Done. Any verdict other than OK means prod is behind this repo. =========='
\echo ''
