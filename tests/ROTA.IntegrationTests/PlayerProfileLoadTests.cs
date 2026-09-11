using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// The live profile showed 0 ATK / 0 DEF for every player, including one wearing Pano's Amulet,
// while the Investments panel showed the real numbers. PlayerService.GetProfileAsync computes the
// chips from player.Stats — and the loader it uses included Resources but not Stats, so on a real
// DbContext the navigation was null and the branch was skipped. Unit tests could not see it: the
// repository is mocked there and Player.Create hands back an entity with Stats already attached.
// Only a load through EF from a context that has nothing tracked shows the gap.
//
// HOW TO VERIFY THIS TEST REQUIRES THE FIX: drop `.Include(p => p.Stats)` from
// PlayerRepository.FindByIdWithResourcesAsync — the first test fails with Stats null.
public class PlayerProfileLoadTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_profile_load_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new RotaDbContext(options);
    }

    private async Task<Guid> SeedPlayerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"prof_{suffix}", $"{suffix}@test.dev", "hash");
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    [Fact]
    public async Task FindByIdWithResources_LoadsStats_SoTheProfileCanComputeAttackAndDefense()
    {
        var pid = await SeedPlayerAsync();

        // A fresh context: nothing tracked, so only what the query includes comes back.
        await using var db = NewDbContext();
        var player = await new PlayerRepository(db).FindByIdWithResourcesAsync(pid);

        player.Should().NotBeNull();
        player!.Stats.Should().NotBeNull(
            "the profile folds equipment into Stats.BaseAttack; unloaded Stats is the 0 ATK / 0 DEF bug");
        player.Stats!.BaseAttack.Should().Be(PlayerStats.Create(pid).BaseAttack);
    }

    [Fact]
    public async Task FindByIdWithResources_StillLoadsEveryResourcePool()
    {
        var pid = await SeedPlayerAsync();

        await using var db = NewDbContext();
        var player = await new PlayerRepository(db).FindByIdWithResourcesAsync(pid);

        player!.Resources.Should().HaveCount(4, "Energy, Stamina, GuildStamina and Health are seeded at creation");
    }
}
