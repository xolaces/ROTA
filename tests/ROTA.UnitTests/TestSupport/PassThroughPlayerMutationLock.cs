using ROTA.Application.Interfaces;

namespace ROTA.UnitTests.TestSupport;

/// <summary>
/// Test double for <see cref="IPlayerMutationLock"/> — runs the action directly with no advisory lock
/// or transaction. Unit tests exercise the service's business logic; the real lock's per-player
/// serialization is covered by integration tests against Postgres.
/// </summary>
public sealed class PassThroughPlayerMutationLock : IPlayerMutationLock
{
    public Task<T> RunAsync<T>(Guid playerId, Func<Task<T>> action, CancellationToken ct = default)
        => action();
}
