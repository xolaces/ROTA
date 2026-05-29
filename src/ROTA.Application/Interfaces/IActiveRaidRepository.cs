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
}
