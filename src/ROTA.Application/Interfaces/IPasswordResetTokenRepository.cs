using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPasswordResetTokenRepository
{
    Task CreateAsync(PasswordResetToken token, CancellationToken ct = default);

    /// <summary>Soft-deletes every unused, unexpired token for the player (one live code at a time).</summary>
    Task InvalidateActiveAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Atomically consumes the token matching (player, codeHash) if it is unused, unexpired, and not
    /// deleted. Single conditional UPDATE — the WHERE clause is the single-use race guard. Returns
    /// false on wrong/expired/already-used code.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid playerId, string codeHash, CancellationToken ct = default);
}
