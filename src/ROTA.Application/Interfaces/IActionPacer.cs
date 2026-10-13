namespace ROTA.Application.Interfaces;

/// <summary>
/// A minimum interval between repeats of one action by one player (quest attempts: 0.8 s).
/// Implemented on Redis so it holds across API instances.
/// </summary>
public interface IActionPacer
{
    /// <summary>
    /// Starts the window for <paramref name="action"/> when none is open and returns null. When one
    /// is still open, returns how long remains and leaves it untouched. Fails open (returns null)
    /// when the store is unreachable: pacing is a guard, not a dependency.
    /// </summary>
    Task<TimeSpan?> TryBeginAsync(string action, Guid playerId, TimeSpan minInterval, CancellationToken ct = default);
}
