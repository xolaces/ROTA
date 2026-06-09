namespace ROTA.Application.Configuration;

// TICKET 46 (Achievements) — scalar tuning surface, bound from appsettings "AchievementConfig" via
// IOptions<AchievementConfig>. Per-achievement point/threshold values live in
// content/achievements.json; this holds only cross-cutting scalar dials. Currently empty (reserved) —
// mirrors MasteryConfig's role so future tuning has a home without a content-schema change.
public class AchievementConfig
{
}
