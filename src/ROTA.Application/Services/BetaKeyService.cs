using System.Security.Cryptography;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Application.Services;

/// <summary>
/// Generates and redeems beta access keys.
/// Key format: ROTA-XXXX-XXXX-XXXX using Crockford base32 (no 0/O/1/I/L).
/// </summary>
public sealed class BetaKeyService : IBetaKeyService
{
    // Crockford base32 alphabet — excludes 0, O, I, L to avoid visual confusion
    private const string CrockfordAlphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly IBetaKeyRepository _betaKeys;
    private readonly IAuditLogRepository _auditLog;

    public BetaKeyService(IBetaKeyRepository betaKeys, IAuditLogRepository auditLog)
    {
        _betaKeys = betaKeys;
        _auditLog = auditLog;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BetaKey>> GenerateAsync(
        Guid? actorPlayerId, int count, CancellationToken ct = default)
    {
        var keys = new List<BetaKey>(count);
        for (int i = 0; i < count; i++)
        {
            var keyString = GenerateKeyString();
            var betaKey = BetaKey.Create(keyString, actorPlayerId == Guid.Empty ? null : actorPlayerId);
            await _betaKeys.CreateAsync(betaKey, ct);
            keys.Add(betaKey);
        }

        var actorId = (actorPlayerId == Guid.Empty) ? null : actorPlayerId;
        await _auditLog.AppendAsync(AuditLog.Create(
            actorId,
            "BetaKeyGenerated",
            null,
            $"Generated {count} beta key(s)",
            null));

        return keys;
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateAndRedeemAsync(string key, Guid newPlayerId, CancellationToken ct = default)
        => await _betaKeys.TryRedeemAsync(key, newPlayerId, ct);

    // -----------------------------------------------------------------------
    // Key generation
    // -----------------------------------------------------------------------

    private static string GenerateKeyString()
    {
        // Generate three groups of 4 Crockford base32 characters: ROTA-XXXX-XXXX-XXXX
        return $"ROTA-{GenerateSegment(4)}-{GenerateSegment(4)}-{GenerateSegment(4)}";
    }

    private static string GenerateSegment(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = CrockfordAlphabet[bytes[i] % CrockfordAlphabet.Length];
        return new string(chars);
    }
}
