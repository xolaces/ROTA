namespace ROTA.Application.Interfaces;

/// <summary>Pinnacle first-claim handling (T33): records who reached a pinnacle level first + notifies ops.</summary>
public interface IPinnacleService
{
    /// <summary>
    /// Records the first claim of <paramref name="pinnacleLevel"/>. Returns true if THIS player was the
    /// first (writes audit + raises a PinnacleFirstClaim operator email); false if already claimed.
    /// Idempotent and non-blocking on email.
    /// </summary>
    Task<bool> RecordFirstClaimAsync(Guid playerId, int pinnacleLevel, CancellationToken ct = default);
}
