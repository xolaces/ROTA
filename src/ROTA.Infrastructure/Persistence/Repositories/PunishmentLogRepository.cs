using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PunishmentLogRepository : IPunishmentLogRepository
{
    private readonly RotaDbContext _db;

    public PunishmentLogRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(PunishmentLog entry, CancellationToken ct = default)
    {
        _db.PunishmentLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PunishmentLog?> FindActivePunishmentAsync(
        Guid targetPlayerId, PunishmentType type, CancellationToken ct = default)
    {
        var reversal = type switch
        {
            PunishmentType.Ban  => PunishmentType.Unban,
            PunishmentType.Mute => PunishmentType.Unmute,
            _ => throw new ArgumentOutOfRangeException(
                nameof(type), type, "Only Ban and Mute can be in force; the others ARE reversals."),
        };

        // Newest entry of the applied/reversed PAIR decides. If it is the punishment, it stands; if it
        // is the reversal, nothing is in force. Cheaper and less error-prone than a NOT EXISTS over
        // later reversals, and it stays correct when a player is punished, cleared and punished again.
        var latest = await _db.PunishmentLogs
            .AsNoTracking()
            .Where(p => p.TargetPlayerId == targetPlayerId && (p.Type == type || p.Type == reversal))
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)   // two entries can share a timestamp; id breaks the tie
            .FirstOrDefaultAsync(ct);

        return latest?.Type == type ? latest : null;
    }

    public async Task<IReadOnlyList<PunishmentLog>> GetHistoryAsync(
        Guid targetPlayerId, int limit = 100, CancellationToken ct = default)
    {
        return await _db.PunishmentLogs
            .AsNoTracking()
            .Where(p => p.TargetPlayerId == targetPlayerId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .ToListAsync(ct);
    }
}
