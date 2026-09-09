using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IActiveRaidRepository
{
    Task<ActiveRaid?> FindByIdAsync(Guid id, CancellationToken ct = default);

    // Like FindByIdAsync but Includes the SummonedByPlayer navigation so callers can map
    // SummonedByUsername. Returns a tracked entity — safe to mutate (Share) and persist via UpdateAsync.
    Task<ActiveRaid?> FindByIdWithSummonerAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ActiveRaid>> GetAllActiveAsync(CancellationToken ct = default);

    // defeated (Lootable) raids where this player has a participant row with UNCLAIMED deferred
    // rewards (RewardedAt == null). Surfaced in the caller's active-raid list so they can return and
    // claim. SummonedByPlayer is Included for the response mapping.
    Task<IReadOnlyList<ActiveRaid>> GetLootableUnclaimedForPlayerAsync(Guid playerId, CancellationToken ct = default);

    // BETA (System 16 Slice 7) — all of a player's Gauntlet ladder raids for one event
    // (summoned_by_player_id == playerId && gauntlet_event_id == eventId && !is_deleted), regardless
    // of defeated/expired state. The ladder service reads these to find the current target stage or
    // derive the next stage to spawn. Not Include()-hydrated (the ladder maps from the entity directly).
    Task<IReadOnlyList<ActiveRaid>> GetGauntletStagesForPlayerAsync(
        Guid playerId, Guid gauntletEventId, CancellationToken ct = default);

    // World-raid expiry settlement — timer-only raids (MaxHp == 0) whose clock has run out but which are
    // still sitting in Active, i.e. nobody has settled the damage ladder yet. Scoped to MaxHp == 0 on
    // purpose: an ordinary health-pool raid that expires un-killed FAILED, and failing pays nothing.
    // Ordered oldest-first so a backlog drains in the order it accrued, and capped so one sweep tick can
    // never pull an unbounded set into memory.
    Task<IReadOnlyList<ActiveRaid>> GetExpiredUnsettledTimerRaidsAsync(
        DateTimeOffset asOf, int limit, CancellationToken ct = default);

    Task<ActiveRaid> CreateAsync(ActiveRaid raid, CancellationToken ct = default);
    Task UpdateAsync(ActiveRaid raid, CancellationToken ct = default);

    // Acquires a PostgreSQL FOR UPDATE row lock on the raid row, then runs the mutate
    // delegate under that lock within a single transaction.
    // Returns true and commits if mutate returns true; rolls back and returns false otherwise.
    // All repository calls inside mutate that share the same DbContext execute within
    // this transaction — rewards are therefore granted exactly once, atomically with the kill.
    Task<bool> AtomicApplyHitAsync(
        Guid raidId,
        Func<ActiveRaid, Task<bool>> mutate,
        CancellationToken ct = default);

    // Acquires the same pg_advisory_xact_lock for raidId, then runs the delegate within a
    // single transaction.  Used by magic application to serialise slot count→insert.
    // Returns true and commits if action returns true; rolls back and returns false otherwise.
    Task<bool> AtomicWithAdvisoryLockAsync(
        Guid raidId,
        Func<Task<bool>> action,
        CancellationToken ct = default);
}
