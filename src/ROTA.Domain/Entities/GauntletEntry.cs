using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

// BETA (System 16 Slice 2) — a player's standing in a single Gauntlet event. League is locked
// at first join (convergence tier resolved from the player's level at that moment) and is NEVER
// re-evaluated for the cycle. Score/TieBreakAt are wired by Slice 4 (combat integration) and
// Slice 3 (snapshot rank); the domain methods are defined now so the schema is complete.
public class GauntletEntry
{
    // Required by EF Core
    private GauntletEntry() { }

    /// <summary>Creates a fresh entry with Score 0 and TieBreakAt = now (the join instant).</summary>
    public static GauntletEntry Create(Guid gauntletEventId, Guid playerId, GauntletLeague league)
        => new GauntletEntry
        {
            Id              = Guid.NewGuid(),
            GauntletEventId = gauntletEventId,
            PlayerId        = playerId,
            League          = league,
            Score           = 0,
            TieBreakAt      = DateTimeOffset.UtcNow,
            LastRank        = null,
            CreatedAt       = DateTimeOffset.UtcNow,
            UpdatedAt       = DateTimeOffset.UtcNow,
            IsDeleted       = false,
        };

    public Guid Id { get; private set; }
    public Guid GauntletEventId { get; private set; }
    public Guid PlayerId { get; private set; }

    /// <summary>The league this player competes in for the cycle — locked at first join.</summary>
    public GauntletLeague League { get; private set; }

    /// <summary>Cumulative damage to event raids — a denormalised running total (Slice 4 wires the update).</summary>
    public long Score { get; private set; }

    /// <summary>Timestamp of the last score-changing hit; the "earliest to reach" tiebreak source.</summary>
    public DateTimeOffset TieBreakAt { get; private set; }

    /// <summary>
    /// T76 — the highest ladder stage this player has DEFEATED this event. The PRIMARY ranking
    /// metric (DotD: "compete on a ladder of the highest tier completed"); Score (damage) and
    /// TieBreakAt break ties. Updated atomically in SQL (GREATEST) on each gauntlet stage kill.
    /// </summary>
    public int HighestStage { get; private set; }

    /// <summary>Snapshot/settled rank within the league; null until a rank pass runs (Slice 3).</summary>
    public int? LastRank { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // ── Domain methods ─────────────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="delta"/> to Score and moves TieBreakAt to <paramref name="hitAt"/>.
    /// Negative/zero deltas are ignored (TieBreakAt only advances on a real increase). Wired by Slice 4.
    /// </summary>
    public void AddScore(long delta, DateTimeOffset hitAt)
    {
        if (delta <= 0)
            return;
        Score      += delta;
        TieBreakAt =  hitAt;
        UpdatedAt  =  DateTimeOffset.UtcNow;
    }

    /// <summary>Stores a computed rank from a snapshot/settlement pass (Slice 3/5).</summary>
    public void SetRank(int rank)
    {
        LastRank  = rank;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
