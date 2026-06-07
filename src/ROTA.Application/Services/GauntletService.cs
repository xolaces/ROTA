using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
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

    public GauntletService(
        IGauntletEventRepository events,
        IGauntletEntryRepository entries,
        IStrikeRepository strikes,
        IGauntletCurrencyRepository currency,
        IGauntletContentProvider content,
        IPlayerRepository players,
        IGemService gems,
        IAuditLogRepository auditLog,
        IOptions<GauntletConfig> config)
    {
        _events   = events;
        _entries  = entries;
        _strikes  = strikes;
        _currency = currency;
        _content  = content;
        _players  = players;
        _gems     = gems;
        _auditLog = auditLog;
        _config   = config.Value;
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
}
