using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly RotaDbContext _db;

    public PasswordResetTokenRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task CreateAsync(PasswordResetToken token, CancellationToken ct = default)
    {
        _db.PasswordResetTokens.Add(token);
        await _db.SaveChangesAsync(ct);
    }

    public async Task InvalidateActiveAsync(Guid playerId, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE password_reset_tokens
               SET is_deleted = true,
                   updated_at = now()
             WHERE player_id  = {0}
               AND used_at IS NULL
               AND is_deleted = false
            """,
            parameters: new object[] { playerId },
            cancellationToken: ct);
    }

    public async Task<bool> TryConsumeAsync(Guid playerId, string codeHash, CancellationToken ct = default)
    {
        // Single atomic UPDATE — the WHERE clause is the single-use race guard (BetaKey pattern).
        var rowsAffected = await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE password_reset_tokens
               SET used_at    = now(),
                   updated_at = now()
             WHERE player_id  = {0}
               AND code_hash  = {1}
               AND used_at IS NULL
               AND expires_at > now()
               AND is_deleted = false
            """,
            parameters: new object[] { playerId, codeHash },
            cancellationToken: ct);

        return rowsAffected == 1;
    }
}
