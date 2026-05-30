using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Business logic for generating and redeeming beta access keys.
/// </summary>
public interface IBetaKeyService
{
    /// <summary>
    /// Generates <paramref name="count"/> unique beta keys in ROTA-XXXX-XXXX-XXXX format
    /// using Crockford base32 from <see cref="System.Security.Cryptography.RandomNumberGenerator"/>.
    /// Writes a <c>BetaKeyGenerated</c> audit entry per batch.
    /// </summary>
    /// <param name="actorPlayerId">Admin PlayerId, or null / Guid.Empty for CLI/system actors.</param>
    /// <param name="count">Number of keys to generate (1–100).</param>
    Task<IReadOnlyList<BetaKey>> GenerateAsync(Guid? actorPlayerId, int count, CancellationToken ct = default);

    /// <summary>
    /// Validates and atomically redeems the key for <paramref name="newPlayerId"/>.
    /// Returns <c>true</c> on success, <c>false</c> if the key is invalid or already used.
    /// </summary>
    Task<bool> ValidateAndRedeemAsync(string key, Guid newPlayerId, CancellationToken ct = default);
}
