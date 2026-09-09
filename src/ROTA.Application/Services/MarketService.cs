using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// the player market (prototype). Built to the threat model in
/// <c>docs/design/PLAYER_MARKET.md</c> §4, which is worth restating because every design choice below
/// falls out of it: once gold has real value, every economy bug becomes an economy-wide event, gold
/// acquires real-money value, and automation stops being tempting and becomes rational.
///
/// FIVE STRUCTURAL DECISIONS, IN THE ORDER THEY MATTER.
///
/// 1. CONSIGNMENT, NOT TRADE. There is no endpoint anywhere that moves an object from one named
///    player to another named player. A seller posts to a public board at a public price and anyone
///    may take it. That single choice removes the trade-window scam, makes muling a public act at a
///    price a stranger can snipe, and means there is no "gift" call for a compromised account to use.
///
/// 2. THE LISTING HOLDS THE GOODS. Listing ESCROWS the stack out of inventory. While it is on the
///    board the seller cannot use it, craft with it, or list it again, because they do not have it.
///    The alternative — checking ownership at sale time — has a window, and windows in an economy are
///    duplication bugs waiting for load.
///
/// 3. THE SALE IS ONE LATCH, AND IT COMES FIRST. Buying is a conditional UPDATE guarded on
///    status = Active. Two buyers pressing together produce exactly one winner; the loser's UPDATE
///    matches no row. The latch runs BEFORE any gold moves, and everything after it is inside the
///    buyer's mutation-lock transaction, so a failure anywhere rolls the latch back with it. There is
///    no ordering in which a player is charged for something they did not receive.
///
/// 4. GOLD MOVES ATOMICALLY ON BOTH SIDES. The buyer's debit is the existing conditional
///    TrySpendGoldAsync. The seller's credit is a matching atomic increment, NOT a read-modify-write:
///    the credit happens inside the BUYER's lock, so the seller is not serialised against their own
///    concurrent activity, and a read-modify-write there would lose gold exactly the way skill-point
///    grants were losing them before 251fec1.
///
/// 5. THE CAPS ARE ON GOLD MOVED PER DAY, NOT ON PRICE. A mule transfer is a sale at an absurd price,
///    and no per-item price band can tell that from a genuinely valuable item — the band would have to
///    know what things are worth, and nothing does. A rolling daily ceiling on gold received and gold
///    spent does not need to know, and it is read from the append-only ledger rather than any mutable
///    counter.
///
/// WHAT IS DELIBERATELY NOT HERE. Consumables are not tradeable (PLAYER_MARKET.md §5: a market that
/// sells potions makes the autolevelling threshold purchasable). Units, legions and magics are not
/// tradeable because they are own-once and would need a buyer-already-owns-it refund path. Sigils are
/// not tradeable because they summon raids, which makes them a service. Gems are not tradeable at all
/// — the whale-to-F2P bridge is an owner decision with the Gauntlet gem bundle as a stated
/// prerequisite, and it is not this prototype's to make.
/// </summary>
public sealed class MarketService : IMarketService
{
    private readonly IMarketListingRepository _listings;
    private readonly IMarketTransactionRepository _ledger;
    private readonly IPlayerRepository _players;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IPlayerGearRepository _gear;
    private readonly IPlayerEquipmentRepository _equipped;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly IGearDefinitionProvider _gearDefs;
    private readonly IPlayerMutationLock _mutationLock;
    private readonly IAuditLogRepository _auditLog;
    private readonly MarketConfig _config;

    public MarketService(
        IMarketListingRepository listings,
        IMarketTransactionRepository ledger,
        IPlayerRepository players,
        IPlayerInventoryRepository inventory,
        IPlayerGearRepository gear,
        IPlayerEquipmentRepository equipped,
        IItemDefinitionProvider itemDefs,
        IGearDefinitionProvider gearDefs,
        IPlayerMutationLock mutationLock,
        IAuditLogRepository auditLog,
        IOptions<MarketConfig> config)
    {
        _listings     = listings;
        _ledger       = ledger;
        _players      = players;
        _inventory    = inventory;
        _gear         = gear;
        _equipped     = equipped;
        _itemDefs     = itemDefs;
        _gearDefs     = gearDefs;
        _mutationLock = mutationLock;
        _auditLog     = auditLog;
        _config       = config.Value;
    }

    // ───────────────────────────────────────────────────────────────────────────── read

    public async Task<MarketBrowseResponse> BrowseAsync(
        Guid playerId, string? kind, string? definitionId, int page, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        var rows = _config.Enabled
            ? await _listings.BrowseAsync(kind, definitionId, Math.Max(1, page), _config.BrowsePageSize, ct)
            : Array.Empty<MarketListing>();

        return await BuildBrowseAsync(playerId, player?.Gold ?? 0, rows, page, ct);
    }

    public async Task<MarketBrowseResponse> GetMyListingsAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        var rows = await _listings.GetForSellerAsync(playerId, ct);
        return await BuildBrowseAsync(playerId, player?.Gold ?? 0, rows, 1, ct);
    }

    private async Task<MarketBrowseResponse> BuildBrowseAsync(
        Guid playerId, long gold, IReadOnlyList<MarketListing> rows, int page, CancellationToken ct)
    {
        var listings = new List<MarketListingResponse>(rows.Count);
        foreach (var l in rows)
        {
            var (name, rarity) = Describe(l.Kind, l.DefinitionId);
            // The seller's NAME is shown, never their id: a board that hands out player guids is a
            // targeting list for harassment and for the "I am from support" scam.
            var seller = await _players.FindByIdAsync(l.SellerId, ct);
            listings.Add(new MarketListingResponse
            {
                Id           = l.Id,
                Kind         = l.Kind.ToString(),
                DefinitionId = l.DefinitionId,
                Name         = name,
                Rarity       = rarity,
                Quantity     = l.Quantity,
                UnitPrice    = l.UnitPrice,
                TotalPrice   = l.TotalPrice,
                Status       = l.Status.ToString(),
                SellerName   = DisplayName(seller),
                IsOwnListing = l.SellerId == playerId,
                ListedAt     = l.ListedAt,
                ExpiresAt    = l.ExpiresAt,
            });
        }

        return new MarketBrowseResponse
        {
            Listings       = listings,
            Page           = Math.Max(1, page),
            PageSize       = _config.BrowsePageSize,
            PlayerGold     = gold,
            ListingFeeRate = _config.ListingFeeRate,
            SaleFeeRate    = _config.SaleFeeRate,
        };
    }

    // ───────────────────────────────────────────────────────────────────────────── list

    public Task<CreateListingResponse> CreateListingAsync(
        Guid playerId, CreateListingRequest request, CancellationToken ct = default)
    {
        if (!_config.Enabled)
            return Task.FromResult(FailCreate(MarketFailureCode.MarketDisabled, "The market is closed."));

        return _mutationLock.RunAsync(playerId, () => CreateListingCoreAsync(playerId, request, ct), ct);
    }

    private async Task<CreateListingResponse> CreateListingCoreAsync(
        Guid playerId, CreateListingRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<MarketItemKind>(request.Kind, ignoreCase: true, out var kind))
            return FailCreate(MarketFailureCode.NotTradeable, $"'{request.Kind}' cannot be listed.");

        if (request.Quantity < 1 || request.Quantity > _config.MaxQuantityPerListing)
            return FailCreate(MarketFailureCode.QuantityOutOfRange,
                $"Quantity must be between 1 and {_config.MaxQuantityPerListing:N0}.");

        if (request.UnitPrice < _config.MinUnitPrice || request.UnitPrice > _config.MaxUnitPrice)
            return FailCreate(MarketFailureCode.PriceOutOfRange,
                $"Price must be between {_config.MinUnitPrice:N0} and {_config.MaxUnitPrice:N0} gold.");

        var player = await _players.FindByIdAsync(playerId, ct);
        var gate = GateTrade(player);
        if (gate is not null) return FailCreate(gate.Value.Code, gate.Value.Reason);

        int active = await _listings.CountActiveForSellerAsync(playerId, ct);
        if (active >= _config.MaxActiveListingsPerPlayer)
            return FailCreate(MarketFailureCode.TooManyActiveListings,
                $"You already have {active} listings up. The limit is {_config.MaxActiveListingsPerPlayer}.");

        var (name, _) = Describe(kind, request.DefinitionId);
        if (name.Length == 0)
            return FailCreate(MarketFailureCode.UnknownDefinition, "No such thing.");

        if (!IsTradeable(kind, request.DefinitionId, out var whyNot))
            return FailCreate(MarketFailureCode.NotTradeable, whyNot);

        // Ownership is re-checked HERE, under the lock, and the escrow below is the only thing that
        // acts on it. A check outside the lock would be advisory.
        var hold = await CheckHoldingAsync(playerId, kind, request.DefinitionId, request.Quantity, ct);
        if (hold is not null) return FailCreate(hold.Value.Code, hold.Value.Reason);

        long total = checked(request.UnitPrice * request.Quantity);
        long listingFee = Fee(total, _config.ListingFeeRate);

        // The fee is charged BEFORE the goods are escrowed, so a player who cannot afford to list
        // keeps their stack. Conditional UPDATE: it can never drive gold negative.
        long newGold = player!.Gold;
        if (listingFee > 0)
        {
            var spent = await _players.TrySpendGoldAsync(playerId, listingFee, ct);
            if (spent is null)
                return FailCreate(MarketFailureCode.InsufficientGold,
                    $"The listing fee is {listingFee:N0} gold and you have {player.Gold:N0}.");
            newGold = spent.Value;
        }

        await EscrowAsync(playerId, kind, request.DefinitionId, request.Quantity, ct);

        var listing = MarketListing.Create(
            playerId, kind, request.DefinitionId, request.Quantity, request.UnitPrice,
            DateTimeOffset.UtcNow.AddHours(_config.ListingDurationHours));
        await _listings.CreateAsync(listing, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "MarketListingCreated", null,
            $"Listed {request.Quantity}x {request.DefinitionId} ({kind}) at {request.UnitPrice:N0} each " +
            $"(total {total:N0}), fee {listingFee:N0}. Listing {listing.Id}.", null), ct);

        return new CreateListingResponse
        {
            Success        = true,
            ListingId      = listing.Id,
            ListingFeePaid = listingFee,
            NewPlayerGold  = newGold,
            ExpiresAt      = listing.ExpiresAt,
        };
    }

    // ───────────────────────────────────────────────────────────────────────────── buy

    public Task<BuyListingResponse> BuyAsync(Guid playerId, Guid listingId, CancellationToken ct = default)
    {
        if (!_config.Enabled)
            return Task.FromResult(FailBuy(MarketFailureCode.MarketDisabled, "The market is closed."));

        return _mutationLock.RunAsync(playerId, () => BuyCoreAsync(playerId, listingId, ct), ct);
    }

    private async Task<BuyListingResponse> BuyCoreAsync(Guid playerId, Guid listingId, CancellationToken ct)
    {
        var listing = await _listings.FindByIdAsync(listingId, ct);
        if (listing is null || listing.IsDeleted)
            return FailBuy(MarketFailureCode.NotFound, "That listing is gone.");

        if (listing.Status != MarketListingStatus.Active)
            return FailBuy(MarketFailureCode.NoLongerActive, "Somebody got there first.");

        if (listing.ExpiresAt <= DateTimeOffset.UtcNow)
            return FailBuy(MarketFailureCode.NoLongerActive, "That listing has lapsed.");

        if (listing.SellerId == playerId)
            return FailBuy(MarketFailureCode.CannotBuyOwnListing, "That is your own listing.");

        var buyer = await _players.FindByIdAsync(playerId, ct);
        var gate = GateTrade(buyer);
        if (gate is not null) return FailBuy(gate.Value.Code, gate.Value.Reason);

        long total = listing.TotalPrice;
        var since = DateTimeOffset.UtcNow.AddHours(-24);

        // Both caps are read from the append-only ledger, which records gold that actually moved
        // rather than gold that was asked for. Checked BEFORE the latch so a capped player never
        // takes a listing off the board.
        long spentToday = await _ledger.SumSpendSinceAsync(playerId, since, ct);
        if (spentToday + total > _config.MaxGoldSpentPerDay)
            return FailBuy(MarketFailureCode.DailySpendCapReached,
                $"That would put you over the {_config.MaxGoldSpentPerDay:N0} gold you may spend in a day.");

        long fee = Fee(total, _config.SaleFeeRate);
        long proceeds = total - fee;

        long receivedToday = await _ledger.SumProceedsSinceAsync(listing.SellerId, since, ct);
        if (receivedToday + proceeds > _config.MaxGoldReceivedPerDay)
            return FailBuy(MarketFailureCode.DailyGoldReceivedCapReached,
                "The seller has taken in as much as they may today. Try again tomorrow.");

        // THE LATCH, and nothing above it has moved anything. Exactly one concurrent buyer wins; the
        // loser matches no row. Everything below runs in the same transaction, so any later failure
        // rolls this back and the listing returns to the board.
        if (!await _listings.TryClaimForSaleAsync(listingId, playerId, total, fee, ct))
            return FailBuy(MarketFailureCode.NoLongerActive, "Somebody got there first.");

        var paid = await _players.TrySpendGoldAsync(playerId, total, ct);
        if (paid is null)
            return FailBuy(MarketFailureCode.InsufficientGold,
                $"That costs {total:N0} gold and you have {buyer!.Gold:N0}.");

        // Atomic increment, not a read-modify-write: this runs under the BUYER's lock, so the seller
        // is not serialised against their own concurrent activity and an EF-tracked add would lose
        // gold under load.
        await _players.AddGoldAsync(listing.SellerId, proceeds, ct);

        // The goods come off the LISTING, not out of the seller's inventory — they left it when the
        // listing was created, so there is nothing here that can fail for lack of stock.
        await DeliverAsync(playerId, listing.Kind, listing.DefinitionId, listing.Quantity, ct);

        await _ledger.AppendAsync(MarketTransaction.Create(
            listing.Id, listing.SellerId, playerId, listing.Kind, listing.DefinitionId,
            listing.Quantity, listing.UnitPrice, total, fee,
            Fee(total, _config.ListingFeeRate), proceeds));

        var (name, _) = Describe(listing.Kind, listing.DefinitionId);
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "MarketPurchase", null,
            $"Bought {listing.Quantity}x {listing.DefinitionId} for {total:N0} gold from {listing.SellerId} " +
            $"(fee {fee:N0}, seller received {proceeds:N0}). Listing {listing.Id}.", null), ct);

        return new BuyListingResponse
        {
            Success       = true,
            Kind          = listing.Kind.ToString(),
            DefinitionId  = listing.DefinitionId,
            Name          = name,
            Quantity      = listing.Quantity,
            GoldSpent     = total,
            NewPlayerGold = paid.Value,
        };
    }

    // ───────────────────────────────────────────────────────────────────────────── cancel

    public Task<CancelListingResponse> CancelAsync(
        Guid playerId, Guid listingId, CancellationToken ct = default)
        // No Enabled gate: a market that is switched off must still hand back what it is holding.
        => _mutationLock.RunAsync(playerId, () => CancelCoreAsync(playerId, listingId, ct), ct);

    private async Task<CancelListingResponse> CancelCoreAsync(
        Guid playerId, Guid listingId, CancellationToken ct)
    {
        var listing = await _listings.FindByIdAsync(listingId, ct);
        if (listing is null || listing.IsDeleted)
            return FailCancel(MarketFailureCode.NotFound, "No such listing.");

        // Ownership is checked here for the message and enforced again inside the latch, because the
        // read and the write are not the same instant.
        if (listing.SellerId != playerId)
            return FailCancel(MarketFailureCode.NotYours, "That is not your listing.");

        if (!await _listings.TryCancelAsync(listingId, playerId, ct))
            return FailCancel(MarketFailureCode.NoLongerActive,
                "Too late — that listing has already sold or lapsed.");

        await DeliverAsync(playerId, listing.Kind, listing.DefinitionId, listing.Quantity, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "MarketListingCancelled", null,
            $"Withdrew {listing.Quantity}x {listing.DefinitionId}. Listing {listing.Id}. " +
            "Listing fee not refunded.", null), ct);

        return new CancelListingResponse
        {
            Success            = true,
            DefinitionId       = listing.DefinitionId,
            QuantityReturned   = listing.Quantity,
            ListingFeeRefunded = 0,
        };
    }

    // ───────────────────────────────────────────────────────────────────────────── expiry

    public async Task<int> SettleExpiredListingsAsync(int max = 50, CancellationToken ct = default)
    {
        var due = await _listings.GetExpiredActiveAsync(DateTimeOffset.UtcNow, max, ct);
        int settled = 0;
        foreach (var listing in due)
        {
            if (ct.IsCancellationRequested) break;
            // Each listing settles under ITS SELLER's lock and its own transaction, so one seller's
            // problem cannot strand the rest of the backlog, and the goods go back into an inventory
            // row nobody else is writing at the same moment.
            var ok = await _mutationLock.RunAsync(listing.SellerId, async () =>
            {
                if (!await _listings.TryExpireAsync(listing.Id, ct)) return false;
                await DeliverAsync(listing.SellerId, listing.Kind, listing.DefinitionId, listing.Quantity, ct);
                await _auditLog.AppendAsync(AuditLog.Create(
                    listing.SellerId, "MarketListingExpired", null,
                    $"Listing {listing.Id} lapsed; returned {listing.Quantity}x {listing.DefinitionId}.",
                    null), ct);
                return true;
            }, ct);
            if (ok) settled++;
        }
        return settled;
    }

    // ───────────────────────────────────────────────────────────────────────────── helpers

    /// <summary>
    /// The gates that apply to BOTH sides of every trade. Kept in one place so a rule cannot be
    /// enforced on listing and forgotten on buying, which is how anti-mule measures usually rot.
    /// </summary>
    private (MarketFailureCode Code, string Reason)? GateTrade(Player? player)
    {
        if (player is null || player.IsDeleted)
            return (MarketFailureCode.NotFound, "No such player.");

        if (player.IsBanned || player.IsMuted)
            return (MarketFailureCode.TradingRestricted, "Your account cannot trade right now.");

        if (player.Level < _config.MinLevelToTrade)
            return (MarketFailureCode.LevelTooLow,
                $"The market opens at level {_config.MinLevelToTrade}.");

        var age = DateTimeOffset.UtcNow - player.CreatedAt;
        if (age < TimeSpan.FromHours(_config.MinAccountAgeHours))
            return (MarketFailureCode.AccountTooNew,
                $"The market opens to an account after {_config.MinAccountAgeHours} hours.");

        return null;
    }

    private bool IsTradeable(MarketItemKind kind, string definitionId, out string whyNot)
    {
        whyNot = string.Empty;

        if (_config.Untradeable.Contains(definitionId, StringComparer.Ordinal))
        {
            whyNot = "That cannot be sold.";
            return false;
        }

        if (kind == MarketItemKind.Gear)
        {
            if (!_config.GearTradeable) { whyNot = "Gear cannot be sold."; return false; }
            return true;
        }

        var def = _itemDefs.GetById(definitionId);
        if (def is null) { whyNot = "No such item."; return false; }

        if (!_config.TradeableItemTypes.Contains(def.Type.ToString(), StringComparer.Ordinal))
        {
            whyNot = $"{def.Type} cannot be sold.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Confirms the seller can part with the stack. For gear this counts EQUIPPED copies rather than
    /// treating equipped as a flag, so a player with two of something may sell the spare — the same
    /// rule crafting uses, and for the same reason: a flag would either block a legitimate sale or
    /// let someone sell the shirt off their own back.
    /// </summary>
    private async Task<(MarketFailureCode Code, string Reason)?> CheckHoldingAsync(
        Guid playerId, MarketItemKind kind, string definitionId, int quantity, CancellationToken ct)
    {
        if (kind == MarketItemKind.Item)
        {
            var row = await _inventory.GetAsync(playerId, definitionId, ct);
            int owned = row?.Quantity ?? 0;
            return owned >= quantity
                ? null
                : (MarketFailureCode.InsufficientQuantity, $"You hold {owned}, not {quantity}.");
        }

        var gearRow = await _gear.GetAsync(playerId, definitionId, ct);
        int held = (gearRow is null || gearRow.IsDeleted) ? 0 : gearRow.Quantity;
        if (held < quantity)
            return (MarketFailureCode.InsufficientQuantity, $"You hold {held}, not {quantity}.");

        int worn = (await _equipped.GetEquippedAsync(playerId, ct))
            .Count(e => string.Equals(e.GearDefinitionId, definitionId, StringComparison.Ordinal));
        if (worn > 0 && held - quantity < worn)
            return (MarketFailureCode.ItemInUse,
                $"You are wearing {worn} of your {held}. Unequip a copy or sell fewer.");

        return null;
    }

    /// <summary>Takes the stack OUT of the seller's holdings and into the listing's keeping.</summary>
    private async Task EscrowAsync(
        Guid playerId, MarketItemKind kind, string definitionId, int quantity, CancellationToken ct)
    {
        if (kind == MarketItemKind.Item)
        {
            var row = await _inventory.GetAsync(playerId, definitionId, ct)
                ?? throw new InvalidOperationException($"Market escrow: no inventory row for '{definitionId}'.");
            if (row.Quantity < quantity)
                throw new InvalidOperationException(
                    $"Market escrow: only {row.Quantity}x '{definitionId}' held, needed {quantity}.");
            row.ConsumeQuantity(quantity);
            await _inventory.UpdateAsync(row, ct);
            return;
        }

        var gearRow = await _gear.GetAsync(playerId, definitionId, ct)
            ?? throw new InvalidOperationException($"Market escrow: no gear row for '{definitionId}'.");
        gearRow.ConsumeQuantity(quantity);   // throws on a short stack
        await _gear.UpdateAsync(gearRow, ct);
    }

    /// <summary>Hands the stack to a player — the buyer on a sale, the seller on a cancel or expiry.</summary>
    private async Task DeliverAsync(
        Guid playerId, MarketItemKind kind, string definitionId, int quantity, CancellationToken ct)
    {
        if (kind == MarketItemKind.Item)
        {
            var row = await _inventory.GetAsync(playerId, definitionId, ct);
            if (row is null)
                await _inventory.CreateAsync(PlayerInventoryItem.Create(playerId, definitionId, quantity), ct);
            else
            {
                row.AddQuantity(quantity);
                await _inventory.UpdateAsync(row, ct);
            }
            return;
        }

        var gearRow = await _gear.GetAsync(playerId, definitionId, ct);
        if (gearRow is null)
            await _gear.CreateAsync(PlayerGear.Create(playerId, definitionId, quantity), ct);
        else
        {
            gearRow.AddQuantity(quantity);
            await _gear.UpdateAsync(gearRow, ct);
        }
    }

    /// <summary>
    /// Fees round UP. A fee that rounds down is a fee a player can avoid entirely by listing small
    /// enough, and a thousand fee-free listings is exactly the spam the listing fee exists to price.
    /// </summary>
    private static long Fee(long amount, double rate)
    {
        if (rate <= 0 || amount <= 0) return 0;
        return (long)Math.Ceiling(amount * rate);
    }

    private (string Name, string Rarity) Describe(MarketItemKind kind, string definitionId)
    {
        if (kind == MarketItemKind.Gear)
        {
            var g = _gearDefs.GetById(definitionId);
            return g is null ? (string.Empty, string.Empty) : (g.Name, g.Rarity.ToString());
        }
        var i = _itemDefs.GetById(definitionId);
        return i is null ? (string.Empty, string.Empty) : (i.Name, i.Rarity.ToString());
    }

    private static string DisplayName(Player? p)
        => p is null ? "unknown"
         : !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName
         : p.Username;

    private static CreateListingResponse FailCreate(MarketFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };

    private static BuyListingResponse FailBuy(MarketFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };

    private static CancelListingResponse FailCancel(MarketFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
