namespace ROTA.Domain.Enums;

/// <summary>
/// The kind of moderation action a <see cref="Entities.PunishmentLog"/> row records.
///
/// Reversals are first-class entries rather than a flag on the original, because the punishment log
/// is append-only: lifting a ban must not edit the row that placed it. A dispute needs to see both
/// halves and the gap between them.
/// </summary>
public enum PunishmentType
{
    Ban = 0,
    Unban = 1,
    Mute = 2,
    Unmute = 3,
}
