using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <summary>
    /// T56 — backfills a Health resource pool for every existing player (current = max =
    /// player_stats.base_max_health). Health is a new ResourceType value stored as the string "Health"
    /// in player_resources.resource_type, so there is NO schema change — EF generated an empty diff and
    /// this migration only carries the data backfill. New players get their Health pool via
    /// Player.CreateWithId.
    /// </summary>
    public partial class AddHealthResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO player_resources
                    (id, player_id, resource_type, current_value, max_value, regen_per_minute, last_regen_at, updated_at)
                SELECT gen_random_uuid(), s.player_id, 'Health', s.base_max_health, s.base_max_health, 1, NOW(), NOW()
                FROM player_stats s
                WHERE NOT EXISTS (
                    SELECT 1 FROM player_resources r
                    WHERE r.player_id = s.player_id AND r.resource_type = 'Health'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM player_resources WHERE resource_type = 'Health';");
        }
    }
}
