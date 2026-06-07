namespace ROTA.Domain.Enums;

/// <summary>
/// Guild role hierarchy (System 21 Slice 1). Permission checks compare by integer value:
/// higher rank = more authority. There is deliberately NO zero value — every membership row
/// always has a concrete rank, so the EF int column needs no store default (the factory always
/// sets it). Member is the floor; Leader is unique per guild.
/// </summary>
public enum GuildRank
{
    Member = 1,
    Officer = 2,
    Leader = 3,
}
