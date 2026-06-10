using Moq;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.TestSupport;

/// <summary>
/// T59 — wires <see cref="IPlayerRepository.MutateWithRetryAsync{TResult}"/> on a Moq mock as a
/// single-shot pass-through: load the player via the mock's own FindByIdAsync setup, invoke the
/// callback once, return its result. Unit tests have no concurrency, so no retry path is modeled —
/// the real retry loop is covered by the PlayerRepository integration tests.
/// </summary>
public static class PlayerRepositoryMockExtensions
{
    public static void SetupMutatePassThrough(this Mock<IPlayerRepository> players)
    {
        players.Setup(r => r.MutateWithRetryAsync(
                It.IsAny<Guid>(), It.IsAny<Func<Player, IReadOnlyList<int>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, Func<Player, IReadOnlyList<int>> mutate, CancellationToken ct) =>
            {
                // Tests stub whichever Find* variant their service path uses — fall back across them.
                var player = await players.Object.FindByIdAsync(id, ct)
                    ?? await players.Object.FindByIdWithStatsAsync(id, ct)
                    ?? await players.Object.FindByIdWithResourcesAsync(id, ct)
                    ?? throw new InvalidOperationException($"Player {id} not found for reward mutation.");
                return mutate(player);
            });
    }
}
