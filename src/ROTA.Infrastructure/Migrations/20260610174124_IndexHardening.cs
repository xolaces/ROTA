using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndexHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_players_email",
                table: "players");

            migrationBuilder.DropIndex(
                name: "IX_players_username",
                table: "players");

            migrationBuilder.CreateIndex(
                name: "IX_players_email",
                table: "players",
                column: "email",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_players_username",
                table: "players",
                column: "username",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_guild_join_requests_pending_unique",
                table: "guild_join_requests",
                columns: new[] { "guild_id", "player_id", "kind" },
                unique: true,
                filter: "status = 0 AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_active_raids_lootable",
                table: "active_raids",
                column: "lifecycle_state",
                filter: "lifecycle_state = 1 AND is_deleted = false");

            // Audit ticket — undirected pair uniqueness: the existing partial unique on
            // (requester_id, addressee_id) is DIRECTIONAL, so A→B and B→A could be inserted
            // concurrently (both service-side reads pass). LEAST/GREATEST is an expression index EF
            // can't model, hence raw SQL (hand-added; not represented in the model snapshot).
            // Live rows are only Pending/Accepted — declines/unfriends soft-delete — so this can't
            // block a legitimate re-friend.
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ix_friendships_pair_undirected
                  ON friendships (LEAST(requester_id, addressee_id), GREATEST(requester_id, addressee_id))
                  WHERE is_deleted = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_friendships_pair_undirected;");

            migrationBuilder.DropIndex(
                name: "IX_players_email",
                table: "players");

            migrationBuilder.DropIndex(
                name: "IX_players_username",
                table: "players");

            migrationBuilder.DropIndex(
                name: "ix_guild_join_requests_pending_unique",
                table: "guild_join_requests");

            migrationBuilder.DropIndex(
                name: "ix_active_raids_lootable",
                table: "active_raids");

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
        }
    }
}
