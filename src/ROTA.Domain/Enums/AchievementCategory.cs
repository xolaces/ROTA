namespace ROTA.Domain.Enums;

/// <summary>
/// Grouping bucket for an achievement (TICKET 46). Purely presentational — used to organise the
/// roster on the (deferred) browse screen and in the GET /api/achievements payload. Persisted only
/// inside JSON content, never to a column. Append new categories at the end, never renumber.
/// </summary>
public enum AchievementCategory
{
    RaidCompletion = 0,
    QuestClear     = 1,
    EquipmentOwned = 2,
    DaysPlayed     = 3,
    Collector      = 4,
    ZoneMastery    = 5,   // System 25 — per-zone rerun ladders
}
