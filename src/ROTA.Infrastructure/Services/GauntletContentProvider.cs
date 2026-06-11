using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

// BETA (System 16 Slice 1) — singleton; loads content/gauntlet_prizes.json,
// content/gauntlet_trophies.json, content/gauntlet_raids.json at construction and
// validates them (and the two Gauntlet magics + league bounds) against the spec.
// Throws InvalidOperationException at startup on any invalid data — mirrors
// MagicDefinitionProvider. Combat reads none of this yet (Slices 2/4).
public sealed class GauntletContentProvider : IGauntletContentProvider
{
    // Reserved ids for the Gauntlet rank magics + the existing raid-drop magics they
    // must NOT collide with (naming guard, spec §"Naming guard").
    private const string WrathId    = "magic_wrath_of_the_ancients";
    private const string BlessingId = "magic_blessing_of_the_ancients";
    private const string SmiteId            = "magic_smite";
    private const string BlessingOfMightId  = "magic_blessing_of_might";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly GauntletPrizeTable _prizeTable;
    private readonly IReadOnlyDictionary<string, GauntletTrophyDefinition> _trophies;
    private readonly IReadOnlyList<GauntletRaidDefinition> _raids;          // ordered by LadderStage
    private readonly IReadOnlyDictionary<int, GauntletRaidDefinition> _raidsByStage;
    private readonly GauntletConfig _config;

    public GauntletContentProvider(
        string contentRootPath,
        IMagicDefinitionProvider magicProvider,
        IOptions<GauntletConfig> config)
    {
        _config = config.Value;

        var trophies = LoadTrophies(contentRootPath);
        var prizeTable = LoadPrizes(contentRootPath);
        var raids = LoadRaids(contentRootPath);

        // Validation order: standalone content first, then cross-references, then config.
        ValidateLeagueBounds(_config);
        ValidateTrophies(trophies);
        ValidateGauntletMagics(magicProvider);
        ValidateNamingGuard(magicProvider);
        ValidateRaids(raids);
        ValidatePrizes(prizeTable, _config, trophies, magicProvider);

        _trophies = trophies.ToDictionary(t => t.Id, t => t, StringComparer.Ordinal);
        _prizeTable = prizeTable;

        // T54 — formula-extend the ladder to MaxLadderStage when configured (0/≤count = off → JSON
        // stages exactly as loaded, so small-fixture unit tests are unaffected).
        var ladder = raids.OrderBy(r => r.LadderStage).ToList();
        if (_config.MaxLadderStage > ladder.Count)
            ladder = BuildExtendedLadder(ladder, _config);
        _raids = ladder;
        _raidsByStage = _raids.ToDictionary(r => r.LadderStage, r => r);
    }

    // T54 — regenerate the ladder as config.MaxLadderStage stages whose HP/rewards follow the single
    // smooth GauntletStageCurve (no per-stage JSON authoring). Early stages keep their JSON name/art/id;
    // generated stages get a synthesized id + name. Strictly rising by construction (growth > 1), so the
    // ladder invariants hold; stage-1 HP == StageHpBase keeps the shipped integration assertion valid.
    private static List<GauntletRaidDefinition> BuildExtendedLadder(
        List<GauntletRaidDefinition> jsonRaids, GauntletConfig config)
    {
        var result = new List<GauntletRaidDefinition>(config.MaxLadderStage);
        for (var stage = 1; stage <= config.MaxLadderStage; stage++)
        {
            var src = stage <= jsonRaids.Count ? jsonRaids[stage - 1] : null;
            result.Add(new GauntletRaidDefinition
            {
                Id                   = src?.Id   ?? $"gauntlet_stage_{stage}",
                Name                 = src?.Name ?? $"Gauntlet — Stage {stage}",
                Tier                 = "Event",
                LadderStage          = stage,
                BaseHp               = GauntletStageCurve.Hp(stage, config),
                TimerHours           = src?.TimerHours        ?? 24,
                StaminaCostPerHit    = src?.StaminaCostPerHit ?? 1,
                LootTableId          = src?.LootTableId       ?? string.Empty,
                BaseGoldReward       = GauntletStageCurve.Gold(stage, config),
                BaseExperienceReward = GauntletStageCurve.Xp(stage, config),
                BaseGemReward        = src?.BaseGemReward     ?? 0,
                ArtKey               = src?.ArtKey            ?? $"gauntlet_stage_{stage}",
                GauntletScored       = true,
            });
        }
        return result;
    }

    // ── Accessors ────────────────────────────────────────────────────────────

    public GauntletPrizeTable GetPrizeTable() => _prizeTable;

    public GauntletPrizeBand? GetBandForRank(int rank)
        => _prizeTable.Bands.FirstOrDefault(b => rank >= b.RankFrom && rank <= b.RankTo);

    // T76 — kind-aware lookup. Ring uses its own authored set when present; otherwise the Neck
    // bands with MagicId stripped (ring gauntlets never grant rank magics — owner-locked).
    public GauntletPrizeBand? GetBandForRank(int rank, GauntletEventKind kind)
    {
        if (kind == GauntletEventKind.Neck)
            return GetBandForRank(rank);

        if (_prizeTable.RingBands is { Count: > 0 })
            return _prizeTable.RingBands.FirstOrDefault(b => rank >= b.RankFrom && rank <= b.RankTo);

        var neck = GetBandForRank(rank);
        return neck is null ? null : StripMagic(neck);
    }

    // T76 — the full kind-aware band list for the prize preview table; same fallback rule as the
    // single-rank lookup so the preview always matches what settle would actually pay.
    public IReadOnlyList<GauntletPrizeBand> GetBands(GauntletEventKind kind)
    {
        if (kind == GauntletEventKind.Neck)
            return _prizeTable.Bands.OrderBy(b => b.RankFrom).ToList();

        if (_prizeTable.RingBands is { Count: > 0 })
            return _prizeTable.RingBands.OrderBy(b => b.RankFrom).ToList();

        return _prizeTable.Bands.OrderBy(b => b.RankFrom).Select(StripMagic).ToList();
    }

    private static GauntletPrizeBand StripMagic(GauntletPrizeBand neck) => new()
    {
        RankFrom  = neck.RankFrom,
        RankTo    = neck.RankTo,
        Tokens    = neck.Tokens,
        Pitchfork = neck.Pitchfork,
        TrophyId  = neck.TrophyId,
        MagicId   = null,
    };

    public IReadOnlyList<GauntletTrophyDefinition> GetAllTrophies() => _trophies.Values.ToList();

    public GauntletTrophyDefinition? GetTrophyById(string id)
        => _trophies.TryGetValue(id, out var t) ? t : null;

    public IReadOnlyList<GauntletRaidDefinition> GetGauntletRaids() => _raids;

    public GauntletRaidDefinition? GetGauntletRaidByStage(int ladderStage)
        => _raidsByStage.TryGetValue(ladderStage, out var r) ? r : null;

    // T76 — reverse lookup for the combat kill hook (raid definition id → ladder stage).
    public GauntletRaidDefinition? GetGauntletRaidByDefinitionId(string raidDefinitionId)
        => _raids.FirstOrDefault(r => r.Id == raidDefinitionId);

    public GauntletLeague ResolveLeague(int level)
    {
        // Bounds are validated as ordered + contiguous, so the first containing band wins.
        foreach (var league in OrderedLeagues(_config))
        {
            var b = _config.LeagueBounds[league.ToString()];
            if (level >= b.Min && level <= b.Max)
                return league;
        }

        throw new InvalidOperationException(
            $"GauntletConfig.LeagueBounds does not cover level {level}.");
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private static List<GauntletTrophyDefinition> LoadTrophies(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "gauntlet_trophies.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<GauntletTrophyDefinition>>(json, JsonOptions)
            ?? throw new InvalidOperationException("gauntlet_trophies.json deserialized to null.");
    }

    private static GauntletPrizeTable LoadPrizes(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "gauntlet_prizes.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GauntletPrizeTable>(json, JsonOptions)
            ?? throw new InvalidOperationException("gauntlet_prizes.json deserialized to null.");
    }

    private static List<GauntletRaidDefinition> LoadRaids(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "gauntlet_raids.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<GauntletRaidDefinition>>(json, JsonOptions)
            ?? throw new InvalidOperationException("gauntlet_raids.json deserialized to null.");
    }

    // ── Validation ───────────────────────────────────────────────────────────

    private static void ValidateTrophies(List<GauntletTrophyDefinition> trophies)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in trophies)
        {
            if (string.IsNullOrWhiteSpace(t.Id))
                throw new InvalidOperationException("gauntlet_trophies.json: a trophy has an empty id.");
            if (!seen.Add(t.Id))
                throw new InvalidOperationException(
                    $"gauntlet_trophies.json: duplicate id '{t.Id}'.");
            if (t.LegionPowerBonusFraction <= 0.0)
                throw new InvalidOperationException(
                    $"gauntlet_trophies.json: trophy '{t.Id}' has legionPowerBonusFraction " +
                    $"{t.LegionPowerBonusFraction}; must be > 0.");
        }
    }

    private static void ValidateRaids(List<GauntletRaidDefinition> raids)
    {
        if (raids.Count == 0)
            throw new InvalidOperationException("gauntlet_raids.json: no Gauntlet raid stages defined.");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenStages = new HashSet<int>();
        foreach (var r in raids)
        {
            if (string.IsNullOrWhiteSpace(r.Id))
                throw new InvalidOperationException("gauntlet_raids.json: a raid stage has an empty id.");
            if (!seenIds.Add(r.Id))
                throw new InvalidOperationException(
                    $"gauntlet_raids.json: duplicate id '{r.Id}'.");
            if (!seenStages.Add(r.LadderStage))
                throw new InvalidOperationException(
                    $"gauntlet_raids.json: duplicate ladderStage {r.LadderStage} (id '{r.Id}').");
            if (!string.Equals(r.Tier, "Event", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"gauntlet_raids.json: raid '{r.Id}' tier must be \"Event\"; got \"{r.Tier}\".");
            if (!r.GauntletScored)
                throw new InvalidOperationException(
                    $"gauntlet_raids.json: raid '{r.Id}' must have gauntletScored = true.");
            if (r.BaseHp <= 0)
                throw new InvalidOperationException(
                    $"gauntlet_raids.json: raid '{r.Id}' must have baseHp > 0; got {r.BaseHp}.");
        }

        // Ladder must be ascending and contiguous from 1, with strictly rising HP.
        var ordered = raids.OrderBy(r => r.LadderStage).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var expectedStage = i + 1;
            if (ordered[i].LadderStage != expectedStage)
                throw new InvalidOperationException(
                    $"gauntlet_raids.json: ladderStage values must be contiguous starting at 1; " +
                    $"expected {expectedStage} but found {ordered[i].LadderStage} (id '{ordered[i].Id}').");
            if (i > 0 && ordered[i].BaseHp <= ordered[i - 1].BaseHp)
                throw new InvalidOperationException(
                    $"gauntlet_raids.json: baseHp must strictly rise with ladderStage; stage " +
                    $"{ordered[i].LadderStage} ('{ordered[i].Id}') baseHp {ordered[i].BaseHp} is not " +
                    $"greater than stage {ordered[i - 1].LadderStage} baseHp {ordered[i - 1].BaseHp}.");
        }
    }

    private static void ValidateGauntletMagics(IMagicDefinitionProvider magicProvider)
    {
        foreach (var id in new[] { WrathId, BlessingId })
        {
            var magic = magicProvider.GetById(id)
                ?? throw new InvalidOperationException(
                    $"magics.json: required Gauntlet magic '{id}' is missing.");

            if (magic.ProcChance <= 0.0 || magic.ProcChance > 1.0)
                throw new InvalidOperationException(
                    $"magics.json: Gauntlet magic '{id}' has procChance {magic.ProcChance} " +
                    "outside (0, 1].");
            if (magic.ProcAmount <= 0.0)
                throw new InvalidOperationException(
                    $"magics.json: Gauntlet magic '{id}' has procAmount {magic.ProcAmount}; must be > 0.");
            if (!magic.OffCap)
                throw new InvalidOperationException(
                    $"magics.json: Gauntlet magic '{id}' must have offCap = true (off-cap aura).");
        }
    }

    private static void ValidateNamingGuard(IMagicDefinitionProvider magicProvider)
    {
        // The Gauntlet rank magics must be distinct entries from the ordinary raid-drop
        // magics; a content edit that reuses those ids would silently repurpose Smite /
        // Blessing of Might. Assert both reserved ordinary ids still resolve to THEIR own
        // definitions (not the Gauntlet ones) so a collision cannot pass startup.
        AssertDistinct(magicProvider, SmiteId, "Smite", WrathId, BlessingId);
        AssertDistinct(magicProvider, BlessingOfMightId, "Blessing of Might", WrathId, BlessingId);
    }

    private static void AssertDistinct(
        IMagicDefinitionProvider magicProvider,
        string ordinaryId,
        string expectedName,
        string wrathId,
        string blessingId)
    {
        if (string.Equals(ordinaryId, wrathId, StringComparison.Ordinal)
            || string.Equals(ordinaryId, blessingId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Gauntlet naming guard: id '{ordinaryId}' collides with a Gauntlet rank-magic id.");

        var ordinary = magicProvider.GetById(ordinaryId);
        if (ordinary is not null && !string.Equals(ordinary.Name, expectedName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Gauntlet naming guard: magic '{ordinaryId}' is expected to be the ordinary " +
                $"raid-drop magic \"{expectedName}\" but resolved to \"{ordinary.Name}\" — the " +
                "Gauntlet rank magics must not collide with it.");
    }

    private static void ValidatePrizes(
        GauntletPrizeTable table,
        GauntletConfig config,
        List<GauntletTrophyDefinition> trophies,
        IMagicDefinitionProvider magicProvider)
    {
        if (table.Bands.Count == 0)
            throw new InvalidOperationException("gauntlet_prizes.json: no prize bands defined.");

        var trophyIds = new HashSet<string>(trophies.Select(t => t.Id), StringComparer.Ordinal);

        foreach (var band in table.Bands)
        {
            if (band.RankFrom < 1 || band.RankTo < band.RankFrom)
                throw new InvalidOperationException(
                    $"gauntlet_prizes.json: invalid band range [{band.RankFrom}..{band.RankTo}].");

            if (band.TrophyId is not null && !trophyIds.Contains(band.TrophyId))
                throw new InvalidOperationException(
                    $"gauntlet_prizes.json: band [{band.RankFrom}..{band.RankTo}] references " +
                    $"trophyId '{band.TrophyId}' which is not defined in gauntlet_trophies.json.");

            if (band.MagicId is not null && magicProvider.GetById(band.MagicId) is null)
                throw new InvalidOperationException(
                    $"gauntlet_prizes.json: band [{band.RankFrom}..{band.RankTo}] references " +
                    $"magicId '{band.MagicId}' which is not defined in magics.json.");
        }

        // Bands must be non-overlapping, contiguous, and cover exactly 1..PrizeRankCount.
        var ordered = table.Bands.OrderBy(b => b.RankFrom).ToList();
        var expectedNext = 1;
        foreach (var band in ordered)
        {
            if (band.RankFrom != expectedNext)
                throw new InvalidOperationException(
                    $"gauntlet_prizes.json: bands must be contiguous with no gap/overlap; expected a " +
                    $"band starting at rank {expectedNext} but found one starting at {band.RankFrom}.");
            expectedNext = band.RankTo + 1;
        }

        var lastRank = expectedNext - 1;
        if (lastRank != config.PrizeRankCount)
            throw new InvalidOperationException(
                $"gauntlet_prizes.json: bands must cover exactly 1..{config.PrizeRankCount} " +
                $"(GauntletConfig.PrizeRankCount); they cover 1..{lastRank}.");
    }

    private static void ValidateLeagueBounds(GauntletConfig config)
    {
        var leagues = OrderedLeagues(config);

        // Every league enum value must have a configured bound.
        foreach (var league in Enum.GetValues<GauntletLeague>())
        {
            if (!config.LeagueBounds.ContainsKey(league.ToString()))
                throw new InvalidOperationException(
                    $"GauntletConfig.LeagueBounds is missing an entry for league '{league}'.");
        }

        // Must start at level 1, be ordered, non-overlapping, contiguous, and cover up to
        // the top league's open-ended max.
        var expectedNext = 1;
        foreach (var league in leagues)
        {
            var b = config.LeagueBounds[league.ToString()];
            if (b.Min > b.Max)
                throw new InvalidOperationException(
                    $"GauntletConfig.LeagueBounds['{league}'] has Min {b.Min} > Max {b.Max}.");
            if (b.Min != expectedNext)
                throw new InvalidOperationException(
                    $"GauntletConfig.LeagueBounds must be contiguous and ordered from level 1; " +
                    $"expected league '{league}' to start at {expectedNext} but it starts at {b.Min}.");
            expectedNext = b.Max == GauntletConfig.NoMaxLevel ? GauntletConfig.NoMaxLevel : b.Max + 1;
        }

        // The top league must be open-ended so every valid level resolves to a league.
        if (expectedNext != GauntletConfig.NoMaxLevel)
            throw new InvalidOperationException(
                $"GauntletConfig.LeagueBounds must cover the full level range; the top league must " +
                $"extend to the no-max sentinel ({GauntletConfig.NoMaxLevel}). Coverage ends at {expectedNext - 1}.");
    }

    // Leagues sorted by their configured Min so validation/resolution are order-independent.
    private static List<GauntletLeague> OrderedLeagues(GauntletConfig config)
        => Enum.GetValues<GauntletLeague>()
            .Where(l => config.LeagueBounds.ContainsKey(l.ToString()))
            .OrderBy(l => config.LeagueBounds[l.ToString()].Min)
            .ToList();
}
