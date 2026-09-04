using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// R5d — the partial index for AchievementProgress.GetIncompleteForPlayerAsync, which runs on
    /// every quest click and every profile read.
    ///
    /// Measured in docs/eval/PARTIAL_INDEX_BENCHMARK.md on a 932,000-row table: reads 1.662 ms to
    /// 0.096 ms (about 14 to 1), writes +10.9 us per incremented row, index size 432 kB against the
    /// 56 MB composite beside it. It is small because it indexes only the incomplete rows.
    ///
    /// OPERATOR NOTE. This is a plain CREATE INDEX and it holds a write lock on achievement_progress
    /// for its duration -- on an empty or small table that is imperceptible, on a live one with real
    /// traffic it is an outage for as long as the build takes. CREATE INDEX CONCURRENTLY avoids that,
    /// but it cannot run inside a transaction, so it cannot be expressed here without
    /// migrationBuilder.Sql(..., suppressTransaction: true) -- raw SQL that the model snapshot would
    /// then not know about, which is the thing that keeps later migrations honest.
    ///
    /// So on a live database, one of two routes, NOT a plain `database update`:
    ///
    ///   (a) apply it in a maintenance window and accept the lock; or
    ///   (b) build it by hand and record the migration as done, WITHOUT running it:
    ///
    ///         CREATE INDEX CONCURRENTLY ix_ap_player_incomplete
    ///             ON achievement_progress (player_id)
    ///             WHERE NOT is_completed AND NOT is_deleted;
    ///
    ///         INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    ///         VALUES ('20260904105215_AddAchievementIncompleteIndex', '9.0.19');
    ///
    /// Route (b) needs the history row because CreateIndex emits a plain CREATE INDEX with no
    /// IF NOT EXISTS -- running the migration over an index that already exists FAILS, it does not
    /// no-op. Check the ProductVersion against the newest row already in that table before using it.
    /// </summary>
    public partial class AddAchievementIncompleteIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ap_player_incomplete",
                table: "achievement_progress",
                column: "player_id",
                filter: "NOT is_completed AND NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ap_player_incomplete",
                table: "achievement_progress");
        }
    }
}
