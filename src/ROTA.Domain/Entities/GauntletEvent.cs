using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

// BETA (System 16 Slice 2) — a fixed-duration Gauntlet event. Admin-triggered lifecycle:
// Scheduled → Active → Closed → Settled. Exactly one event may be Active at a time (enforced
// by the repository + admin service). State transitions are guarded — illegal moves throw.
public class GauntletEvent
{
    // Required by EF Core
    private GauntletEvent() { }

    /// <summary>Creates a new event in the <see cref="GauntletEventState.Scheduled"/> state.</summary>
    public static GauntletEvent Create(string name, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event name is required.", nameof(name));
        if (endsAt <= startsAt)
            throw new ArgumentException("endsAt must be after startsAt.", nameof(endsAt));

        return new GauntletEvent
        {
            Id        = Guid.NewGuid(),
            Name      = name,
            State     = GauntletEventState.Scheduled,
            StartsAt  = startsAt,
            EndsAt    = endsAt,
            SettledAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false,
        };
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public GauntletEventState State { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>UTC instant settlement completed; null until <see cref="MarkSettled"/> runs.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // ── Domain methods (guarded transitions) ──────────────────────────────────

    /// <summary>Scheduled → Active. Throws if not currently Scheduled.</summary>
    public void Activate()
    {
        if (State != GauntletEventState.Scheduled)
            throw new InvalidOperationException(
                $"Cannot activate a Gauntlet event in state {State}; must be Scheduled.");
        State     = GauntletEventState.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Active → Closed. Throws if not currently Active.</summary>
    public void Close()
    {
        if (State != GauntletEventState.Active)
            throw new InvalidOperationException(
                $"Cannot close a Gauntlet event in state {State}; must be Active.");
        State     = GauntletEventState.Closed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Closed → Settled. Throws if not currently Closed. Stamps <see cref="SettledAt"/>.</summary>
    public void MarkSettled()
    {
        if (State != GauntletEventState.Closed)
            throw new InvalidOperationException(
                $"Cannot settle a Gauntlet event in state {State}; must be Closed.");
        State     = GauntletEventState.Settled;
        SettledAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
