using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 29 S2 — the daily chest service over an in-memory repository that enforces the same rules
/// as the SQL (one row per player-day, one pick per chest). The real races are in
/// DailyChestIntegrationTests against Postgres.
/// </summary>
public class DailyChestServiceTests
{
    private static readonly Guid PlayerId = Guid.NewGuid();

    // ── fakes ────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeChestRepo : IPlayerDailyChestRepository
    {
        public readonly Dictionary<(Guid, DateOnly), PlayerDailyChest> Rows = new();
        public int Inserts;

        public Task<PlayerDailyChest?> FindAsync(Guid playerId, DateOnly chestDay, CancellationToken ct = default)
            => Task.FromResult(Rows.GetValueOrDefault((playerId, chestDay)));

        public Task<bool> InsertIfAbsentAsync(PlayerDailyChest chest, CancellationToken ct = default)
        {
            if (!Rows.TryAdd((chest.PlayerId, chest.ChestDay), chest)) return Task.FromResult(false);
            Inserts++;
            return Task.FromResult(true);
        }

        public Task<bool> TryPickAsync(Guid chestId, Guid playerId, short index, CancellationToken ct = default)
        {
            var chest = Rows.Values.SingleOrDefault(c => c.Id == chestId && c.PlayerId == playerId);
            if (chest is null || chest.PickedIndex.HasValue) return Task.FromResult(false);
            // The SQL latch sets these; reflection stands in for the UPDATE.
            typeof(PlayerDailyChest).GetProperty(nameof(PlayerDailyChest.PickedIndex))!.SetValue(chest, (short?)index);
            typeof(PlayerDailyChest).GetProperty(nameof(PlayerDailyChest.PickedAt))!.SetValue(chest, (DateTimeOffset?)DateTimeOffset.UtcNow);
            return Task.FromResult(true);
        }
    }

    private sealed class FakePool : IDailyChestPoolProvider
    {
        public FakePool(params DailyChestPoolEntry[] pool) { Pool = pool; TotalWeight = pool.Sum(p => p.Weight); }
        public IReadOnlyList<DailyChestPoolEntry> Pool { get; }
        public int TotalWeight { get; }
    }

    /// <summary>Returns a fixed sequence from Next(max), so weighted draws are exact.</summary>
    private sealed class SequenceRandom : Random
    {
        private readonly Queue<int> _values;
        public SequenceRandom(params int[] values) => _values = new Queue<int>(values);
        public override int Next(int maxValue) => _values.Count > 0 ? _values.Dequeue() : 0;
    }

    private sealed record Harness(
        DailyChestService Service, FakeChestRepo Repo, Mock<IPlayerInventoryRepository> Inventory,
        Mock<IAuditLogRepository> Audit);

    private static Harness Build(IDailyChestPoolProvider? pool = null, Random? random = null)
    {
        var items = new Mock<IItemDefinitionProvider>();
        items.Setup(i => i.GetById("potion_a")).Returns(new ItemDefinition
            { Id = "potion_a", Name = "Potion A", Rarity = ItemRarity.Green, Type = ItemType.Consumable });
        items.Setup(i => i.GetById("potion_b")).Returns(new ItemDefinition
            { Id = "potion_b", Name = "Potion B", Rarity = ItemRarity.Purple, Type = ItemType.Consumable });

        var repo      = new FakeChestRepo();
        var inventory = new Mock<IPlayerInventoryRepository>();
        var audit     = new Mock<IAuditLogRepository>();

        var service = new DailyChestService(
            repo,
            pool ?? new FakePool(new DailyChestPoolEntry("potion_a", 1, 3), new DailyChestPoolEntry("potion_b", 2, 1)),
            items.Object, inventory.Object, new PassThroughPlayerMutationLock(), audit.Object, random);

        return new Harness(service, repo, inventory, audit);
    }

    private static int AuditCount(Mock<IAuditLogRepository> audit, string action)
        => audit.Invocations.Count(i => i.Arguments[0] is AuditLog a && a.Action == action);

    // ── reading ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstRead_RollsSixChests_AndHidesThem()
    {
        var h = Build();

        var chest = await h.Service.GetTodayAsync(PlayerId);

        chest.Picked.Should().BeFalse();
        chest.Chests.Should().BeNull("the contents must never reach the client before the pick");
        h.Repo.Rows.Values.Single().GetContents().Should().HaveCount(6);
        AuditCount(h.Audit, "DailyChestRolled").Should().Be(1);
    }

    [Fact]
    public async Task ReadingAgain_DoesNotReroll()
    {
        var h = Build();
        await h.Service.GetTodayAsync(PlayerId);
        var firstRoll = h.Repo.Rows.Values.Single().ContentsJson;

        await h.Service.GetTodayAsync(PlayerId);
        await h.Service.GetTodayAsync(PlayerId);

        h.Repo.Inserts.Should().Be(1);
        h.Repo.Rows.Values.Single().ContentsJson.Should().Be(firstRoll);
        AuditCount(h.Audit, "DailyChestRolled").Should().Be(1);
    }

    [Fact]
    public async Task ResetsAt_IsTheNextUtcMidnight()
    {
        var h = Build();

        var chest = await h.Service.GetTodayAsync(PlayerId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        chest.Day.Should().Be(today.ToString("yyyy-MM-dd"));
        chest.ResetsAt.Should().Be(new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }

    // ── rolling ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Draws_FollowTheWeights_AtTheBoundaries()
    {
        // potion_a weight 3 owns rolls 0–2, potion_b weight 7 owns 3–9.
        var pool = new FakePool(new DailyChestPoolEntry("potion_a", 1, 3), new DailyChestPoolEntry("potion_b", 1, 7));
        var h = Build(pool, new SequenceRandom(0, 2, 3, 9, 5, 1));

        await h.Service.GetTodayAsync(PlayerId);

        h.Repo.Rows.Values.Single().GetContents().Select(s => s.ItemDefinitionId).Should().Equal(
            "potion_a", "potion_a", "potion_b", "potion_b", "potion_b", "potion_a");
    }

    [Fact]
    public async Task Roll_SnapshotsRarityFromTheItemDefinition()
    {
        var pool = new FakePool(new DailyChestPoolEntry("potion_b", 1, 1));
        var h = Build(pool);

        await h.Service.GetTodayAsync(PlayerId);

        h.Repo.Rows.Values.Single().GetContents().Should().OnlyContain(s => s.Rarity == ItemRarity.Purple);
    }

    // ── picking ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pick_GrantsThatChest_AndRevealsAllSix()
    {
        // Slot 2 is potion_b x2; everything else potion_a.
        var h = Build(random: new SequenceRandom(0, 0, 3, 0, 0, 0));
        await h.Service.GetTodayAsync(PlayerId);

        var result = await h.Service.PickAsync(PlayerId, 2);

        result.Success.Should().BeTrue();
        result.Granted!.ItemDefinitionId.Should().Be("potion_b");
        result.Granted.Quantity.Should().Be(2);
        result.Granted.ItemName.Should().Be("Potion B");
        result.Granted.Rarity.Should().Be("Purple");
        result.Chest!.Picked.Should().BeTrue();
        result.Chest.PickedIndex.Should().Be(2);
        result.Chest.Chests.Should().HaveCount(6);
        result.Chest.Chests![2].ItemDefinitionId.Should().Be("potion_b");

        h.Inventory.Verify(i => i.CreateAsync(
            It.Is<PlayerInventoryItem>(x => x.PlayerId == PlayerId && x.ItemDefinitionId == "potion_b" && x.Quantity == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        AuditCount(h.Audit, "DailyChestPicked").Should().Be(1);
    }

    [Fact]
    public async Task Pick_StacksOntoAnItemAlreadyOwned()
    {
        var h = Build(new FakePool(new DailyChestPoolEntry("potion_a", 1, 1)));
        var owned = PlayerInventoryItem.Create(PlayerId, "potion_a", 4);
        h.Inventory.Setup(i => i.GetAsync(PlayerId, "potion_a", It.IsAny<CancellationToken>())).ReturnsAsync(owned);

        await h.Service.PickAsync(PlayerId, 0);

        owned.Quantity.Should().Be(5);
        h.Inventory.Verify(i => i.UpdateAsync(owned, It.IsAny<CancellationToken>()), Times.Once);
        h.Inventory.Verify(i => i.CreateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SecondPick_IsRefused_AndGrantsNothing()
    {
        var h = Build();
        await h.Service.PickAsync(PlayerId, 1);
        h.Inventory.Invocations.Clear();

        var second = await h.Service.PickAsync(PlayerId, 4);

        second.Success.Should().BeFalse();
        second.FailureCode.Should().Be(DailyChestFailureCode.AlreadyPicked);
        second.Chest!.PickedIndex.Should().Be(1, "the reveal still shows the first pick");
        h.Inventory.Invocations.Should().BeEmpty();
        AuditCount(h.Audit, "DailyChestPicked").Should().Be(1);
    }

    [Fact]
    public async Task ReadingAfterThePick_ShowsTheReveal()
    {
        var h = Build();
        await h.Service.PickAsync(PlayerId, 3);

        var chest = await h.Service.GetTodayAsync(PlayerId);

        chest.Picked.Should().BeTrue();
        chest.PickedIndex.Should().Be(3);
        chest.Chests.Should().HaveCount(6);
    }

    [Fact]
    public async Task PickWithoutReadingFirst_RollsThenPicks()
    {
        var h = Build();

        var result = await h.Service.PickAsync(PlayerId, 0);

        result.Success.Should().BeTrue();
        h.Repo.Inserts.Should().Be(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task PickOutOfRange_IsRefused_WithoutRolling(int index)
    {
        var h = Build();

        var result = await h.Service.PickAsync(PlayerId, index);

        result.FailureCode.Should().Be(DailyChestFailureCode.InvalidIndex);
        h.Repo.Rows.Should().BeEmpty();
    }
}
