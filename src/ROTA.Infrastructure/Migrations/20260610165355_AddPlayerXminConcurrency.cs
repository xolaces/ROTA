using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerXminConcurrency : Migration
    {
        // T59 — INTENTIONALLY EMPTY. The Player entity now maps the PostgreSQL `xmin` SYSTEM column
        // as its optimistic-concurrency token; every Postgres row already carries xmin, so there is
        // no schema change to apply. The scaffolded AddColumn("xmin") was removed — running it would
        // fail (cannot add a system column). This migration exists only to keep the model snapshot
        // in sync with the mapping.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
