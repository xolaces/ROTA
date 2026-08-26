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
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
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

    // Ticket 50 — visibility + lifecycle round-trip (replaces the old System 19 is_public test). New
    // summons start Private + Active. ShareTo() moves the visibility tier; MarkDefeated() flips lifecycle
    // to Lootable; Loot() flips it to Looted. Guards the AddRaidVisibilityModel migration + EF mapping.
    [Fact]
    public async Task ActiveRaid_Visibility_DefaultsPrivate_AndShareTo_Persists()
    {
        Guid raidId;
        await using (var db = NewDbContext())
        {
            var summoner = Player.Create("sum_pub", "pub@persist.test", "hash");
            db.Players.Add(summoner);

            var raid = ActiveRaid.Create(
                "raid_ironcolossus",
                summoner.Id,
                maxHp: 500L,
                expiresAt: DateTimeOffset.UtcNow.AddHours(1),
                difficulty: RaidDifficulty.Normal,
                size: RaidSize.Small);
            db.ActiveRaids.Add(raid);
            await db.SaveChangesAsync();
            raidId = raid.Id;
        }

        // Newly summoned raids must be Private + Active.
        await using (var db = NewDbContext())
        {
            var stored = await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raidId);
            stored.Visibility.Should().Be(RaidVisibility.Private, "new summons start private");
            stored.LifecycleState.Should().Be(RaidLifecycleState.Active, "new summons start active");
        }

        // ShareTo(GuildOnly) moves the tier; the update must persist.
        await using (var db = NewDbContext())
        {
            var raid = await db.ActiveRaids.FirstAsync(r => r.Id == raidId);
            raid.ShareTo(RaidVisibility.GuildOnly);
            await db.SaveChangesAsync();
        }

        await using (var db = NewDbContext())
        {
            var stored = await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raidId);
            stored.Visibility.Should().Be(RaidVisibility.GuildOnly, "ShareTo() must persist the tier");
        }
    }

    // Ticket 50 — lifecycle round-trip: MarkDefeated() → Lootable, then Loot() → Looted, both persisting.
    [Fact]
    public async Task ActiveRaid_Lifecycle_DefeatThenLoot_Persists()
    {
        Guid raidId;
        await using (var db = NewDbContext())
        {
            var summoner = Player.Create("sum_loot", "loot@persist.test", "hash");
            db.Players.Add(summoner);

            var raid = ActiveRaid.Create(
                "raid_ironcolossus",
                summoner.Id,
                maxHp: 500L,
                expiresAt: DateTimeOffset.UtcNow.AddHours(1),
                difficulty: RaidDifficulty.Normal,
                size: RaidSize.Small);
            db.ActiveRaids.Add(raid);
            await db.SaveChangesAsync();
            raidId = raid.Id;
        }

        // Defeating the raid flips lifecycle to Lootable.
        await using (var db = NewDbContext())
        {
            var raid = await db.ActiveRaids.FirstAsync(r => r.Id == raidId);
            raid.MarkDefeated();
            await db.SaveChangesAsync();
        }
        await using (var db = NewDbContext())
        {
            var stored = await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raidId);
            stored.LifecycleState.Should().Be(RaidLifecycleState.Lootable, "a defeated raid is lootable");
        }

        // Looting (dismissing) flips it to Looted without soft-deleting the row.
        await using (var db = NewDbContext())
        {
            var raid = await db.ActiveRaids.FirstAsync(r => r.Id == raidId);
            raid.Loot();
            await db.SaveChangesAsync();
        }
        await using (var db = NewDbContext())
        {
            var stored = await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raidId);
            stored.LifecycleState.Should().Be(RaidLifecycleState.Looted, "Loot() must persist Looted");
            stored.IsDeleted.Should().BeFalse("Loot() must NOT soft-delete (history/FK stay intact)");
        }
    }
}
