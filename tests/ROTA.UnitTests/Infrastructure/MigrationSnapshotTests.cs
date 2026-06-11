using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Infrastructure.Persistence;

namespace ROTA.UnitTests.Infrastructure;

/// <summary>
/// T67 — the CI migration gate. Compares the runtime EF model against the last migration's model
/// snapshot WITHOUT touching a database; fails when an entity/configuration changed and nobody ran
/// `dotnet ef migrations add`. (Schema applied only by raw SQL inside a migration — e.g. the
/// friendships LEAST/GREATEST pair index — is intentionally outside the snapshot and stays invisible
/// to this check.)
/// </summary>
public class MigrationSnapshotTests
{
    [Fact]
    public void Model_HasNoPendingChanges_SinceLastMigration()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only") // never connected
            .Options;

        using var ctx = new RotaDbContext(options);

        ctx.Database.HasPendingModelChanges().Should().BeFalse(
            "the EF model drifted from the migration snapshot — run " +
            "`dotnet ef migrations add <Name> --project src/ROTA.Infrastructure --startup-project src/ROTA.Api`");
    }
}
