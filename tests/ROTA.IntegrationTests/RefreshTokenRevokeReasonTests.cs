using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// Refresh tokens record WHY they were revoked, because that decides whether presenting one again is a
// theft signal (only Rotated is). These tests pin the database half: the rotation latch writes the
// reason in the same statement, revoke-all never relabels a rotated token, the check constraint keeps
// "revoked" and "reason" in step, and the migration backfills rows revoked before reasons existed.
public class RefreshTokenRevokeReasonTests : IAsyncLifetime
{
    private const string MigrationBeforeReasons = "20260904105215_AddAchievementIncompleteIndex";

    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_refresh_reason_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
        => new(new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private async Task MigrateFullyAsync()
    {
        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    private async Task<Player> SeedPlayerAsync(string suffix)
    {
        await using var db = NewDbContext();
        var player = Player.Create($"refresher{suffix}", $"refresh{suffix}@rota.test", "hash");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private async Task<RefreshToken> SeedTokenAsync(Guid playerId, string hash)
    {
        await using var db = NewDbContext();
        var token = new RefreshToken(playerId, hash, DateTimeOffset.UtcNow.AddDays(7), null);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<RefreshToken> ReloadAsync(Guid id)
    {
        await using var db = NewDbContext();
        return await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    [Fact]
    public async Task TryMarkRotated_WritesReasonAndTime_AndIsSingleUse()
    {
        await MigrateFullyAsync();
        var player = await SeedPlayerAsync("a");
        var token = await SeedTokenAsync(player.Id, "hash-a");

        await using (var db = NewDbContext())
        {
            var repo = new RefreshTokenRepository(db);
            (await repo.TryMarkRotatedAsync("hash-a")).Should().BeTrue();
            (await repo.TryMarkRotatedAsync("hash-a")).Should().BeFalse("rotation is single-use");
            (await repo.FindRevokeReasonAsync("hash-a")).Should().Be(RefreshTokenRevokeReason.Rotated);
        }

        var stored = await ReloadAsync(token.Id);
        stored.IsRevoked.Should().BeTrue();
        stored.RevokedReason.Should().Be(RefreshTokenRevokeReason.Rotated);
        stored.RevokedAt.Should().NotBeNull();
        stored.WasRotated.Should().BeTrue();
    }

    [Fact]
    public async Task TryMarkRotated_LosesToAnEarlierLogout_AndTheReasonSaysSo()
    {
        await MigrateFullyAsync();
        var player = await SeedPlayerAsync("b");
        await SeedTokenAsync(player.Id, "hash-b");

        await using var db = NewDbContext();
        var repo = new RefreshTokenRepository(db);
        var token = await repo.FindByTokenHashAsync("hash-b");
        await repo.RevokeAsync(token!, RefreshTokenRevokeReason.LoggedOut);

        await using var other = NewDbContext();
        var otherRepo = new RefreshTokenRepository(other);
        (await otherRepo.TryMarkRotatedAsync("hash-b")).Should().BeFalse();
        (await otherRepo.FindRevokeReasonAsync("hash-b")).Should().Be(RefreshTokenRevokeReason.LoggedOut,
            "the latch loser must be able to see it lost to a logout, not to a rotation");
    }

    [Fact]
    public async Task RevokeAllActive_RecordsReason_AndNeverRelabelsARotatedToken()
    {
        await MigrateFullyAsync();
        var player = await SeedPlayerAsync("c");
        var rotated = await SeedTokenAsync(player.Id, "hash-c1");
        var live    = await SeedTokenAsync(player.Id, "hash-c2");

        await using (var db = NewDbContext())
        {
            var repo = new RefreshTokenRepository(db);
            (await repo.TryMarkRotatedAsync("hash-c1")).Should().BeTrue();
            await repo.RevokeAllActiveAsync(player.Id, RefreshTokenRevokeReason.ReplayDetected);
        }

        (await ReloadAsync(rotated.Id)).RevokedReason.Should().Be(RefreshTokenRevokeReason.Rotated,
            "a rotated token must keep reading as Rotated, or replaying it would stop being a theft signal");
        (await ReloadAsync(live.Id)).RevokedReason.Should().Be(RefreshTokenRevokeReason.ReplayDetected);
    }

    [Fact]
    public async Task CheckConstraint_RejectsARevokedRowWithoutAReason()
    {
        await MigrateFullyAsync();
        var player = await SeedPlayerAsync("d");
        var token = await SeedTokenAsync(player.Id, "hash-d");

        await using var db = NewDbContext();
        var act = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE refresh_tokens SET is_revoked = true WHERE id = {token.Id}");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_refresh_tokens_revoked_reason");
    }

    [Fact]
    public async Task Migration_BackfillsTokensRevokedBeforeReasonsExisted_AsRotated()
    {
        // Seed the player with today's model, then roll back to just before revoke reasons, write the
        // token rows the old code would have written (raw SQL, old shape), then finish. Seeding the
        // player after the rollback breaks once a later migration adds a players column.
        await MigrateFullyAsync();
        var player = await SeedPlayerAsync("legacy");
        await using (var db = NewDbContext())
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeReasons);
        Guid revokedId = Guid.NewGuid(), liveId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO refresh_tokens (id, player_id, token_hash, expires_at, is_revoked)
                VALUES (@rid, @pid, 'legacy-revoked', now() + interval '7 days', true),
                       (@lid, @pid, 'legacy-live',    now() + interval '7 days', false);";
            cmd.Parameters.AddWithValue("pid", player.Id);
            cmd.Parameters.AddWithValue("rid", revokedId);
            cmd.Parameters.AddWithValue("lid", liveId);
            await cmd.ExecuteNonQueryAsync();
        }

        await MigrateFullyAsync();

        var revoked = await ReloadAsync(revokedId);
        revoked.RevokedReason.Should().Be(RefreshTokenRevokeReason.Rotated,
            "legacy revoked rows keep the old strict behaviour until they expire");
        revoked.RevokedAt.Should().BeNull("the real revoke time was never recorded");

        var live = await ReloadAsync(liveId);
        live.RevokedReason.Should().BeNull();
        live.IsActive.Should().BeTrue();
    }
}
