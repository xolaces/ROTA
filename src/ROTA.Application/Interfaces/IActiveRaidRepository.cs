using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IActiveRaidRepository
{
    Task<ActiveRaid?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveRaid>> GetAllActiveAsync(CancellationToken ct = default);
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
