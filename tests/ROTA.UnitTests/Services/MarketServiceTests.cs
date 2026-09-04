using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 27 — the player market.
///
/// These are written against the threat model in docs/design/PLAYER_MARKET.md §4 rather than against
/// the happy path, because the happy path is the easy half. The properties that actually matter, and
/// which each have a test below:
///
///   - listing ESCROWS, so the stack is gone from inventory the moment it is on the board;
///   - the sale LATCH runs before any gold moves, so a lost race charges nobody;
///   - the seller's credit is ATOMIC, not a read-modify-write;
///   - the daily caps read the LEDGER, not a counter, and are checked before the latch;
///   - a cancel returns the goods and does NOT refund the listing fee;
///   - an expiry returns the goods, because a listing that lapses is holding something.
/// </summary>
public class MarketServiceTests
{
    private record Bundle(
        MarketService Service,
        Mock<IMarketListingRepository> Listings,
        Mock<IMarketTransactionRepository> Ledger,
        Mock<IPlayerRepository> Players,
        Mock<IPlayerInventoryRepository> Inventory,
        Mock<IPlayerGearRepository> Gear,
        Mock<IPlayerEquipmentRepository> Equipped,
        Mock<IAuditLogRepository> AuditLog,
        MarketConfig Config);

    private static readonly Guid SellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BuyerId  = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Player MakePlayer(Guid id, long gold = 1_000_000, int level = 100, int ageHours = 500)
    {
        var p = Player.CreateWithId(id, $"p{id:N}"[..12], $"{id:N}@t.local", "hash");
        p.AddGold(gold);
        for (int i = 1; i < level; i++) p.AddExperience(0, _ => 0);
        typeof(Player).GetProperty(nameof(Player.Level))!
            .SetValue(p, level);
        typeof(Player).GetProperty(nameof(Player.CreatedAt))!
            .SetValue(p, DateTimeOffset.UtcNow.AddHours(-ageHours));
        return p;
    }

    private static Bundle Build(Action<MarketConfig>? tune = null)
    {
        var listings  = new Mock<IMarketListingRepository>();
        var ledger    = new Mock<IMarketTransactionRepository>();
        var players   = new Mock<IPlayerRepository>();
        var inventory = new Mock<IPlayerInventoryRepository>();
        var gear      = new Mock<IPlayerGearRepository>();
        var equipped  = new Mock<IPlayerEquipmentRepository>();
        var itemDefs  = new Mock<IItemDefinitionProvider>();
        var gearDefs  = new Mock<IGearDefinitionProvider>();
        var auditLog  = new Mock<IAuditLogRepository>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        equipped.Setup(e => e.GetEquippedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlayerEquipment>());
        listings.Setup(l => l.CountActiveForSellerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        ledger.Setup(l => l.SumProceedsSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        ledger.Setup(l => l.SumSpendSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        itemDefs.Setup(d => d.GetById("mat_rime_glass")).Returns(new ItemDefinition
        {
            Id = "mat_rime_glass", Name = "Rime-Glass", Rarity = ItemRarity.Blue, Type = ItemType.Material,
        });
        itemDefs.Setup(d => d.GetById("potion_energy_minor")).Returns(new ItemDefinition
        {
            Id = "potion_energy_minor", Name = "Minor Energy Draught",
            Rarity = ItemRarity.Green, Type = ItemType.Consumable,
        });
        gearDefs.Setup(d => d.GetById("gear_relay_hood")).Returns(new GearDefinition
        {
            Id = "gear_relay_hood", Name = "Relaykeeper's Hood", Rarity = ItemRarity.Blue, Slot = "Head",
        });

        var config = new MarketConfig { Enabled = true };
        tune?.Invoke(config);

        var service = new MarketService(
            listings.Object, ledger.Object, players.Object, inventory.Object, gear.Object,
            equipped.Object, itemDefs.Object, gearDefs.Object,
            new ROTA.UnitTests.TestSupport.PassThroughPlayerMutationLock(),
            auditLog.Object, Options.Create(config));

        return new Bundle(service, listings, ledger, players, inventory, gear, equipped, auditLog, config);
    }

    private static void SetupPlayer(Bundle b, Player p)
        => b.Players.Setup(r => r.FindByIdAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);

    private static void SetupInventory(Bundle b, Guid playerId, string id, int qty)
        => b.Inventory.Setup(r => r.GetAsync(playerId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerInventoryItem.Create(playerId, id, qty));

    // ─────────────────────────────────────────────────────────────────────── listing escrows

    [Fact]
    public async Task Listing_TakesTheStackOutOfInventory_Immediately()
    {
        // THE PROPERTY THAT MAKES EVERYTHING ELSE SAFE. If the goods stayed in inventory until a sale,
        // there would be a window in which a seller can craft with, use, or re-list what is already on
        // the board — and a window in an economy is a duplication bug waiting for load.
        var b = Build();
        var seller = MakePlayer(SellerId);
        SetupPlayer(b, seller);
        SetupInventory(b, SellerId, "mat_rime_glass", 10);
        b.Players.Setup(r => r.TrySpendGoldAsync(SellerId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(999_000L);

        var result = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Item", DefinitionId = "mat_rime_glass", Quantity = 4, UnitPrice = 5_000,
        });

        result.Success.Should().BeTrue();
        b.Inventory.Verify(r => r.UpdateAsync(
            It.Is<PlayerInventoryItem>(i => i.Quantity == 6), It.IsAny<CancellationToken>()), Times.Once);
        b.Listings.Verify(l => l.CreateAsync(It.IsAny<MarketListing>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listing_ChargesTheFeeBeforeEscrow_SoAPoorSellerKeepsTheirStack()
    {
        // Ordering matters. Escrow-then-charge would take the goods and then refuse, and the refund
        // path for that is exactly the sort of thing that gets written wrong once and lived with.
        var b = Build();
        SetupPlayer(b, MakePlayer(SellerId, gold: 10));
        SetupInventory(b, SellerId, "mat_rime_glass", 10);
        b.Players.Setup(r => r.TrySpendGoldAsync(SellerId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var result = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Item", DefinitionId = "mat_rime_glass", Quantity = 4, UnitPrice = 5_000,
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MarketFailureCode.InsufficientGold);
        b.Inventory.Verify(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Listings.Verify(l => l.CreateAsync(It.IsAny<MarketListing>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Listing_RefusesAConsumable_BecauseAMarketThatSellsPotionsSellsPacing()
    {
        // PLAYER_MARKET.md §5: potions are the pacing lever past low level, so a market that trades
        // them makes the autolevelling threshold purchasable. Not a balance preference — a structural
        // interaction, and the default config keeps consumables off the board entirely.
        var b = Build();
        SetupPlayer(b, MakePlayer(SellerId));
        SetupInventory(b, SellerId, "potion_energy_minor", 10);

        var result = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Item", DefinitionId = "potion_energy_minor", Quantity = 1, UnitPrice = 5_000,
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MarketFailureCode.NotTradeable);
    }

    [Fact]
    public async Task Listing_RefusesToSellTheShirtOffYourBack()
    {
        // Counts equipped copies rather than treating equipped as a flag, so a player holding two may
        // sell the spare. A flag would either block a legitimate sale or let someone sell what they
        // are wearing.
        var b = Build();
        SetupPlayer(b, MakePlayer(SellerId));
        b.Gear.Setup(r => r.GetAsync(SellerId, "gear_relay_hood", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerGear.Create(SellerId, "gear_relay_hood", 1));
        b.Equipped.Setup(e => e.GetEquippedAsync(SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { PlayerEquipment.Create(SellerId, EquipmentSlot.Head, "gear_relay_hood") });

        var result = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Gear", DefinitionId = "gear_relay_hood", Quantity = 1, UnitPrice = 50_000,
        });

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MarketFailureCode.ItemInUse);
    }

    [Theory]
    [InlineData(1, 20, MarketFailureCode.LevelTooLow)]
    [InlineData(100, 1, MarketFailureCode.AccountTooNew)]
    public async Task Listing_IsGatedOnLevelAndAccountAge(int level, int ageHours, MarketFailureCode expected)
    {
        // The cheapest anti-mule measures there are: a farm-and-dump operation has to grow its mules
        // before it can use them, which is most of the point.
        var b = Build();
        SetupPlayer(b, MakePlayer(SellerId, level: level, ageHours: ageHours));
        SetupInventory(b, SellerId, "mat_rime_glass", 10);

        var result = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Item", DefinitionId = "mat_rime_glass", Quantity = 1, UnitPrice = 5_000,
        });

        result.FailureCode.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────────────────── the sale latch

    private static MarketListing ActiveListing(long unitPrice = 10_000, int qty = 2)
        => MarketListing.Create(SellerId, MarketItemKind.Item, "mat_rime_glass", qty, unitPrice,
            DateTimeOffset.UtcNow.AddHours(24));

    [Fact]
    public async Task LosingTheRaceForAListing_ChargesNothing()
    {
        // THE ORDERING PROPERTY. The latch runs before any gold moves, so the buyer who arrives second
        // is told the listing is gone rather than paying for goods the winner already has.
        var b = Build();
        var listing = ActiveListing();
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        SetupPlayer(b, MakePlayer(BuyerId));
        b.Listings.Setup(l => l.TryClaimForSaleAsync(
                listing.Id, BuyerId, It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);                       // somebody else got there first

        var result = await b.Service.BuyAsync(BuyerId, listing.Id);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MarketFailureCode.NoLongerActive);
        b.Players.Verify(r => r.TrySpendGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Players.Verify(r => r.AddGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Ledger.Verify(l => l.AppendAsync(It.IsAny<MarketTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ABuy_PaysTheSellerAtomically_TakingTheFeeOutOfTheProceeds()
    {
        var b = Build(c => { c.SaleFeeRate = 0.08; });
        var listing = ActiveListing(unitPrice: 10_000, qty: 2);   // total 20,000, fee 1,600
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        SetupPlayer(b, MakePlayer(BuyerId));
        b.Listings.Setup(l => l.TryClaimForSaleAsync(
                listing.Id, BuyerId, 20_000L, 1_600L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.Players.Setup(r => r.TrySpendGoldAsync(BuyerId, 20_000L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(980_000L);
        b.Players.Setup(r => r.AddGoldAsync(SellerId, 18_400L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(18_400L);
        b.Inventory.Setup(r => r.GetAsync(BuyerId, "mat_rime_glass", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerInventoryItem?)null);

        var result = await b.Service.BuyAsync(BuyerId, listing.Id);

        result.Success.Should().BeTrue();
        result.GoldSpent.Should().Be(20_000);
        // AddGoldAsync, not a tracked AddGold: the credit happens under the BUYER's lock, so the
        // seller is not serialised against their own activity and a read-modify-write would lose it.
        b.Players.Verify(r => r.AddGoldAsync(SellerId, 18_400L, It.IsAny<CancellationToken>()), Times.Once);
        b.Ledger.Verify(l => l.AppendAsync(
            It.Is<MarketTransaction>(t => t.TotalPrice == 20_000
                                       && t.SaleFee == 1_600
                                       && t.SellerProceeds == 18_400
                                       && t.BuyerId == BuyerId
                                       && t.SellerId == SellerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task YouCannotBuyYourOwnListing()
    {
        var b = Build();
        var listing = ActiveListing();
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        SetupPlayer(b, MakePlayer(SellerId));

        var result = await b.Service.BuyAsync(SellerId, listing.Id);

        result.FailureCode.Should().Be(MarketFailureCode.CannotBuyOwnListing);
        b.Listings.Verify(l => l.TryClaimForSaleAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnExpiredListingCannotBeBought_EvenBeforeTheSweepReachesIt()
    {
        // The sweep runs every five minutes, so there is always a window in which a lapsed listing is
        // still sitting at status Active. The read path must refuse it on the clock, not on the status.
        var b = Build();
        var listing = MarketListing.Create(SellerId, MarketItemKind.Item, "mat_rime_glass", 1, 10_000,
            DateTimeOffset.UtcNow.AddSeconds(-1));
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        SetupPlayer(b, MakePlayer(BuyerId));

        var result = await b.Service.BuyAsync(BuyerId, listing.Id);

        result.FailureCode.Should().Be(MarketFailureCode.NoLongerActive);
    }

    // ─────────────────────────────────────────────────────────────────────── the daily caps

    [Fact]
    public async Task ASaleThatWouldBreachTheSellersDailyIntake_IsRefusedBeforeTheLatch()
    {
        // The anti-mule cap that actually bites. A mule transfer is a sale at an absurd price and no
        // per-item price band can tell that from a genuinely valuable item — nothing knows what things
        // are worth. A rolling daily ceiling does not need to know.
        var b = Build(c => { c.MaxGoldReceivedPerDay = 100_000; });
        var listing = ActiveListing(unitPrice: 90_000, qty: 1);
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        SetupPlayer(b, MakePlayer(BuyerId));
        b.Ledger.Setup(l => l.SumProceedsSinceAsync(SellerId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50_000L);

        var result = await b.Service.BuyAsync(BuyerId, listing.Id);

        result.FailureCode.Should().Be(MarketFailureCode.DailyGoldReceivedCapReached);
        b.Listings.Verify(l => l.TryClaimForSaleAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ABuyThatWouldBreachTheBuyersDailySpend_IsRefusedBeforeTheLatch()
    {
        var b = Build(c => { c.MaxGoldSpentPerDay = 25_000; });
        var listing = ActiveListing(unitPrice: 10_000, qty: 2);
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        SetupPlayer(b, MakePlayer(BuyerId));
        b.Ledger.Setup(l => l.SumSpendSinceAsync(BuyerId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(20_000L);

        var result = await b.Service.BuyAsync(BuyerId, listing.Id);

        result.FailureCode.Should().Be(MarketFailureCode.DailySpendCapReached);
        b.Listings.Verify(l => l.TryClaimForSaleAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────── cancel and expiry

    [Fact]
    public async Task Cancelling_ReturnsTheGoods_AndDoesNotRefundTheListingFee()
    {
        // The fee prices the act of occupying the board. Refunding it on cancel would make
        // price-probing free, which is the behaviour the fee exists to charge for.
        var b = Build();
        var listing = ActiveListing(qty: 3);
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        b.Listings.Setup(l => l.TryCancelAsync(listing.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.Inventory.Setup(r => r.GetAsync(SellerId, "mat_rime_glass", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerInventoryItem?)null);

        var result = await b.Service.CancelAsync(SellerId, listing.Id);

        result.Success.Should().BeTrue();
        result.QuantityReturned.Should().Be(3);
        result.ListingFeeRefunded.Should().Be(0);
        b.Inventory.Verify(r => r.CreateAsync(
            It.Is<PlayerInventoryItem>(i => i.Quantity == 3), It.IsAny<CancellationToken>()), Times.Once);
        b.Players.Verify(r => r.AddGoldAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancellingAListingThatJustSold_ReturnsNothing()
    {
        // The cancel latch is what stops a seller racing a buyer and ending up with both the gold and
        // the goods. The read said Active; the write is what decides.
        var b = Build();
        var listing = ActiveListing();
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        b.Listings.Setup(l => l.TryCancelAsync(listing.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Service.CancelAsync(SellerId, listing.Id);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MarketFailureCode.NoLongerActive);
        b.Inventory.Verify(r => r.CreateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Inventory.Verify(r => r.UpdateAsync(It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AStrangerCannotCancelYourListing()
    {
        var b = Build();
        var listing = ActiveListing();
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);

        var result = await b.Service.CancelAsync(BuyerId, listing.Id);

        result.FailureCode.Should().Be(MarketFailureCode.NotYours);
        b.Listings.Verify(l => l.TryCancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ALapsedListing_GivesTheGoodsBack()
    {
        // The defect shape this codebase has been bitten by twice: a clock runs out and nothing runs.
        // A market listing is the worst case, because the listing is HOLDING the seller's stack.
        var b = Build();
        var listing = MarketListing.Create(SellerId, MarketItemKind.Item, "mat_rime_glass", 5, 10_000,
            DateTimeOffset.UtcNow.AddHours(-1));
        b.Listings.Setup(l => l.GetExpiredActiveAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { listing });
        b.Listings.Setup(l => l.TryExpireAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        b.Inventory.Setup(r => r.GetAsync(SellerId, "mat_rime_glass", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerInventoryItem?)null);

        var settled = await b.Service.SettleExpiredListingsAsync();

        settled.Should().Be(1);
        b.Inventory.Verify(r => r.CreateAsync(
            It.Is<PlayerInventoryItem>(i => i.PlayerId == SellerId && i.Quantity == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ASecondSweepOverTheSameListing_ReturnsTheGoodsOnlyOnce()
    {
        // Idempotence under the status latch, which is what lets a double-fire, a restart mid-sweep,
        // or a second app instance all run without duplicating a stack.
        var b = Build();
        var listing = MarketListing.Create(SellerId, MarketItemKind.Item, "mat_rime_glass", 5, 10_000,
            DateTimeOffset.UtcNow.AddHours(-1));
        b.Listings.Setup(l => l.GetExpiredActiveAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { listing });
        b.Listings.SetupSequence(l => l.TryExpireAsync(listing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);                        // the second sweep loses the latch
        b.Inventory.Setup(r => r.GetAsync(SellerId, "mat_rime_glass", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerInventoryItem?)null);

        (await b.Service.SettleExpiredListingsAsync()).Should().Be(1);
        (await b.Service.SettleExpiredListingsAsync()).Should().Be(0);

        b.Inventory.Verify(r => r.CreateAsync(
            It.IsAny<PlayerInventoryItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACancelStillWorksWhileTheMarketIsSwitchedOff()
    {
        // Closing the market must not confiscate what it is holding. Buying and listing are gated on
        // Enabled; cancelling deliberately is not.
        var b = Build(c => { c.Enabled = false; });
        var listing = ActiveListing(qty: 2);
        b.Listings.Setup(l => l.FindByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        b.Listings.Setup(l => l.TryCancelAsync(listing.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.Inventory.Setup(r => r.GetAsync(SellerId, "mat_rime_glass", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerInventoryItem?)null);

        var result = await b.Service.CancelAsync(SellerId, listing.Id);

        result.Success.Should().BeTrue();
        result.QuantityReturned.Should().Be(2);
    }

    [Fact]
    public async Task WhileTheMarketIsOff_NothingCanBeListedOrBought()
    {
        var b = Build(c => { c.Enabled = false; });

        var listed = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Item", DefinitionId = "mat_rime_glass", Quantity = 1, UnitPrice = 5_000,
        });
        var bought = await b.Service.BuyAsync(BuyerId, Guid.NewGuid());

        listed.FailureCode.Should().Be(MarketFailureCode.MarketDisabled);
        bought.FailureCode.Should().Be(MarketFailureCode.MarketDisabled);
    }

    // ─────────────────────────────────────────────────────────────────────── price and fee arithmetic

    [Theory]
    [InlineData(50)]              // under MinUnitPrice
    [InlineData(2_000_000_000)]   // over MaxUnitPrice
    public async Task APriceOutsideTheBand_IsRefused(long unitPrice)
    {
        var b = Build();
        SetupPlayer(b, MakePlayer(SellerId));
        SetupInventory(b, SellerId, "mat_rime_glass", 10);

        var result = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Item", DefinitionId = "mat_rime_glass", Quantity = 1, UnitPrice = unitPrice,
        });

        result.FailureCode.Should().Be(MarketFailureCode.PriceOutOfRange);
    }

    [Fact]
    public async Task TheListingFeeRoundsUp_SoASmallListingIsNotFree()
    {
        // A fee that rounds down is a fee a player avoids by listing small enough, and a thousand
        // fee-free listings is exactly the spam the fee exists to price. 2% of 101 is 2.02 -> 3.
        var b = Build(c => { c.ListingFeeRate = 0.02; c.MinUnitPrice = 1; });
        SetupPlayer(b, MakePlayer(SellerId));
        SetupInventory(b, SellerId, "mat_rime_glass", 10);
        long charged = -1;
        b.Players.Setup(r => r.TrySpendGoldAsync(SellerId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, long, CancellationToken>((_, amt, _) => charged = amt)
            .ReturnsAsync(1_000L);

        var result = await b.Service.CreateListingAsync(SellerId, new CreateListingRequest
        {
            Kind = "Item", DefinitionId = "mat_rime_glass", Quantity = 1, UnitPrice = 101,
        });

        result.Success.Should().BeTrue();
        charged.Should().Be(3, "2% of 101 is 2.02, and a fee must never round to less than it is");
    }
}
