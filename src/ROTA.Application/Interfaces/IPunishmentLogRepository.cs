using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Append-only store for moderation actions (northstar §6). There is deliberately no update or
/// delete: a mistaken entry is corrected by appending, never by editing history.
/// </summary>
public interface IPunishmentLogRepository
{
    Task AppendAsync(PunishmentLog entry, CancellationToken ct = default);

    /// <summary>
    /// The entry that placed the punishment currently in force against <paramref name="targetPlayerId"/>,
    /// or null if none is. <paramref name="type"/> must be <see cref="PunishmentType.Ban"/> or
    /// <see cref="PunishmentType.Mute"/>.
    ///
    /// This is what lets a reversal check the AUTHORITY behind what it is about to lift, rather than
    /// only the shape of it.
    /// </summary>
    Task<PunishmentLog?> FindActivePunishmentAsync(
        Guid targetPlayerId, PunishmentType type, CancellationToken ct = default);

    /// <summary>Full moderation history for one player, newest first. For dispute review.</summary>
    Task<IReadOnlyList<PunishmentLog>> GetHistoryAsync(
        Guid targetPlayerId, int limit = 100, CancellationToken ct = default);
}
