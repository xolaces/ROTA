using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

// System 16 Slice 1 — provider load + startup-validation tests. Malformed content is fed
// via a temp content dir (a fresh copy per test); the real src/ROTA.Api/content JSON is
// never mutated.
public class GauntletContentProviderTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _contentDir;

    public GauntletContentProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_gauntlet_test_{Guid.NewGuid():N}");
        _contentDir = Path.Combine(_tmpDir, "content");
        Directory.CreateDirectory(_contentDir);

        // Seed each temp dir with a known-good copy of all four content files.
        WritePrizes(ValidPrizesJson);
        WriteTrophies(ValidTrophiesJson);
        WriteRaids(ValidRaidsJson);
        WriteMagics(ValidMagicsJson);
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    // ── Helpers ────────────────────────────────────────────────────────────

    private void WritePrizes(string json)   => File.WriteAllText(Path.Combine(_contentDir, "gauntlet_prizes.json"), json);
    private void WriteTrophies(string json) => File.WriteAllText(Path.Combine(_contentDir, "gauntlet_trophies.json"), json);
    private void WriteRaids(string json)    => File.WriteAllText(Path.Combine(_contentDir, "gauntlet_raids.json"), json);
    private void WriteMagics(string json)   => File.WriteAllText(Path.Combine(_contentDir, "magics.json"), json);

    private static IOptions<GauntletConfig> DefaultConfig() => Options.Create(new GauntletConfig());

    // Builds the provider against the temp dir, using a real MagicDefinitionProvider over
    // the temp magics.json (so magic cross-ref + offCap + naming-guard run for real).
    private GauntletContentProvider Build(IOptions<GauntletConfig>? config = null)
    {
        var magicProvider = new MagicDefinitionProvider(_tmpDir);
        return new GauntletContentProvider(_tmpDir, magicProvider, config ?? DefaultConfig());
    }

    private GauntletContentProvider BuildWithMagic(IMagicDefinitionProvider magicProvider)
        => new GauntletContentProvider(_tmpDir, magicProvider, DefaultConfig());

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Provider_LoadsValidContent()
    {
        var provider = Build();

        provider.GetPrizeTable().Bands.Should().HaveCount(7);
        provider.GetAllTrophies().Should().HaveCount(3);
        provider.GetGauntletRaids().Should().HaveCount(2);

        provider.GetBandForRank(1)!.MagicId.Should().Be("magic_wrath_of_the_ancients");
        provider.GetBandForRank(5)!.TrophyId.Should().Be("trophy_argent");
        provider.GetBandForRank(500)!.TrophyId.Should().Be("trophy_bronzed");
        provider.GetBandForRank(501).Should().BeNull("ranks past PrizeRankCount are unawarded");

        provider.GetTrophyById("trophy_aureate")!.LegionPowerBonusFraction.Should().Be(0.25);
        provider.GetGauntletRaidByStage(2)!.Id.Should().Be("gauntlet_stage_2");
        provider.GetGauntletRaids().Select(r => r.LadderStage).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public void GetBands_NeckKeepsMagics_RingFallbackStripsThem()
    {
        // T76 — the prize-preview list must match what settle would actually pay per kind. With no
        // authored RingBands, Ring serves the Neck bands with MagicId stripped (trophies kept).
        var provider = Build();

        var neck = provider.GetBands(GauntletEventKind.Neck);
        var ring = provider.GetBands(GauntletEventKind.Ring);

        neck.Should().HaveCount(7);
        neck.Select(b => b.RankFrom).Should().BeInAscendingOrder();
        neck[0].MagicId.Should().Be("magic_wrath_of_the_ancients");

        ring.Should().HaveCount(7, "the Ring fallback mirrors the Neck band structure");
        ring.Should().OnlyContain(b => b.MagicId == null, "rings never grant rank magics");
        ring[0].TrophyId.Should().Be(neck[0].TrophyId, "trophies survive the Ring fallback");
        ring[0].Tokens.Should().Be(neck[0].Tokens);
    }

    [Fact]
    public void Provider_LoadsRealApiContent()
    {
        // Point at the actual Api content directory — proves the shipped JSON validates.
        var apiContentRoot = FindApiContentRoot();
        var magicProvider = new MagicDefinitionProvider(apiContentRoot);
        var provider = new GauntletContentProvider(apiContentRoot, magicProvider, DefaultConfig());

        provider.GetPrizeTable().Bands.Should().NotBeEmpty();
        provider.GetAllTrophies().Should().HaveCount(3);
        provider.GetGauntletRaids().Should().NotBeEmpty();
    }

    [Fact]
    public void Wrath_LoadsWithLockedNumbers()
    {
        var apiContentRoot = FindApiContentRoot();
        var magic = new MagicDefinitionProvider(apiContentRoot).GetById("magic_wrath_of_the_ancients");

        magic.Should().NotBeNull();
        magic!.ProcChance.Should().BeApproximately(0.27, 0.0001);
        magic.ProcAmount.Should().BeApproximately(2.50, 0.0001);
        magic.OffCap.Should().BeTrue();
        magic.GemPrice.Should().Be(0);
    }

    [Fact]
    public void Blessing_LoadsWithLockedNumbers()
    {
        var apiContentRoot = FindApiContentRoot();
        var magic = new MagicDefinitionProvider(apiContentRoot).GetById("magic_blessing_of_the_ancients");

        magic.Should().NotBeNull();
        magic!.ProcChance.Should().BeApproximately(0.15, 0.0001);
        magic.ProcAmount.Should().BeApproximately(4.25, 0.0001);
        magic.OffCap.Should().BeTrue();
        magic.GemPrice.Should().Be(0);
    }

    // ── League resolution ──────────────────────────────────────────────────────

    // T76 — owner-locked DotD brackets: 1–999 / 1000–2499 / 2500–4999 / 5000+.
    [Theory]
    [InlineData(20,     GauntletLeague.Whelpling)]
    [InlineData(999,    GauntletLeague.Whelpling)]
    [InlineData(1000,   GauntletLeague.Wyrm)]
    [InlineData(2499,   GauntletLeague.Wyrm)]
    [InlineData(2500,   GauntletLeague.Dragon)]
    [InlineData(4999,   GauntletLeague.Dragon)]
    [InlineData(5000,   GauntletLeague.Ancient)]
    [InlineData(999999, GauntletLeague.Ancient)]
    public void ResolveLeague_MapsLevelToLeagueAtBandEdges(int level, GauntletLeague expected)
        => Build().ResolveLeague(level).Should().Be(expected);

    // ── Trophy validation ──────────────────────────────────────────────────────

    [Fact]
    public void DuplicateTrophyId_Throws()
    {
        WriteTrophies("""
        [
          { "id": "trophy_aureate", "name": "A", "tier": "Aureate", "legionPowerBonusFraction": 0.25 },
          { "id": "trophy_aureate", "name": "A2", "tier": "Argent", "legionPowerBonusFraction": 0.10 },
          { "id": "trophy_bronzed", "name": "B", "tier": "Bronzed", "legionPowerBonusFraction": 0.05 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate id*trophy_aureate*");
    }

    [Fact]
    public void TrophyWithZeroBonusFraction_Throws()
    {
        WriteTrophies("""
        [
          { "id": "trophy_aureate", "name": "A", "tier": "Aureate", "legionPowerBonusFraction": 0.25 },
          { "id": "trophy_argent",  "name": "B", "tier": "Argent",  "legionPowerBonusFraction": 0.10 },
          { "id": "trophy_bronzed", "name": "C", "tier": "Bronzed", "legionPowerBonusFraction": 0.0 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*legionPowerBonusFraction*must be > 0*");
    }

    // ── Prize-band validation ──────────────────────────────────────────────────

    [Fact]
    public void OverlappingPrizeBands_Throws()
    {
        // Band 2 starts at 5 while band 1 covers 1..10 — overlap.
        WritePrizes("""
        {
          "bands": [
            { "rankFrom": 1, "rankTo": 10,  "tokens": 50, "trophyId": "trophy_aureate", "magicId": "magic_wrath_of_the_ancients" },
            { "rankFrom": 5, "rankTo": 500, "tokens": 5,  "trophyId": "trophy_bronzed" }
          ]
        }
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*contiguous*no gap/overlap*");
    }

    [Fact]
    public void GapInPrizeBands_Throws()
    {
        // Jumps from 1..10 to 12..500 — rank 11 is uncovered.
        WritePrizes("""
        {
          "bands": [
            { "rankFrom": 1,  "rankTo": 10,  "tokens": 50, "trophyId": "trophy_aureate", "magicId": "magic_wrath_of_the_ancients" },
            { "rankFrom": 12, "rankTo": 500, "tokens": 5,  "trophyId": "trophy_bronzed" }
          ]
        }
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*contiguous*expected*11*");
    }

    [Fact]
    public void PrizeBandsNotCoveringFullRange_Throws()
    {
        // Contiguous from 1 but stops at 200, not 500 (PrizeRankCount).
        WritePrizes("""
        {
          "bands": [
            { "rankFrom": 1,   "rankTo": 200, "tokens": 50 }
          ]
        }
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*cover exactly 1..500*");
    }

    [Fact]
    public void PrizeBandWithMissingTrophyId_Throws()
    {
        WritePrizes("""
        {
          "bands": [
            { "rankFrom": 1,   "rankTo": 500, "tokens": 50, "trophyId": "trophy_does_not_exist" }
          ]
        }
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*trophyId 'trophy_does_not_exist'*not defined*");
    }

    [Fact]
    public void PrizeBandWithMissingMagicId_Throws()
    {
        WritePrizes("""
        {
          "bands": [
            { "rankFrom": 1, "rankTo": 1,   "tokens": 50, "magicId": "magic_does_not_exist", "trophyId": "trophy_aureate" },
            { "rankFrom": 2, "rankTo": 500, "tokens": 5,  "trophyId": "trophy_bronzed" }
          ]
        }
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*magicId 'magic_does_not_exist'*not defined*");
    }

    // ── Gauntlet-magic validation ──────────────────────────────────────────────

    [Fact]
    public void GauntletMagicMissingOffCap_Throws()
    {
        // Same content but Wrath has offCap omitted (defaults false).
        WriteMagics(MagicsJson(wrathOffCap: false));

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*magic_wrath_of_the_ancients*offCap*");
    }

    [Fact]
    public void MissingGauntletMagic_Throws()
    {
        // magics.json without the Wrath/Blessing rows at all.
        WriteMagics("""
        [
          { "id": "magic_smite", "name": "Smite", "rarity": "Blue", "category": "Damage",
            "effectType": "DamageProc", "procChance": 0.10, "procAmount": 0.60 }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*required Gauntlet magic*missing*");
    }

    // ── Naming guard ───────────────────────────────────────────────────────────

    [Fact]
    public void NamingGuard_FiresWhenSmiteIsRepurposedToAGauntletId()
    {
        // Simulate a collision: a mocked magic provider where 'magic_smite' resolves to a
        // definition NAMED like the Gauntlet aura (i.e. the id was repurposed).
        var magic = new Mock<IMagicDefinitionProvider>();
        magic.Setup(m => m.GetById("magic_wrath_of_the_ancients"))
            .Returns(new MagicDefinition { Id = "magic_wrath_of_the_ancients", ProcChance = 0.27, ProcAmount = 2.50, OffCap = true });
        magic.Setup(m => m.GetById("magic_blessing_of_the_ancients"))
            .Returns(new MagicDefinition { Id = "magic_blessing_of_the_ancients", ProcChance = 0.15, ProcAmount = 4.25, OffCap = true });
        // Collision: magic_smite now resolves to something NOT named "Smite".
        magic.Setup(m => m.GetById("magic_smite"))
            .Returns(new MagicDefinition { Id = "magic_smite", Name = "Wrath of the Ancients" });
        magic.Setup(m => m.GetById("magic_blessing_of_might"))
            .Returns(new MagicDefinition { Id = "magic_blessing_of_might", Name = "Blessing of Might" });

        var act = () => BuildWithMagic(magic.Object);
        act.Should().Throw<InvalidOperationException>().WithMessage("*naming guard*magic_smite*");
    }

    // ── Raid-ladder validation ──────────────────────────────────────────────────

    [Fact]
    public void NonContiguousLadderStage_Throws()
    {
        // Stages 1 and 3 — stage 2 is missing.
        WriteRaids("""
        [
          { "id": "gauntlet_stage_1", "name": "S1", "tier": "Event", "ladderStage": 1, "baseHp": 5000,  "gauntletScored": true },
          { "id": "gauntlet_stage_3", "name": "S3", "tier": "Event", "ladderStage": 3, "baseHp": 12500, "gauntletScored": true }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*contiguous*expected 2*");
    }

    [Fact]
    public void NonRisingLadderHp_Throws()
    {
        WriteRaids("""
        [
          { "id": "gauntlet_stage_1", "name": "S1", "tier": "Event", "ladderStage": 1, "baseHp": 12500, "gauntletScored": true },
          { "id": "gauntlet_stage_2", "name": "S2", "tier": "Event", "ladderStage": 2, "baseHp": 5000,  "gauntletScored": true }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*baseHp must strictly rise*");
    }

    [Fact]
    public void RaidNotGauntletScored_Throws()
    {
        WriteRaids("""
        [
          { "id": "gauntlet_stage_1", "name": "S1", "tier": "Event", "ladderStage": 1, "baseHp": 5000, "gauntletScored": false }
        ]
        """);

        var act = () => Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*gauntletScored = true*");
    }

    // ── League-bounds validation (config) ────────────────────────────────────────

    [Fact]
    public void LeagueBoundsWithGap_Throws()
    {
        var cfg = new GauntletConfig
        {
            LeagueBounds = new()
            {
                ["Whelpling"] = new LeagueBound { Min = 1,    Max = 1999 },
                // Gap: 2000 is uncovered.
                ["Wyrm"]      = new LeagueBound { Min = 2001, Max = 2499 },
                ["Dragon"]    = new LeagueBound { Min = 2500, Max = 4999 },
                ["Ancient"]   = new LeagueBound { Min = 5000, Max = GauntletConfig.NoMaxLevel },
            },
        };

        var act = () => Build(Options.Create(cfg));
        act.Should().Throw<InvalidOperationException>().WithMessage("*LeagueBounds*contiguous*2000*");
    }

    [Fact]
    public void LeagueBoundsTopNotOpenEnded_Throws()
    {
        var cfg = new GauntletConfig
        {
            LeagueBounds = new()
            {
                ["Whelpling"] = new LeagueBound { Min = 1,    Max = 999 },
                ["Wyrm"]      = new LeagueBound { Min = 1000, Max = 2499 },
                ["Dragon"]    = new LeagueBound { Min = 2500, Max = 4999 },
                // Ancient capped at 5000 instead of the no-max sentinel.
                ["Ancient"]   = new LeagueBound { Min = 5000, Max = 5000 },
            },
        };

        var act = () => Build(Options.Create(cfg));
        act.Should().Throw<InvalidOperationException>().WithMessage("*top league must*extend to the no-max sentinel*");
    }

    // ── Static valid-content fixtures ────────────────────────────────────────────

    private const string ValidPrizesJson = """
    {
      "bands": [
        { "rankFrom": 1,   "rankTo": 1,   "tokens": 50, "pitchfork": 10, "trophyId": "trophy_aureate", "magicId": "magic_wrath_of_the_ancients" },
        { "rankFrom": 2,   "rankTo": 10,  "tokens": 25, "pitchfork": 5,  "trophyId": "trophy_argent",  "magicId": "magic_blessing_of_the_ancients" },
        { "rankFrom": 11,  "rankTo": 50,  "tokens": 20, "pitchfork": 2 },
        { "rankFrom": 51,  "rankTo": 100, "tokens": 15, "pitchfork": 1 },
        { "rankFrom": 101, "rankTo": 200, "tokens": 10 },
        { "rankFrom": 201, "rankTo": 499, "tokens": 5 },
        { "rankFrom": 500, "rankTo": 500, "tokens": 5, "trophyId": "trophy_bronzed" }
      ]
    }
    """;

    private const string ValidTrophiesJson = """
    [
      { "id": "trophy_aureate", "name": "Aureate Trophy", "tier": "Aureate", "legionPowerBonusFraction": 0.25 },
      { "id": "trophy_argent",  "name": "Argent Trophy",  "tier": "Argent",  "legionPowerBonusFraction": 0.10 },
      { "id": "trophy_bronzed", "name": "Bronzed Trophy", "tier": "Bronzed", "legionPowerBonusFraction": 0.05 }
    ]
    """;

    private const string ValidRaidsJson = """
    [
      { "id": "gauntlet_stage_1", "name": "S1", "tier": "Event", "ladderStage": 1, "baseHp": 5000,  "gauntletScored": true },
      { "id": "gauntlet_stage_2", "name": "S2", "tier": "Event", "ladderStage": 2, "baseHp": 12500, "gauntletScored": true }
    ]
    """;

    private static readonly string ValidMagicsJson = MagicsJson(wrathOffCap: true);

    // The two Gauntlet magics + the two ordinary magics the naming guard checks against.
    private static string MagicsJson(bool wrathOffCap) => $$"""
    [
      { "id": "magic_smite", "name": "Smite", "rarity": "Blue", "category": "Damage",
        "effectType": "DamageProc", "procChance": 0.10, "procAmount": 0.60 },
      { "id": "magic_blessing_of_might", "name": "Blessing of Might", "rarity": "Blue", "category": "Damage",
        "effectType": "DamageProc", "procChance": 0.08, "procAmount": 0.80 },
      { "id": "magic_wrath_of_the_ancients", "name": "Wrath of the Ancients", "rarity": "Orange", "category": "Damage",
        "effectType": "DamageProc", "procChance": 0.27, "procAmount": 2.50, "offCap": {{(wrathOffCap ? "true" : "false")}}, "stacks": false, "gemPrice": 0 },
      { "id": "magic_blessing_of_the_ancients", "name": "Blessing of the Ancients", "rarity": "Orange", "category": "Damage",
        "effectType": "DamageProc", "procChance": 0.15, "procAmount": 4.25, "offCap": true, "stacks": false, "gemPrice": 0 }
    ]
    """;

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ROTA.Api");
            if (Directory.Exists(Path.Combine(candidate, "content")))
                return candidate;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
