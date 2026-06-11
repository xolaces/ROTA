using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// T65 — proves the conditional-UPDATE consume is single-use under concurrency and that
// expiry / invalidation are enforced in SQL, not just in service code.
public class PasswordResetTokenRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_pwreset_test")
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

    private async Task<Player> SeedPlayerAsync(string suffix)
    {
        await using var db = NewDbContext();
        var player = Player.Create($"resetter{suffix}", $"reset{suffix}@rota.test", "hash");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task TryConsumeAsync_ValidToken_SucceedsOnce_ThenRejectsReplay()
    {
        var player = await SeedPlayerAsync("a");
        await using var db = NewDbContext();
        var repo = new PasswordResetTokenRepository(db);
        await repo.CreateAsync(PasswordResetToken.Create(player.Id, Hash, TimeSpan.FromMinutes(15)));

        (await repo.TryConsumeAsync(player.Id, Hash)).Should().BeTrue();
        (await repo.TryConsumeAsync(player.Id, Hash)).Should().BeFalse("the code is single-use");
    }

    [Fact]
    public async Task TryConsumeAsync_ExpiredToken_Rejected()
    {
        var player = await SeedPlayerAsync("b");
        await using var db = NewDbContext();
        var repo = new PasswordResetTokenRepository(db);
        await repo.CreateAsync(PasswordResetToken.Create(player.Id, Hash, TimeSpan.FromMinutes(-1)));

        (await repo.TryConsumeAsync(player.Id, Hash)).Should().BeFalse();
    }

    [Fact]
    public async Task TryConsumeAsync_WrongPlayer_Rejected()
    {
        var owner = await SeedPlayerAsync("c1");
        var other = await SeedPlayerAsync("c2");
        await using var db = NewDbContext();
        var repo = new PasswordResetTokenRepository(db);
        await repo.CreateAsync(PasswordResetToken.Create(owner.Id, Hash, TimeSpan.FromMinutes(15)));

        (await repo.TryConsumeAsync(other.Id, Hash)).Should().BeFalse("a code is bound to its account");
        (await repo.TryConsumeAsync(owner.Id, Hash)).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateActiveAsync_KillsOutstandingCode()
    {
        var player = await SeedPlayerAsync("d");
        await using var db = NewDbContext();
        var repo = new PasswordResetTokenRepository(db);
        await repo.CreateAsync(PasswordResetToken.Create(player.Id, Hash, TimeSpan.FromMinutes(15)));

        await repo.InvalidateActiveAsync(player.Id);

        (await repo.TryConsumeAsync(player.Id, Hash)).Should().BeFalse("a new request supersedes the old code");
    }

    [Fact]
    public async Task TryConsumeAsync_ConcurrentAttempts_ExactlyOneWins()
    {
        var player = await SeedPlayerAsync("e");
        await using (var db = NewDbContext())
        {
            await new PasswordResetTokenRepository(db)
                .CreateAsync(PasswordResetToken.Create(player.Id, Hash, TimeSpan.FromMinutes(15)));
        }

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = NewDbContext();
            return await new PasswordResetTokenRepository(db).TryConsumeAsync(player.Id, Hash);
        });

        var results = await Task.WhenAll(attempts);
        results.Count(r => r).Should().Be(1, "the conditional UPDATE is the single-use race guard");
    }
}
