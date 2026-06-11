namespace ROTA.Application.Models;

// BETA (System 16 Slice 1) — the Gauntlet prize table, loaded from
// content/gauntlet_prizes.json. Bands MUST be non-overlapping, contiguous, and together
// cover exactly 1..GauntletConfig.PrizeRankCount (enforced by GauntletContentProvider).
public class GauntletPrizeTable
{
    public List<GauntletPrizeBand> Bands { get; set; } = new();

    // T76 — the RARE Ring-Gauntlet prize set (owner-locked: ring gear, NO rank magics). Optional
    // until the content wave authors it: when null/empty, Ring events fall back to the Neck bands
    // with MagicId STRIPPED (rings never grant magics). Same band invariants when present.
    public List<GauntletPrizeBand>? RingBands { get; set; }
}

// A single placement band → rewards. Ranks are inclusive [RankFrom, RankTo].
// TrophyId/MagicId are optional (only the top bands grant them).
public class GauntletPrizeBand
{
    public int     RankFrom  { get; set; }
    public int     RankTo    { get; set; }
    public int     Tokens    { get; set; }
    public int     Pitchfork { get; set; }
    public string? TrophyId  { get; set; }
    public string? MagicId   { get; set; }
}
