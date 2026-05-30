using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// REGRESSION (EF model-validation 20601):
// RaidSize.Personal is the CLR default (0). ActiveRaid.Size has a store default of Large.
// Without a sentinel matching the store default, EF omits the column for Personal raids and
// the database writes Large instead — silently breaking the Personal/sigil raid feature
// (access gate + summoner-only visibility never match). This test pins the exact round-trip.
// Remove `.HasSentinel(RaidSize.Large)` from ActiveRaidConfiguration and the Personal case fails.
public class RaidSizePersistenceTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_test")
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

    [Fact]
    public async Task ActiveRaid_Size_RoundTripsExactly_ForEverySize()
    {
        // Guards the ExpandRaidSizeSet migration + sentinel for all five sizes.
        foreach (var size in new[] { RaidSize.Personal, RaidSize.Small, RaidSize.Medium, RaidSize.Large, RaidSize.Titanic })
        {
            Guid raidId;
            await using (var db = NewDbContext())
            {
                // ActiveRaid.SummonedByPlayerId is an FK → seed the summoner first.
                var summoner = Player.Create($"sum_{size}", $"{size}@persist.test", "hash");
                db.Players.Add(summoner);

                var raid = ActiveRaid.Create(
                    "raid_ironcolossus",
                    summoner.Id,
                    maxHp: 500L,
                    expiresAt: DateTimeOffset.UtcNow.AddHours(1),
                    difficulty: RaidDifficulty.Normal,
                    size: size);
                db.ActiveRaids.Add(raid);

                await db.SaveChangesAsync();
                raidId = raid.Id;
            }

            await using (var db = NewDbContext())
            {
                var stored = await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raidId);
                stored.Size.Should().Be(size,
                    $"a {size} raid must persist as {size}, never coerced to the store default");
            }
        }
    }
}
