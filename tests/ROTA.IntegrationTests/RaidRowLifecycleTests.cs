using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// A looted raid is not stored. The claim deletes the participation inside its own transaction, the
// last claimant's conditional delete takes the raid, and the sweeper's spent-raid query removes what
// nobody will ever claim. These are the three statements, run against a real database, because each
// one is SQL whose shape (a cascade, a NOT EXISTS, a four-way OR over lifecycle and clock) a mock
// cannot vouch for.
public class RaidRowLifecycleTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_raidrows_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
        => new(new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private static Player NewPlayer()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Player.Create($"rows_{suffix}", $"{suffix}@test.dev", "hash");
    }

    private async Task<bool> RaidExistsAsync(Guid raidId)
    {
        await using var db = NewDbContext();
        return await db.ActiveRaids.AsNoTracking().AnyAsync(r => r.Id == raidId);
    }

    private async Task<int> ParticipantCountAsync(Guid raidId)
    {
        await using var db = NewDbContext();
        return await db.RaidParticipants.AsNoTracking().CountAsync(p => p.ActiveRaidId == raidId);
    }

    private async Task<int> MagicCountAsync(Guid raidId)
    {
        await using var db = NewDbContext();
        return await db.RaidMagics.AsNoTracking().CountAsync(m => m.ActiveRaidId == raidId);
    }

    [Fact]
    public async Task TheClaimDeletesTheRow_AndTheLastClaimantDeletesTheRaid()
    {
        var a = NewPlayer();
        var b = NewPlayer();
        var raid = ActiveRaid.Create("raid_test", a.Id, 1000, DateTimeOffset.UtcNow.AddHours(1));
        raid.MarkDefeated();
        var partA = RaidParticipant.Create(raid.Id, a.Id);
        var partB = RaidParticipant.Create(raid.Id, b.Id);
        await using (var db = NewDbContext())
        {
            db.Players.AddRange(a, b);
            db.ActiveRaids.Add(raid);
            db.RaidParticipants.AddRange(partA, partB);
            db.RaidMagics.Add(RaidMagic.Create(raid.Id, "magic_test", a.Id));
            await db.SaveChangesAsync();
        }

        // A claims, as LootRaidAsync does: latch and delete under the participant's advisory lock.
        await using (var db = NewDbContext())
        {
            var raids = new ActiveRaidRepository(db);
            var participants = new RaidParticipantRepository(db);
            var won = await raids.AtomicWithAdvisoryLockAsync(partA.Id, async () =>
            {
                if (!await participants.TryClaimRewardsAsync(partA.Id, DateTimeOffset.UtcNow)) return false;
                await participants.DeleteAsync(partA.Id);
                return true;
            });
            won.Should().BeTrue();
            (await raids.DeleteIfEmptyAsync(raid.Id)).Should().BeFalse("B has not claimed yet");
        }
        (await ParticipantCountAsync(raid.Id)).Should().Be(1);
        (await RaidExistsAsync(raid.Id)).Should().BeTrue();

        // B claims: the raid goes with the last row, and the magics with the raid.
        await using (var db = NewDbContext())
        {
            var raids = new ActiveRaidRepository(db);
            var participants = new RaidParticipantRepository(db);
            await raids.AtomicWithAdvisoryLockAsync(partB.Id, async () =>
            {
                if (!await participants.TryClaimRewardsAsync(partB.Id, DateTimeOffset.UtcNow)) return false;
                await participants.DeleteAsync(partB.Id);
                return true;
            });
            (await raids.DeleteIfEmptyAsync(raid.Id)).Should().BeTrue();
            (await raids.DeleteIfEmptyAsync(raid.Id)).Should().BeFalse("already gone");
        }
        (await RaidExistsAsync(raid.Id)).Should().BeFalse();
        (await ParticipantCountAsync(raid.Id)).Should().Be(0);
        (await MagicCountAsync(raid.Id)).Should().Be(0);
    }

    [Fact]
    public async Task AFailedClaimRollsTheRowBack()
    {
        var p = NewPlayer();
        var raid = ActiveRaid.Create("raid_test", p.Id, 1000, DateTimeOffset.UtcNow.AddHours(1));
        raid.MarkDefeated();
        var part = RaidParticipant.Create(raid.Id, p.Id);
        await using (var db = NewDbContext())
        {
            db.Players.Add(p);
            db.ActiveRaids.Add(raid);
            db.RaidParticipants.Add(part);
            await db.SaveChangesAsync();
        }

        await using (var db = NewDbContext())
        {
            var raids = new ActiveRaidRepository(db);
            var participants = new RaidParticipantRepository(db);
            var act = () => raids.AtomicWithAdvisoryLockAsync(part.Id, async () =>
            {
                await participants.TryClaimRewardsAsync(part.Id, DateTimeOffset.UtcNow);
                await participants.DeleteAsync(part.Id);
                throw new InvalidOperationException("a grant failed after the delete");
            });
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        (await ParticipantCountAsync(raid.Id)).Should().Be(1, "the delete rode the transaction that failed");
        await using (var db = NewDbContext())
            (await db.RaidParticipants.AsNoTracking().Where(x => x.Id == part.Id).Select(x => x.RewardedAt).FirstAsync())
                .Should().BeNull("and so did the latch");
    }

    [Fact]
    public async Task GetSpent_PicksOnlyRaidsWithNothingLeftToDo()
    {
        var now = DateTimeOffset.UtcNow;
        var p = NewPlayer();
        var ev = GauntletEvent.Create("rows", now.AddDays(-30), now.AddDays(30));

        ActiveRaid Make(long maxHp, DateTimeOffset expiresAt)
            => ActiveRaid.Create("raid_test", p.Id, maxHp, expiresAt);

        var forfeited    = Make(1000, now.AddDays(-8)); forfeited.MarkDefeated();      // Lootable, window passed
        var stillClaimable = Make(1000, now.AddDays(-1)); stillClaimable.MarkDefeated(); // Lootable, inside the window
        var failed       = Make(1000, now.AddHours(-1));                               // Active, out of time, health left
        var unsettled    = Make(0, now.AddHours(-1));                                  // timer raid: settlement's, not ours
        var live         = Make(1000, now.AddHours(1));                                // Active, running
        var legacyLooted = Make(1000, now.AddDays(-1)); legacyLooted.MarkDefeated(); legacyLooted.Loot();
        var stage        = Make(1000, now.AddDays(-8)); stage.MarkDefeated(); stage.LinkGauntletEvent(ev.Id);

        await using (var db = NewDbContext())
        {
            db.Players.Add(p);
            db.GauntletEvents.Add(ev);
            db.ActiveRaids.AddRange(forfeited, stillClaimable, failed, unsettled, live, legacyLooted, stage);
            await db.SaveChangesAsync();
        }

        await using var q = NewDbContext();
        var spent = await new ActiveRaidRepository(q).GetSpentAsync(now, now.AddDays(-7), 50);
        var ids = spent.Select(r => r.Id).ToList();

        ids.Should().BeEquivalentTo(new[] { forfeited.Id, failed.Id, legacyLooted.Id });
        ids.Should().NotContain(stage.Id, "the ladder keeps its stages");
        ids.Should().NotContain(unsettled.Id, "a timer raid past its clock is settled first, then claimed");
    }

    [Fact]
    public async Task Delete_TakesParticipantsAndMagicsWithTheRaid()
    {
        var p = NewPlayer();
        var raid = ActiveRaid.Create("raid_test", p.Id, 1000, DateTimeOffset.UtcNow.AddHours(-1));
        await using (var db = NewDbContext())
        {
            db.Players.Add(p);
            db.ActiveRaids.Add(raid);
            db.RaidParticipants.Add(RaidParticipant.Create(raid.Id, p.Id));
            db.RaidMagics.Add(RaidMagic.Create(raid.Id, "magic_test", p.Id));
            await db.SaveChangesAsync();
        }

        await using (var db = NewDbContext())
        {
            var raids = new ActiveRaidRepository(db);
            (await raids.DeleteAsync(raid.Id)).Should().BeTrue();
            (await raids.DeleteAsync(raid.Id)).Should().BeFalse();
        }

        (await RaidExistsAsync(raid.Id)).Should().BeFalse();
        (await ParticipantCountAsync(raid.Id)).Should().Be(0);
        (await MagicCountAsync(raid.Id)).Should().Be(0);
    }
}
