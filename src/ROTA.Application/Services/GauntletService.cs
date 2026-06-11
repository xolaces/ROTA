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
    // Slice 7 — ladder summon/auto-advance
    private readonly IActiveRaidRepository _raids;
    private readonly IRaidService _raidService;
    // T76 — magic display names for the prize preview / settlement summary
    private readonly IMagicDefinitionProvider _magics;

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
        IEquipmentService equipment,
        IActiveRaidRepository raids,
        IRaidService raidService,
        IMagicDefinitionProvider magics)
    {
        _events      = events;
        _entries     = entries;
        _strikes     = strikes;
        _currency    = currency;
        _content     = content;
        _players     = players;
        _gems        = gems;
        _auditLog    = auditLog;
        _config      = config.Value;
        _shop        = shop;
        _legions     = legions;
        _equipment   = equipment;
        _raids       = raids;
        _raidService = raidService;
        _magics      = magics;
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

        // T76 — Coming Soon: an opened event with a future StartsAt is visible but not yet playable.
        if (active.StartsAt > DateTimeOffset.UtcNow)
            return JoinGauntletResult.Fail("The Gauntlet has not started yet.");

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

    // BETA (System 16 Slice 7) — the ladder summon / auto-advance. The player never manually summons a
    // Gauntlet raid: the next stage is spawned lazily the first time GetLadder runs after a defeat, so
    // "defeat a stage → the next is ready" reads as auto-advance. Progress is DERIVED from the player's
    // gauntlet ActiveRaids (stage number parsed from RaidDefinitionId) — NO new entity, NO migration.
    public async Task<GauntletLadderResponse> GetLadderAsync(Guid playerId, CancellationToken ct = default)
    {
        int stageCount = _content.GetGauntletRaids().Count;

        var active = await _events.GetActiveAsync(ct);
        if (active is null)
            return new GauntletLadderResponse { NoActiveEvent = true, StageCount = stageCount };

        // T76 — Coming Soon: before StartsAt nothing spawns and nothing is climbable (mirrors the
        // join gate, so the pair can never disagree about whether the event window is open).
        if (active.StartsAt > DateTimeOffset.UtcNow)
            return new GauntletLadderResponse { NotStarted = true, StageCount = stageCount };

        // Must have joined the event (a GauntletEntry) before climbing — joining locks the league and
        // is what makes the player scoreable. Mirrors "you must join to be scored" in the combat hook.
        var entry = await _entries.FindByEventAndPlayerAsync(active.Id, playerId, ct);
        if (entry is null)
            return new GauntletLadderResponse { JoinedRequired = true, StageCount = stageCount };

        // Audit fix — the KNOWN ladder double-spawn race: two concurrent GetLadder calls both saw "no
        // active stage" and both spawned stage N (two raids, double per-defeat rewards). The decide-
        // and-spawn now runs under a per-PLAYER advisory lock (key = playerId; the generic wrapper
        // derives the lock id from any Guid), so concurrent calls serialize and the loser re-queries
        // committed truth and returns the winner's stage instead of spawning a twin.
        var eventId  = active.Id;
        var eventEnd = active.EndsAt;
        ActiveRaid? ladderRaid = null;
        bool complete = false;

        await _raids.AtomicWithAdvisoryLockAsync(playerId, async () =>
        {
            // Tracker was cleared by the wrapper — this read sees every committed spawn.
            var stages = await _raids.GetGauntletStagesForPlayerAsync(playerId, eventId, ct);
            var now = DateTimeOffset.UtcNow;

            // (1) An ACTIVE stage (not defeated, not expired) is the current target — return it as-is,
            //     never re-spawn. A player can hold at most one active stage at a time (we only ever
            //     spawn the next after the prior is defeated); if several match we take the highest.
            ladderRaid = stages
                .Where(r => !r.IsDefeated && r.ExpiresAt > now)
                .OrderByDescending(r => StageNumberOf(r.RaidDefinitionId))
                .FirstOrDefault();
            if (ladderRaid is not null)
                return true;

            // (2) No active stage → auto-advance. nextStage = (highest DEFEATED stage) + 1, or 1.
            int highestDefeated = stages
                .Where(r => r.IsDefeated)
                .Select(r => StageNumberOf(r.RaidDefinitionId))
                .DefaultIfEmpty(0)
                .Max();
            int nextStage = highestDefeated + 1;

            // (3) Past the final stage → the ladder is complete for this event.
            if (nextStage > stageCount)
            {
                complete = true;
                return true;
            }

            // (4) Spawn the next stage: Personal, GauntletEventId-stamped, MaxHp = stage baseHp (NO
            //     difficulty multiplier — Gauntlet has no difficulty; Normal is the enum placeholder).
            //     ExpiresAt = event end so every stage shares the event window. Spawn + audit commit
            //     atomically with the lock.
            var def = _content.GetGauntletRaidByStage(nextStage)
                ?? throw new InvalidOperationException(
                    $"Gauntlet ladder stage {nextStage} is missing from gauntlet_raids.json.");

            var raid = ActiveRaid.Create(
                raidDefinitionId: def.Id,
                summonedByPlayerId: playerId,
                maxHp: def.BaseHp,
                expiresAt: eventEnd,
                difficulty: RaidDifficulty.Normal,
                size: RaidSize.Personal);
            raid.LinkGauntletEvent(eventId);
            await _raids.CreateAsync(raid, ct);

            await _auditLog.AppendAsync(AuditLog.Create(
                playerId, "GauntletLadderSpawn", null,
                $"Spawned Gauntlet ladder stage {nextStage} ('{def.Id}', id={raid.Id}) for event {eventId}. " +
                $"HP={raid.MaxHp}, expires={raid.ExpiresAt:O}.", null), ct);

            ladderRaid = raid;
            return true;
        }, ct);

        if (complete)
            return new GauntletLadderResponse
            {
                Complete     = true,
                CurrentStage = 0,
                StageCount   = stageCount,
            };

        if (ladderRaid is null) // defensive: lock body always sets one outcome
            return new GauntletLadderResponse { NoActiveEvent = true, StageCount = stageCount };

        return await BuildLadderForRaidAsync(ladderRaid, playerId, stageCount, ct);
    }

    // Maps a current/just-spawned ladder ActiveRaid onto the ladder response, reusing the canonical
    // ActiveRaidResponse projection (RaidService.GetRaidByIdAsync). The raid is always active +
    // Personal + summoned by the caller here, so the join-by-id projection returns it (never null).
    private async Task<GauntletLadderResponse> BuildLadderForRaidAsync(
        ActiveRaid raid, Guid playerId, int stageCount, CancellationToken ct)
    {
        var response = await _raidService.GetRaidByIdAsync(raid.Id, playerId, ct);
        return new GauntletLadderResponse
        {
            ActiveRaid   = response,
            CurrentStage = StageNumberOf(raid.RaidDefinitionId),
            StageCount   = stageCount,
        };
    }

    // Parses the 1-based stage number from a "gauntlet_stage_N" definition id. Returns 0 if the id is
    // not a recognised ladder-stage id (defensive — ladder raids always carry this shape).
    private static int StageNumberOf(string raidDefinitionId)
    {
        const string prefix = "gauntlet_stage_";
        if (raidDefinitionId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(raidDefinitionId.AsSpan(prefix.Length), out var n))
            return n;
        return 0;
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

    // ── T76 — prize preview + settlement summary ─────────────────────────────

    public async Task<GauntletPrizeTableResponse> GetPrizeTableAsync(
        GauntletEventKind? kind = null, CancellationToken ct = default)
    {
        // Kind resolution: explicit override → the active event's kind → Neck (the standard run).
        var resolved = kind
            ?? (await _events.GetActiveAsync(ct))?.Kind
            ?? GauntletEventKind.Neck;

        var bands = _content.GetBands(resolved);

        return new GauntletPrizeTableResponse
        {
            Kind  = resolved.ToString(),
            Bands = bands.Select(MapBand).ToList(),
        };
    }

    public async Task<GauntletPlayerSettlementResponse?> GetMyLastSettlementAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var settled = await _events.GetMostRecentSettledAsync(ct);
        if (settled is null)
            return null;

        var entry = await _entries.FindByEventAndPlayerAsync(settled.Id, playerId, ct);
        if (entry is null)
            return null;

        // The prize band the FINAL rank landed in (kind-aware — exactly what settle paid). An
        // unranked entry or a rank beyond every band yields a "placed, won nothing" summary.
        var band = entry.LastRank is int rank
            ? _content.GetBandForRank(rank, settled.Kind)
            : null;

        return new GauntletPlayerSettlementResponse
        {
            EventId      = settled.Id,
            EventName    = settled.Name,
            Kind         = settled.Kind.ToString(),
            RunNumber    = settled.RunNumber,
            SettledAt    = settled.SettledAt,
            League       = entry.League.ToString(),
            FinalRank    = entry.LastRank,
            HighestStage = entry.HighestStage,
            Score        = entry.Score,
            WonPrizes    = band is not null,
            TokensAwarded    = band?.Tokens ?? 0,
            PitchforkAwarded = band?.Pitchfork ?? 0,
            TrophyId   = band?.TrophyId,
            TrophyName = band?.TrophyId is null ? null : _content.GetTrophyById(band.TrophyId)?.Name,
            MagicId    = band?.MagicId,
            MagicName  = band?.MagicId is null ? null : _magics.GetById(band.MagicId)?.Name,
        };
    }

    private GauntletPrizeBandResponse MapBand(GauntletPrizeBand b) => new()
    {
        RankFrom  = b.RankFrom,
        RankTo    = b.RankTo,
        Tokens    = b.Tokens,
        Pitchfork = b.Pitchfork,
        TrophyId   = b.TrophyId,
        TrophyName = b.TrophyId is null ? null : _content.GetTrophyById(b.TrophyId)?.Name,
        MagicId    = b.MagicId,
        MagicName  = b.MagicId is null ? null : _magics.GetById(b.MagicId)?.Name,
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
            // T76 — event identity + server-side countdown.
            Kind      = e.Kind.ToString(),
            RunNumber = e.RunNumber,
            LoreBlurb = e.LoreBlurb,
            BannerKey = e.BannerKey,
            SecondsRemaining = (long)Math.Max(0, (e.EndsAt - DateTimeOffset.UtcNow).TotalSeconds),
            // T76 — non-zero only before the window opens (the Home CTA's Coming Soon state).
            SecondsUntilStart = (long)Math.Max(0, (e.StartsAt - DateTimeOffset.UtcNow).TotalSeconds),
        };

    internal static GauntletEntryResponse MapEntry(GauntletEntry e)
        => new()
        {
            Id              = e.Id,
            GauntletEventId = e.GauntletEventId,
            PlayerId        = e.PlayerId,
            League          = e.League.ToString(),
            Score           = e.Score,
            HighestStage    = e.HighestStage,
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
