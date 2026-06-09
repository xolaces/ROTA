using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRaidVisibilityModel : Migration
    {
        // Ticket 50 — replaces the boolean is_public with a visibility tier enum
        // (Private=0/Public=1/GuildOnly=2/FriendsOnly=3) and adds a lifecycle_state enum
        // (Active=0/Lootable=1/Looted=2). Existing data is preserved by backfilling BEFORE dropping
        // is_public: shared (is_public=true) raids become Public, and already-defeated raids become
        // Looted (they predate the lootable lifecycle — avoids stale "loot me" prompts).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add visibility (default Private=0), backfill from is_public, THEN drop is_public.
            migrationBuilder.AddColumn<int>(
                name: "visibility",
                table: "active_raids",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE active_raids SET visibility = 1 WHERE is_public = true;");

            migrationBuilder.DropColumn(
                name: "is_public",
                table: "active_raids");

            // 2. Add lifecycle_state (default Active=0), backfill already-defeated raids to Looted=2.
            migrationBuilder.AddColumn<int>(
                name: "lifecycle_state",
                table: "active_raids",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE active_raids SET lifecycle_state = 2 WHERE is_defeated = true AND is_deleted = false;");

            // 3. List-query index (partial — only live rows ever list).
            migrationBuilder.CreateIndex(
                name: "ix_active_raids_visibility_lifecycle",
                table: "active_raids",
                columns: new[] { "visibility", "lifecycle_state" },
                filter: "is_defeated = false AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_active_raids_visibility_lifecycle",
                table: "active_raids");

            migrationBuilder.DropColumn(
                name: "lifecycle_state",
                table: "active_raids");

            // Restore is_public, backfilling from the visibility tier (Public=1 → true), then drop visibility.
            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                table: "active_raids",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE active_raids SET is_public = true WHERE visibility = 1;");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "active_raids");
        }
    }
}
