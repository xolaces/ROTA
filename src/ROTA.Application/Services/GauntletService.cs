using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA (System 16 Slice 2) — player-facing Gauntlet service: current-event read, join (league
// locked + idempotent), and gem→strikes purchase (idempotent with lost-purchase recovery).
public sealed class GauntletService : IGauntletService
{
    private readonly IGauntletEventRepository _events;
    private readonly IGauntletEntryRepository _entries;
    private readonly IStrikeRepository _strikes;
    private readonly IGauntletCurrencyRepository _currency;
    private readonly IGauntletContentProvider _content;
    private readonly IPlayerRepository _players;
    private readonly IGemService _gems;
    private readonly IAuditLogRepository _auditLog;
    private readonly GauntletConfig _config;
    // Slice 6 — token shop
    private readonly IGauntletShopProvider _shop;
    private readonly ILegionService _legions;
    private readonly IEquipmentService _equipment;

    public GauntletService(
        IGauntletEventRepository events,
        IGauntletEntryRepository entries,
        IStrikeRepository strikes,
        IGauntletCurrencyRepository currency,
        IGauntletContentProvider content,
        IPlayerRepository players,
        IGemService gems,
        IAuditLogRepository auditLog,
        IOptions<GauntletConfig> config,
        IGauntletShopProvider shop,
        ILegionService legions,
        IEquipmentService equipment)
    {
        _events    = events;
        _entries   = entries;
        _strikes   = strikes;
        _currency  = currency;
        _content   = content;
        _players   = players;
        _gems      = gems;
        _auditLog  = auditLog;
        _config    = config.Value;
        _shop      = shop;
        _legions   = legions;
        _equipment = equipment;
    }

    public async Task<GauntletEventResponse?> GetCurrentEventAsync(CancellationToken ct = default)
    {
        var active = await _events.GetActiveAsync(ct);
        return active is null ? null : MapEvent(active);
    }

    public async Task<JoinGauntletResult> JoinEventAsync(Guid playerId, CancellationToken ct = default)
    {
        var active = await _events.GetActiveAsync(ct);
        if (active is null)
            return JoinGauntletResult.Fail("There is no active Gauntlet event.");

        // Idempotent: if the player already has an entry for this event, return it WITHOUT
        // re-creating or re-evaluating their league (locked for the cycle).
        var existing = await _entries.FindByEventAndPlayerAsync(active.Id, playerId, ct);
        if (existing is not null)
            return JoinGauntletResult.Ok(MapEntry(existing));

        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null || player.IsDeleted)
            return JoinGauntletResult.Fail("Player not found.");
        if (player.IsBanned)
            return JoinGauntletResult.Fail("Banned players cannot join the Gauntlet.");
        if (player.Level < _config.MinEntryLevel)
            return JoinGauntletResult.Fail(
                $"You must be at least level {_config.MinEntryLevel} to enter the Gauntlet.");

        // League is locked at this moment from the player's current level.
        var league = _content.ResolveLeague(player.Level);
        var created = await _entries.UpsertAsync(
            GauntletEntry.Create(active.Id, playerId, league), ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "GauntletJoin", null,
            $"Joined event {active.Id} in league {created.League} at level {player.Level}.", null), ct);

        return JoinGauntletResult.Ok(MapEntry(created));
    }

    public async Task<GauntletEntryResponse?> GetMyEntryAsync(
        Guid playerId, Guid gauntletEventId, CancellationToken ct = default)
    {
        var entry = await _entries.FindByEventAndPlayerAsync(gauntletEventId, playerId, ct);
        return entry is null ? null : MapEntry(entry);
    }

    public async Task<BuyStrikesResult> BuyStrikesAsync(
        Guid playerId, int strikes, string idempotencyKey, CancellationToken ct = default)
    {
        if (strikes <= 0)
            return BuyStrikesResult.Fail("Strikes to buy must be greater than zero.", 0);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BuyStrikesResult.Fail("An idempotency key is required.", 0);

        var cost = checked(strikes * _config.StrikeGemPrice);

        // Client-idempotency-key design (refines the spec's strikebuy:{playerId}:{gemTransactionId}):
        // the SAME referenceId threads the gem spend AND the strike credit, so a retry re-runs both
        // idempotently — the gem ledger returns AlreadyProcessed and the strike credit is guarded by
        // the strike ledger's unique index. Mirrors the magic/unit-buy lost-purchase recovery.
        var referenceId = $"strikebuy:{playerId}:{idempotencyKey}";

        var spend = await _gems.SpendGemsAsync(
            playerId, cost, GemTransactionType.GauntletStrikePurchase, referenceId, ct);

        if (spend == GemSpendOutcome.InsufficientBalance)
            return BuyStrikesResult.Fail($"Insufficient gems. Required: {cost}.", cost);

        // spend is Charged or AlreadyProcessed → credit strikes idempotently with the SAME reference.
        // ReferenceExists guards against double-credit on replay (the gem charge committed but the
        // strike credit may or may not have on a previous crashed attempt).
        if (!await _strikes.ReferenceExistsAsync(
                playerId, StrikeTransactionType.GemPurchase, referenceId, ct))
        {
            await _strikes.CreateAsync(StrikeTransaction.Create(
                playerId, strikes, StrikeTransactionType.GemPurchase, referenceId), ct);
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "GauntletStrikeBuy", null,
            $"Bought {strikes} strikes for {cost} gems (ref={referenceId}, gem={spend}).", null), ct);

        var balance = await _strikes.GetBalanceAsync(playerId, ct);
        return BuyStrikesResult.Ok(cost, balance);
    }

    // ── Slice 6 — token shop ────────────────────────────────────────────────

    public async Task<GauntletShopResponse> GetShopAsync(Guid playerId, CancellationToken ct = default)
    {
        var entries = _shop.GetAll();

        // Own-once ownership is read once per kind so the catalogue can flag AlreadyOwned without a
        // per-entry round trip. Repeatable kinds (GemBundle/StrikeRefill) are never "owned".
        var hydrated = new List<GauntletShopEntryResponse>(entries.Count);
        if (entries.Count > 0)
        {
            var ownedUnits   = (await _legions.GetOwnedUnitsAsync(playerId, ct)).Select(u => u.UnitDefinitionId).ToHashSet(StringComparer.Ordinal);
            var ownedLegions = (await _legions.GetOwnedLegionsAsync(playerId, ct)).Select(l => l.LegionDefinitionId).ToHashSet(StringComparer.Ordinal);
            var ownedGear    = (await _equipment.GetOwnedGearAsync(playerId, ct)).Select(g => g.GearDefinitionId).ToHashSet(StringComparer.Ordinal);

            foreach (var e in entries)
            {
                hydrated.Add(MapShopEntry(e, IsOwned(e, ownedUnits, ownedLegions, ownedGear)));
            }
        }

        var tokenBalance     = await _currency.GetBalanceAsync(playerId, GauntletCurrency.Token, ct);
        var pitchforkBalance = await _currency.GetBalanceAsync(playerId, GauntletCurrency.Pitchfork, ct);

        return new GauntletShopResponse
        {
            Entries          = hydrated,
            TokenBalance     = tokenBalance,
            PitchforkBalance = pitchforkBalance,
        };
    }

    public async Task<BuyShopResult> BuyFromShopAsync(
        Guid playerId, string shopEntryId, CancellationToken ct = default)
    {
        // 1. Unknown id → NotFound-style failure (no charge, no grant).
        var entry = _shop.GetById(shopEntryId);
        if (entry is null)
            return BuyShopResult.Fail($"Shop entry '{shopEntryId}' not found.");

        // 2. Own-once kinds (Unit/Legion/Gear, maxOwned:1): ownership pre-check FIRST — if already
        //    owned, return AlreadyOwned WITHOUT charging or granting (mirrors BuyUnit/BuyMagic).
        if (entry.IsOwnOnce && await IsPayloadOwnedAsync(playerId, entry, ct))
            return BuyShopResult.OwnedAlready();

        // 3. Spend from the entry's currency with the tri-state result. The SAME referenceId threads
        //    the spend and the (idempotent) grant, so a retry recovers a lost purchase without
        //    double-charging. Token vs Pitchfork is isolated by passing entry.Currency: a
        //    Pitchfork-priced entry debits the Pitchfork balance, so a caller with only Tokens gets
        //    Insufficient (wrong-currency = insufficient in THAT currency).
        //
        // PHASE-2: wrap SpendAsync + the grant in one DB transaction (see LegionService.BuyUnitAsync).
        // The AlreadyCharged recovery path already makes a mid-air crash recoverable; atomicity is a
        // hardening step, not a correctness fix.
        var referenceId = $"gauntletshop:{playerId}:{entry.Id}";
        var outcome = await _currency.SpendAsync(playerId, entry.Currency, entry.Price, referenceId, ct);

        if (outcome == GauntletCurrencySpendOutcome.Insufficient)
            return BuyShopResult.Insufficient();

        // outcome is Charged OR AlreadyCharged → grant idempotently. AlreadyCharged means the spend
        // row already existed (lost-purchase replay): re-grant, never re-charge.
        bool alreadyCharged = outcome == GauntletCurrencySpendOutcome.AlreadyCharged;

        await GrantPayloadAsync(playerId, entry, referenceId, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "GauntletShopBuy", null,
            $"Bought shop entry {entry.Id} ({entry.RewardKind}, payload={entry.PayloadId}, " +
            $"amount={entry.Amount}) for {entry.Price} {entry.Currency} " +
            $"(ref={referenceId}, spend={outcome}).", null), ct);

        return BuyShopResult.Ok(alreadyCharged);
    }

    // Dispatches the grant on rewardKind. Every branch is idempotent so the AlreadyCharged (lost-
    // purchase) path can re-run safely without double-granting:
    //   Unit/Legion → ILegionService.Grant* (idempotent own-once upsert).
    //   Gear        → guarded by the step-2 ownership pre-check (only reached when NOT owned), so
    //                 GrantGearAsync(..,1) grants exactly once across the happy + replay paths.
    //   GemBundle   → IGemService.GrantGemsAsync — idempotent via the gem ledger's unique index on
    //                 the referenceId.
    //   StrikeRefill→ IStrikeRepository.CreateAsync guarded by ReferenceExistsAsync.
    private async Task GrantPayloadAsync(
        Guid playerId, GauntletShopEntry entry, string referenceId, CancellationToken ct)
    {
        switch (entry.RewardKind)
        {
            case GauntletShopRewardKind.Unit:
                await _legions.GrantUnitAsync(playerId, entry.PayloadId, ct);
                break;

            case GauntletShopRewardKind.Legion:
                await _legions.GrantLegionAsync(playerId, entry.PayloadId, ct);
                break;

            case GauntletShopRewardKind.Gear:
                await _equipment.GrantGearAsync(playerId, entry.PayloadId, 1, ct);
                break;

            case GauntletShopRewardKind.GemBundle:
                await _gems.GrantGemsAsync(
                    playerId, entry.Amount, GemTransactionType.GauntletShopReward, referenceId, ct);
                break;

            case GauntletShopRewardKind.StrikeRefill:
                if (!await _strikes.ReferenceExistsAsync(
                        playerId, StrikeTransactionType.ShopRefill, referenceId, ct))
                {
                    await _strikes.CreateAsync(StrikeTransaction.Create(
                        playerId, entry.Amount, StrikeTransactionType.ShopRefill, referenceId), ct);
                }
                break;
        }
    }

    // Ownership pre-check for an own-once entry (single round trip on the matching collection).
    private async Task<bool> IsPayloadOwnedAsync(
        Guid playerId, GauntletShopEntry entry, CancellationToken ct)
        => entry.RewardKind switch
        {
            GauntletShopRewardKind.Unit =>
                (await _legions.GetOwnedUnitsAsync(playerId, ct)).Any(u => u.UnitDefinitionId == entry.PayloadId),
            GauntletShopRewardKind.Legion =>
                (await _legions.GetOwnedLegionsAsync(playerId, ct)).Any(l => l.LegionDefinitionId == entry.PayloadId),
            GauntletShopRewardKind.Gear =>
                (await _equipment.GetOwnedGearAsync(playerId, ct)).Any(g => g.GearDefinitionId == entry.PayloadId),
            _ => false,
        };

    private static bool IsOwned(
        GauntletShopEntry e,
        HashSet<string> ownedUnits, HashSet<string> ownedLegions, HashSet<string> ownedGear)
        => e.IsOwnOnce && e.RewardKind switch
        {
            GauntletShopRewardKind.Unit   => ownedUnits.Contains(e.PayloadId),
            GauntletShopRewardKind.Legion => ownedLegions.Contains(e.PayloadId),
            GauntletShopRewardKind.Gear   => ownedGear.Contains(e.PayloadId),
            _ => false,
        };

    // ── Mapping ──────────────────────────────────────────────────────────────

    internal static GauntletEventResponse MapEvent(GauntletEvent e)
        => new()
        {
            Id        = e.Id,
            Name      = e.Name,
            State     = e.State.ToString(),
            StartsAt  = e.StartsAt,
            EndsAt    = e.EndsAt,
            SettledAt = e.SettledAt,
        };

    internal static GauntletEntryResponse MapEntry(GauntletEntry e)
        => new()
        {
            Id              = e.Id,
            GauntletEventId = e.GauntletEventId,
            PlayerId        = e.PlayerId,
            League          = e.League.ToString(),
            Score           = e.Score,
            TieBreakAt      = e.TieBreakAt,
            LastRank        = e.LastRank,
        };

    internal static GauntletShopEntryResponse MapShopEntry(GauntletShopEntry e, bool alreadyOwned)
        => new()
        {
            Id           = e.Id,
            RewardKind   = e.RewardKind.ToString(),
            PayloadId    = e.PayloadId,
            Amount       = e.Amount,
            Currency     = e.Currency.ToString(),
            Price        = e.Price,
            MaxOwned     = e.MaxOwned,
            AlreadyOwned = alreadyOwned,
        };
}
