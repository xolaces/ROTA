namespace ROTA.Application.Configuration;

public class LevelingConfig
{
    public double XpBaseMultiplier { get; set; } = 30.0;

    // Defaults MUST match appsettings.json. This was 0.7 while appsettings said 0.8, so a binding
    // failure would silently have halved the late-game XP requirement instead of failing loudly.
    public double XpExponent { get; set; } = 0.8;

    /// <summary>
    /// Minimum XP-to-next-level per level -- a LINEAR floor under the whole curve.
    ///
    /// Why it exists: a player's stamina pool grows LINEARLY with level (the LSI cap bounds
    /// Energy + Stamina x 2 to 7.45 x level, so an all-stamina build reaches ~3.725 x level), while
    /// `XpBaseMultiplier * level^XpExponent` grows SUBLINEARLY. Linear always overtakes sublinear --
    /// past about level 139 a single full stamina dump earns more XP than a level costs.
    ///
    /// MilestoneFloors only patched that in steps, so pacing sawtoothed: each milestone bought some
    /// headroom, the pool caught up, and the player auto-levelled until the next milestone. At level
    /// 2,499 one dump was worth 1.12 levels; at 4,000, 1.28.
    ///
    /// A linear floor fixes it structurally because it grows at the same rate as the pool. At 14 the
    /// worst case across the whole 1..25,000 range is 0.82 levels per full dump, and it LOWERS the
    /// requirement nowhere -- every existing milestone floor already exceeds 14 x level at its own
    /// level, so this only fills the troughs between them.
    ///
    /// Tuning: levels-per-dump is about (3.0 XP/stamina x 3.725 x level) / (this x level), i.e.
    /// roughly 11.2 / this. Raise it to slow levelling, lower it to speed up; below ~11.2 the
    /// auto-levelling returns. 0 disables the floor entirely (the pre-2026-08-25 behaviour).
    /// </summary>
    public double XpLinearPerLevel { get; set; } = 14.0;

    public Dictionary<int, int> MilestoneFloors { get; set; } = new();

    /// <summary>
    /// Gem rewards granted on reaching exact pinnacle / milestone levels (T32): level → gems.
    /// Class-gate levels (the mandatory class-select overlay) are <c>ClassConfig.ConvergenceLevels</c>;
    /// this map is the gem grant, which also includes two gem-only milestones (1000, 2500) that are NOT
    /// class gates. The convergence tiers 2000/15000/25000 are intentionally absent until their gem
    /// amounts are confirmed — a level not present here grants no pinnacle gems.
    /// </summary>
    public Dictionary<int, int> PinnacleGemRewards { get; set; } = new();

    public int GetFloor(int level)
        => MilestoneFloors
            .Where(kvp => level >= kvp.Key)
            .OrderByDescending(kvp => kvp.Key)
            .Select(kvp => (int?)kvp.Value)
            .FirstOrDefault() ?? 0;

    /// <summary>Gems to grant on reaching exactly <paramref name="level"/> (0 when it is not a pinnacle level).</summary>
    public int GetPinnacleGems(int level)
        => PinnacleGemRewards.TryGetValue(level, out var gems) ? gems : 0;

    /// <summary>
    /// True if <paramref name="level"/> is a configured pinnacle level. Drives both the gem reward (T32)
    /// and the first-claim logging (T33), so a single config map is the one source of "pinnacle levels".
    /// </summary>
    public bool IsPinnacleLevel(int level) => PinnacleGemRewards.ContainsKey(level);
}
