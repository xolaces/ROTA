using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA (System 16 Slice 2 + 5) — admin lifecycle for Gauntlet events. Enforces ≤1 Active on open;
// guards illegal state transitions; every action is audited. The actor check is done at the
// controller (DB re-verify) / CLI bypass layer, matching AdminService's convention.
//
// BETA (System 16 Slice 5) — SettleEventAsync now distributes the rank-band prizes. Idempotency is
// the whole point (the magic/gem money-bug class): every grant is keyed by a unique referenceId
// (the currency ledger's unique partial index + a ReferenceExistsAsync pre-check) or a unique
// ownership index (trophy upsert, honor grant), and the event is only marked Settled after all
// grants commit — so a re-triggered/retried/re-run settle NEVER double-pays tokens, pitchfork, or
// trophies. An already-Settled event short-circuits to a zero-count no-op.
public sealed class GauntletAdminService : IGauntletAdminService
{
    private readonly IGauntletEventRepository _events;
    private readonly IGauntletScoringService _scoring;
    private readonly IGauntletEntryRepository _entries;
    private readonly IGauntletContentProvider _content;
    private readonly IGauntletCurrencyRepository _currency;
    private readonly IPlayerGauntletTrophyRepository _trophies;
    private readonly IPlayerEventMagicRepository _eventMagics;
    private readonly IPlayerMagicHonorRepository _magicHonors;
    private readonly IAuditLogRepository _auditLog;
    private readonly GauntletConfig _config;

    public GauntletAdminService(
        IGauntletEventRepository events,
        IGauntletScoringService scoring,
        IGauntletEntryRepository entries,
        IGauntletContentProvider content,
        IGauntletCurrencyRepository currency,
        IPlayerGauntletTrophyRepository trophies,
        IPlayerEventMagicRepository eventMagics,
        IPlayerMagicHonorRepository magicHonors,
        IAuditLogRepository auditLog,
        IOptions<GauntletConfig> config)
    {
        _events      = events;
        _scoring     = scoring;
        _entries     = entries;
        _content     = content;
        _currency    = currency;
        _trophies    = trophies;
        _eventMagics = eventMagics;
        _magicHonors = magicHonors;
        _auditLog    = auditLog;
        _config      = config.Value;
    }

    public async Task<GauntletEventActionResult> OpenEventAsync(
        string name, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return GauntletEventActionResult.Fail("Event name is required.");
        if (endsAt <= startsAt)
            return GauntletEventActionResult.Fail("endsAt must be after startsAt.");

        // ≤1 Active guard (service-level; the repo read is the source of truth).
        var active = await _events.GetActiveAsync(ct);
        if (active is not null)
            return GauntletEventActionResult.Fail(
                $"An active Gauntlet event already exists ({active.Id}). Close it before opening another.");

        var ev = GauntletEvent.Create(name, startsAt, endsAt);
        ev.Activate();
        await _events.CreateAsync(ev, ct);

        // System 16 Slice 7 — cross-event rank-magic consumable hand-off (the deferred "spec step 2e").
        // It belongs HERE (at open), not at settle: per-event consumables are scoped to the NEXT event,
        // but auto-settle-on-close runs BEFORE the next event exists. Now that the new event (ev) is
        // created, grant the most-recently-settled event's rank winners their consumable for ev — so a
        // prior rank-1 holder is a CURRENT Wrath owner (×1.25) and ranks 2–10 are CURRENT Blessing
        // owners (×1.10/×1.25 honor logic in Slice 4 combat reads PlayerEventMagic for the active event).
        int handedOff = await HandOffRankMagicsAsync(ev.Id, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            null, "GauntletEventOpen", null,
            $"Opened event {ev.Id} '{ev.Name}' [{ev.StartsAt:O}..{ev.EndsAt:O}]. " +
            $"Rank-magic consumables handed off to {handedOff} prior winner(s).", null), ct);

        return GauntletEventActionResult.Ok(GauntletService.MapEvent(ev));
    }

    // Grants the most-recently-settled event's rank winners their per-event consumable, scoped to the
    // NEW event (newEventId). For each settled-event entry with a snapshot rank (LastRank) whose prize
    // band carries a MagicId, grant PlayerEventMagic(player, newEventId, magicId). Idempotent: a
    // FindAsync pre-check skips an existing active grant, and PlayerEventMagicRepository.GrantAsync is
    // itself idempotent (insert-if-absent / restore-if-soft-deleted / no-op-if-active), so a re-opened
    // (or retried) open never double-grants. Returns the number of grants written this call.
    private async Task<int> HandOffRankMagicsAsync(Guid newEventId, CancellationToken ct)
    {
        var prior = await _events.GetMostRecentSettledAsync(ct);
        if (prior is null)
            return 0;   // first-ever event → nobody to hand off to

        var entries = await _entries.GetForEventAsync(prior.Id, ct);
        int granted = 0;

        foreach (var entry in entries)
        {
            if (entry.LastRank is null)
                continue;   // unranked prior entries earned no rank magic

            var band = _content.GetBandForRank(entry.LastRank.Value);
            if (band?.MagicId is null)
                continue;   // band carries no rank magic (only ranks 1 and 2–10 do)

            // Skip if the player already holds this consumable for the new event (idempotent recovery).
            if (await _eventMagics.FindAsync(entry.PlayerId, newEventId, band.MagicId, ct) is not null)
                continue;

            await _eventMagics.GrantAsync(entry.PlayerId, newEventId, band.MagicId, ct);
            granted++;
        }

        return granted;
    }

    public async Task<GauntletEventActionResult> CloseEventAsync(
        Guid eventId, CancellationToken ct = default)
    {
        var ev = await _events.FindByIdAsync(eventId, ct);
        if (ev is null)
            return GauntletEventActionResult.Fail("Gauntlet event not found.");

        if (ev.State != GauntletEventState.Active)
            return GauntletEventActionResult.Fail(
                $"Cannot close an event in state {ev.State}; must be Active.");

        ev.Close();
        await _events.UpdateAsync(ev, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            null, "GauntletEventClose", null,
            $"Closed event {ev.Id} '{ev.Name}'.", null), ct);

        return GauntletEventActionResult.Ok(GauntletService.MapEvent(ev));
    }

    public async Task<GauntletEventActionResult> SettleEventAsync(
        Guid eventId, CancellationToken ct = default)
    {
        var ev = await _events.FindByIdAsync(eventId, ct);
        if (ev is null)
            return GauntletEventActionResult.Fail("Gauntlet event not found.");

        // Idempotent fast-path: already Settled → no-op, return the summary with ZERO counts (nothing
        // new is paid). A re-settle must never throw or double-pay (the money-bug guard).
        if (ev.State == GauntletEventState.Settled)
            return GauntletEventActionResult.Ok(
                GauntletService.MapEvent(ev), new GauntletSettlementSummaryResponse());

        if (ev.State != GauntletEventState.Closed)
            return GauntletEventActionResult.Fail(
                $"Cannot settle an event in state {ev.State}; must be Closed.");

        // 1. Snapshot ranks (idempotent — re-running over an unchanged board yields identical ranks).
        await _scoring.RecomputeRanksAsync(eventId, ct);

        // 2. Distribute the per-rank-band prizes. Ranks are per-league (Slice 3's ROW_NUMBER
        //    PARTITION BY league), so iterating ALL entries naturally pays every league's rank-1..N
        //    its own band — the per-entry LastRank → band mapping handles league separation.
        var entries = await _entries.GetForEventAsync(eventId, ct);

        int  ranksSettled     = 0;
        long tokensGranted    = 0;
        long pitchforkGranted = 0;
        int  trophiesGranted  = 0;

        foreach (var entry in entries)
        {
            // Only ranked entries inside the prize ceiling are paid. Unranked (LastRank == null) or
            // beyond PrizeRankCount → no prize.
            if (entry.LastRank is null || entry.LastRank.Value > _config.PrizeRankCount)
                continue;

            var band = _content.GetBandForRank(entry.LastRank.Value);
            if (band is null)
                continue;   // rank not covered by any band → nothing to grant

            ranksSettled++;

            // (a) Tokens — idempotent via referenceId + the currency ledger's unique partial index.
            //     ReferenceExistsAsync is the pre-check; the unique index is the hard backstop.
            if (band.Tokens > 0)
            {
                var tokenRef = $"gauntletsettle:{eventId}:{entry.PlayerId}:tokens";
                if (!await _currency.ReferenceExistsAsync(
                        entry.PlayerId, GauntletCurrency.Token,
                        GauntletCurrencyTransactionType.RankReward, tokenRef, ct))
                {
                    await _currency.CreateAsync(GauntletCurrencyTransaction.Create(
                        entry.PlayerId, GauntletCurrency.Token, band.Tokens,
                        GauntletCurrencyTransactionType.RankReward, tokenRef), ct);
                    tokensGranted += band.Tokens;
                }
            }

            // (b) Pitchfork — same idempotency discipline; the index folds currency in, so the
            //     pitchfork referenceId never collides with the token one even with the same type.
            if (band.Pitchfork > 0)
            {
                var pitchforkRef = $"gauntletsettle:{eventId}:{entry.PlayerId}:pitchfork";
                if (!await _currency.ReferenceExistsAsync(
                        entry.PlayerId, GauntletCurrency.Pitchfork,
                        GauntletCurrencyTransactionType.RankReward, pitchforkRef, ct))
                {
                    await _currency.CreateAsync(GauntletCurrencyTransaction.Create(
                        entry.PlayerId, GauntletCurrency.Pitchfork, band.Pitchfork,
                        GauntletCurrencyTransactionType.RankReward, pitchforkRef), ct);
                    pitchforkGranted += band.Pitchfork;
                }
            }

            // (c) Trophy — UpsertAsync is idempotent (one row per (player, trophy); restores a
            //     soft-deleted row; no-op if already owned). Count it as a grant action regardless;
            //     a re-settle hits the already-Settled fast-path above and never reaches here.
            if (band.TrophyId is not null)
            {
                await _trophies.UpsertAsync(entry.PlayerId, band.TrophyId, ct);
                trophiesGranted++;
            }

            // (d) Rank magic (Wrath/Blessing) — DEFERRED. The per-event consumable PlayerEventMagic
            // is scoped to the NEXT event, which does not exist yet under auto-settle-on-close. The
            // cross-event consumable hand-off (spec step 2e) is a deliberate FOLLOW-UP — settle writes
            // only the permanent honor (on revoke, step 3) + tokens + pitchfork + trophies.
            // PHASE-2 / FOLLOW-UP: grant band.MagicId as a PlayerEventMagic on the next active event.
        }

        // 3. Honor-echo write-back. RevokeAllForEventAsync soft-deletes every active PlayerEventMagic
        //    for the closing event and RETURNS the revoked rows, giving us the (playerId, magicId)
        //    pairs directly — no separate query. For each revoked holder, write the permanent
        //    PlayerMagicHonor if absent (HasHonorAsync pre-check; GrantAsync is itself idempotent).
        //    Until the cross-event consumable hand-off (the FOLLOW-UP above) is wired, there are
        //    usually no rows to revoke — this mechanism is correct and inert until consumables exist.
        var revoked = await _eventMagics.RevokeAllForEventAsync(eventId, ct);
        int honorsWritten = 0;
        foreach (var grant in revoked)
        {
            if (!await _magicHonors.HasHonorAsync(grant.PlayerId, grant.MagicDefinitionId, ct))
            {
                await _magicHonors.GrantAsync(grant.PlayerId, grant.MagicDefinitionId, ct);
                honorsWritten++;
            }
        }

        // 4. Mark Settled ONLY after all grants commit.
        ev.MarkSettled();
        await _events.UpdateAsync(ev, ct);

        var summary = new GauntletSettlementSummaryResponse
        {
            RanksSettled     = ranksSettled,
            TokensGranted    = tokensGranted,
            PitchforkGranted = pitchforkGranted,
            TrophiesGranted  = trophiesGranted,
            HonorsWritten    = honorsWritten,
        };

        await _auditLog.AppendAsync(AuditLog.Create(
            null, "GauntletEventSettle", null,
            $"Settled event {ev.Id} '{ev.Name}': ranks={ranksSettled}, tokens={tokensGranted}, " +
            $"pitchfork={pitchforkGranted}, trophies={trophiesGranted}, honors={honorsWritten}.", null), ct);

        return GauntletEventActionResult.Ok(GauntletService.MapEvent(ev), summary);
    }
}
