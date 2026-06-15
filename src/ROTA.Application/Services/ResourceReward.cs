namespace ROTA.Application.Services;

/// <summary>
/// Resource-spent reward rolls. XP earned scales with the RESOURCE SPENT — energy for quests, stamina
/// for raids — and NEVER with player level (level only raises the cost to level, see
/// <c>IStatService.XpToNextLevel</c>). Owner decision 2026-06-14.
///
/// "Summed per-unit": each point of resource independently rolls Uniform[min, max] and the rolls are
/// summed. This is <b>batching-invariant</b> (a 20-stamina hit ≈ twenty 1-stamina hits) and bell-curved
/// around <c>units × (min+max)/2</c>, so a bulk spend pays out predictably instead of riding a single
/// swingy multiplier. Mean is identical to a single-roll-×-units model — only the variance is tighter.
///
/// Consequence by design: with XP flat-per-resource and TNL climbing with level, late-game leveling is
/// throughput-gated (pool + pots), exactly the DotD power-leveling model.
/// </summary>
public static class ResourceReward
{
    /// <summary>
    /// Sum of <paramref name="units"/> independent Uniform[min, max] draws, rounded to a whole reward.
    /// Returns 0 for a non-positive spend. When min == max the draw collapses to a constant, so the
    /// result is exactly <c>units × min</c> (deterministic — used to pin unit tests).
    /// </summary>
    public static long RollSummed(Random rng, int units, double min, double max)
    {
        if (units <= 0) return 0;
        double span = max - min;
        double total = 0.0;
        for (int i = 0; i < units; i++)
            total += min + rng.NextDouble() * span;
        return (long)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Expected (mean) payout for a resource spend — <c>units × (min+max)/2</c> — for display previews
    /// where the actual roll happens later (e.g. the quest availability card before the attempt).
    /// </summary>
    public static long Mean(int units, double min, double max)
        => units <= 0 ? 0 : (long)Math.Round(units * (min + max) / 2.0, MidpointRounding.AwayFromZero);
}
