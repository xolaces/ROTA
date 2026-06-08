using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// System 22 Phase A — the Masteries read/compute service. Combat/loot consumers (Slices 5/6) read
/// plain modifier values from here at their existing hooks; masteries are never modelled as
/// <c>ConditionalBonus</c> rows (they are per-player DB level-state, not content/inventory bonuses).
/// </summary>
public interface IMasteryService
{
    /// <summary>Full overview for GET /api/masteries (ensures level-1 rows; rating, titles, per-Ancient progress).</summary>
    Task<MasteryOverviewResponse> GetMasteriesAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Wrath (+% legion power) + Bulwark (+% guild-raid damage) modifiers for the combat hooks (Slice 5).</summary>
    Task<MasteryCombatModifiers> GetCombatModifiersAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Hoard (drop/gold) + Discernment (sigil-find/quality) modifiers for the loot hooks (Slices 6/7).</summary>
    Task<MasteryLootModifiers> GetLootModifiersAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Both combat + loot modifiers from ONE load of (levels + pledge) — used by the raid hit path
    /// (Slice 6) so the money path reads mastery state once, not twice.</summary>
    Task<MasteryModifiers> GetModifiersAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Refreshes the MasteryRating leaderboard boards (Live snapshot). Returns the count snapshotted.</summary>
    Task<int> SnapshotRatingBoardAsync(CancellationToken ct = default);

    /// <summary>
    /// LOSSLESS pledge change (Slice 3). Resolves the cheapest eligible path — free first-pledge to the
    /// Ancient → free monthly → paid weekly (gems, Redis-capped) — then flips the active pledge. Mastery
    /// levels are never touched.
    /// </summary>
    Task<MasteryRespecResult> RespecAsync(Guid playerId, MasteryAncient toAncient, CancellationToken ct = default);

    /// <summary>Pure Overall Mastery Rating (Formula B). Missing Ancients default to level 1.</summary>
    int ComputeRating(IReadOnlyDictionary<MasteryAncient, int> levels);

    /// <summary>
    /// Records mastery challenge-counter activity (Slice 4). Best-effort at chokepoints. When
    /// <paramref name="referenceId"/> is supplied the increment is exactly-once (idempotency ledger).
    /// Does NOT evaluate tier-ups (that runs off the hot path).
    /// </summary>
    Task RecordActivityAsync(Guid playerId, MasteryActivityType activityType, long amount = 1,
        string? referenceId = null, CancellationToken ct = default);

    /// <summary>
    /// Evaluates and persists any earned tier-ups for the player (off the hot path — called on read +
    /// after quest completion). Monotonic; audited; may advance several levels at once.
    /// </summary>
    Task EvaluateTierUpsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// ADMIN/DEV ONLY — force one or more Ancients' levels (1..5, up OR down) for testing, bypassing
    /// the monotonic/challenge rules. Audited. Returns the refreshed overview.
    /// </summary>
    Task<MasteryOverviewResponse> ForceLevelsAsync(
        Guid playerId, IReadOnlyDictionary<MasteryAncient, int> levels, CancellationToken ct = default);
}

/// <summary>
/// Combat modifiers. <see cref="WrathLegionPercent"/> is a PERCENT added into the legion bonus sum
/// (pre /100, like a General's LegionBonus). <see cref="BulwarkGuildDamageFraction"/> is a FRACTION
/// (already hard-capped) added to FlatDamagePercent, applied on guild raids only.
/// </summary>
public sealed record MasteryCombatModifiers(double WrathLegionPercent, double BulwarkGuildDamageFraction);

/// <summary>
/// Loot modifiers. Drop/gold/sigil-find values are MULTIPLIERS (≥ 1.0). <see cref="DiscernmentQualityChance"/>
/// is a FRACTION (the rarity-upgrade roll chance, consumed in Slice 7).
/// </summary>
public sealed record MasteryLootModifiers(
    double HoardDropMultiplier,
    double HoardGoldMultiplier,
    double DiscernmentSigilFindMultiplier,
    double DiscernmentQualityChance);

/// <summary>Combat + loot modifiers together (one mastery-state load).</summary>
public sealed record MasteryModifiers(MasteryCombatModifiers Combat, MasteryLootModifiers Loot);
