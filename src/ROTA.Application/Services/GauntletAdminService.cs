using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA (System 16 Slice 2) — admin lifecycle for Gauntlet events. Enforces ≤1 Active on open;
// guards illegal state transitions; Settle here is a state-only transition (payout = Slice 5) and
// idempotent (no-op if already Settled). Every action is audited. The actor check is done at the
// controller (DB re-verify) / CLI bypass layer, matching AdminService's convention.
public sealed class GauntletAdminService : IGauntletAdminService
{
    private readonly IGauntletEventRepository _events;
    private readonly IAuditLogRepository _auditLog;

    public GauntletAdminService(IGauntletEventRepository events, IAuditLogRepository auditLog)
    {
        _events   = events;
        _auditLog = auditLog;
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

        await _auditLog.AppendAsync(AuditLog.Create(
            null, "GauntletEventOpen", null,
            $"Opened event {ev.Id} '{ev.Name}' [{ev.StartsAt:O}..{ev.EndsAt:O}].", null), ct);

        return GauntletEventActionResult.Ok(GauntletService.MapEvent(ev));
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

        // Idempotent: already Settled → no-op, return the summary (re-settle must never throw or
        // double-anything — Slice 5 will make the payout itself idempotent too).
        if (ev.State == GauntletEventState.Settled)
            return GauntletEventActionResult.Ok(GauntletService.MapEvent(ev));

        if (ev.State != GauntletEventState.Closed)
            return GauntletEventActionResult.Fail(
                $"Cannot settle an event in state {ev.State}; must be Closed.");

        // Slice 5 inserts prize distribution here, before MarkSettled.
        ev.MarkSettled();
        await _events.UpdateAsync(ev, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            null, "GauntletEventSettle", null,
            $"Settled event {ev.Id} '{ev.Name}' (state-only; payout is Slice 5).", null), ct);

        return GauntletEventActionResult.Ok(GauntletService.MapEvent(ev));
    }
}
