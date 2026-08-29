using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

/// <summary>
/// The summon screen's content projection: what a boss is, how much health it has, and what its
/// loot table pays out per contribution bracket.
///
/// None of this reached the client before 2026-08-28 — sigils carried only the raid id, difficulty
/// and tier — so the summon screen could not say how much HP you were signing up for or what you
/// stood to win.
/// </summary>
public class RaidCatalogueServiceTests
{
    private static RaidDefinition Raid(
        string id = "raid_colossus", long baseHp = 100000, long personalHp = 500,
        string tier = "World", string lootTableId = "lt_colossus") => new()
    {
        Id = id, Name = "The Iron Colossus", Tier = tier,
        BaseHp = baseHp, PersonalBaseHp = personalHp,
        TimerHours = 48, LootTableId = lootTableId, ArtKey = "raid_colossus",
    };

    private static LootTableDefinition Table(params (string Difficulty, ThresholdReward[] Rewards)[] tiers)
    {
        var d = new Dictionary<string, LootTableDifficulty>();
        foreach (var (difficulty, rewards) in tiers)
            d[difficulty] = new LootTableDifficulty { ThresholdRewards = rewards.ToList() };
        return new LootTableDefinition { Id = "lt_colossus", Type = "Raid", Difficulties = d };
    }

    private static ThresholdReward Reward(double pct, int sp, params ItemDropChance[] items) => new()
    {
        ContributionPercent = pct, UnassignedStatPoints = sp, ItemDrops = items.ToList(),
    };

    private static ItemDropChance Drop(string id, int qty = 1, double chance = 1.0)
        => new() { ItemId = id, Quantity = qty, Chance = chance };

    private static (RaidCatalogueService svc,
                    Mock<IRaidDefinitionProvider> raids,
                    Mock<ILootTableProvider> loot,
                    Mock<IItemDefinitionProvider> items,
                    Mock<IMagicDefinitionProvider> magics) Build()
    {
        var raids   = new Mock<IRaidDefinitionProvider>();
        var loot    = new Mock<ILootTableProvider>();
        var items   = new Mock<IItemDefinitionProvider>();
        var magics  = new Mock<IMagicDefinitionProvider>();
        var units   = new Mock<IUnitDefinitionProvider>();
        var legions = new Mock<ILegionDefinitionProvider>();
        var gear    = new Mock<IGearDefinitionProvider>();

        var svc = new RaidCatalogueService(
            raids.Object, loot.Object, items.Object, magics.Object,
            units.Object, legions.Object, gear.Object);

        return (svc, raids, loot, items, magics);
    }

    // ── catalogue ─────────────────────────────────────────────────────────────

    [Fact]
    public void Catalogue_ProjectsEveryRaid()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetAll()).Returns(new[] { Raid("a"), Raid("b") });
        loot.Setup(l => l.GetById(It.IsAny<string>())).Returns(Table(("Normal", new[] { Reward(0.1, 1) })));

        var all = svc.GetCatalogue();

        all.Should().HaveCount(2);
        all[0].RaidDefinitionId.Should().Be("a");
        all[0].TimerHours.Should().Be(48);
        all[0].Difficulties.Should().ContainSingle().Which.Should().Be("Normal");
    }

    [Fact]
    public void PersonalHp_FallsBackToBaseHp_WhenTheDefinitionLeavesItZero()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid(personalHp: 0, baseHp: 100000));
        loot.Setup(l => l.GetById(It.IsAny<string>())).Returns(Table(("Normal", new[] { Reward(0.1, 1) })));

        var preview = svc.GetPreview("raid_colossus");

        preview!.PersonalHp.Should().Be(100000,
            "0 means 'no separate personal size', and resolving it here keeps every client from "
            + "reimplementing the fallback");
        preview.BaseHp.Should().Be(100000);
    }

    [Fact]
    public void PersonalHp_IsUsedWhenSet_BecauseASigilSummonsAPersonalRaid()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid(personalHp: 500, baseHp: 100000));
        loot.Setup(l => l.GetById(It.IsAny<string>())).Returns(Table(("Normal", new[] { Reward(0.1, 1) })));

        var preview = svc.GetPreview("raid_colossus");

        preview!.PersonalHp.Should().Be(500);
        preview.BaseHp.Should().Be(100000, "both are carried; the screen decides which it is asking about");
    }

    [Fact]
    public void UnknownRaid_IsNull_NotAnEmptyPreview()
    {
        var (svc, raids, _, _, _) = Build();
        raids.Setup(r => r.GetById(It.IsAny<string>())).Returns((RaidDefinition?)null);

        svc.GetPreview("nope").Should().BeNull();
    }

    // ── loot brackets ─────────────────────────────────────────────────────────

    [Fact]
    public void Brackets_ComeBackAscendingByContribution()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());
        // Deliberately out of order in the content.
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(
            ("Hard", new[] { Reward(20, 4), Reward(0.1, 2), Reward(5, 3) })));

        var result = svc.GetLootPreview("raid_colossus", "Hard");

        // The brackets are a ladder, and a ladder out of order is not a ladder.
        result!.Brackets.Select(b => b.ContributionPercent)
            .Should().ContainInOrder(new[] { 0.1, 5.0, 20.0 });
    }

    [Fact]
    public void Difficulty_MatchesCaseInsensitively_ButEchoesTheContentSpelling()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(("Nightmare", new[] { Reward(0.05, 5) })));

        var result = svc.GetLootPreview("raid_colossus", "nightmare");

        result.Should().NotBeNull("a query string should not have to match content casing");
        result!.Difficulty.Should().Be("Nightmare", "the response is the content's spelling, not the caller's");
    }

    [Fact]
    public void ADifficultyTheTableDoesNotDefine_IsNull()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(("Normal", new[] { Reward(0.1, 1) })));

        svc.GetLootPreview("raid_colossus", "Nightmare").Should().BeNull(
            "'no Nightmare tier' and 'drops nothing' are different answers; an empty list would "
            + "conflate them");
    }

    [Fact]
    public void DropNames_AreResolvedFromDefinitions()
    {
        var (svc, raids, loot, items, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(
            ("Normal", new[] { Reward(0.1, 1, Drop("mat_iron_shard", qty: 3, chance: 0.5)) })));
        items.Setup(i => i.GetById("mat_iron_shard"))
             .Returns(new ItemDefinition { Id = "mat_iron_shard", Name = "Iron Shard", Rarity = ItemRarity.Grey });

        var drop = svc.GetLootPreview("raid_colossus", "Normal")!.Brackets[0].Drops.Single();

        drop.Kind.Should().Be("Item");
        drop.Name.Should().Be("Iron Shard");
        drop.Rarity.Should().Be("Grey");
        drop.Quantity.Should().Be(3);
        drop.Chance.Should().Be(0.5);
    }

    [Fact]
    public void AnUnknownDropId_FallsBackToTheId_RatherThanRenderingBlank()
    {
        var (svc, raids, loot, items, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(
            ("Normal", new[] { Reward(0.1, 1, Drop("mat_ghost")) })));
        items.Setup(i => i.GetById(It.IsAny<string>())).Returns((ItemDefinition?)null);

        var drop = svc.GetLootPreview("raid_colossus", "Normal")!.Brackets[0].Drops.Single();

        drop.Name.Should().Be("mat_ghost",
            "a nameless row tells nobody anything; the id at least names the content bug");
    }

    [Fact]
    public void MagicDrops_AppearAlongsideItemDrops_InOneList()
    {
        var (svc, raids, loot, items, magics) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());

        var reward = Reward(20, 10, Drop("mat_iron_shard", 8));
        reward.MagicDrops = new List<MagicDropChance>
        {
            new() { MagicId = "magic_impending_doom", Chance = 0.05 },
        };
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(("Nightmare", new[] { reward })));

        items.Setup(i => i.GetById("mat_iron_shard"))
             .Returns(new ItemDefinition { Id = "mat_iron_shard", Name = "Iron Shard", Rarity = ItemRarity.Grey });
        magics.Setup(m => m.GetById("magic_impending_doom"))
              .Returns(new MagicDefinition { Id = "magic_impending_doom", Name = "Impending Doom", Rarity = ItemRarity.Purple });

        var drops = svc.GetLootPreview("raid_colossus", "Nightmare")!.Brackets[0].Drops;

        drops.Should().HaveCount(2, "a player reading a bracket wants everything it can pay out, "
                                    + "not items in one place and magics in another");
        drops.Should().Contain(d => d.Kind == "Magic" && d.Name == "Impending Doom" && d.Chance == 0.05);
    }

    [Fact]
    public void StatPointsRideOnTheBracket_BecauseTheyAreMostOfTheReward()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());

        var reward = Reward(20, 10);
        reward.AttackPoints = 5;
        reward.DefensePoints = 3;
        reward.DiscernmentPoints = 2;
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(("Nightmare", new[] { reward })));

        var bracket = svc.GetLootPreview("raid_colossus", "Nightmare")!.Brackets[0];

        bracket.StatPoints.Should().Be(10);
        bracket.AttackPoints.Should().Be(5);
        bracket.DefensePoints.Should().Be(3);
        bracket.DiscernmentPoints.Should().Be(2);
    }

    [Fact]
    public void ATableWithNoThresholds_IsAnEmptyLadder_NotACrash()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());
        loot.Setup(l => l.GetById("lt_colossus")).Returns(new LootTableDefinition
        {
            Id = "lt_colossus",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new LootTableDifficulty { ThresholdRewards = null },
            },
        });

        svc.GetLootPreview("raid_colossus", "Normal")!.Brackets.Should().BeEmpty();
    }

    // ── the World-raid damage ladder ──────────────────────────────────────────
    // Timer-only raids pay on ABSOLUTE damage rather than a share of the total, because there is
    // no total to take a share of until the raid ends.

    [Fact]
    public void ADamageLadder_ComesBackInRungOrder_NotFileOrder()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid(baseHp: 0, personalHp: 0));

        // Every rung leaves ContributionPercent at 0, so sorting by it would preserve file order —
        // correct only by luck. Deliberately shuffled here to prove the ladder sorts on damage.
        ThresholdReward Rung(long dmg, int sp) => new()
        {
            DamageThreshold = dmg, ContributionPercent = 0, UnassignedStatPoints = sp,
        };
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(
            ("Normal", new[] { Rung(1_000_000_000, 15), Rung(500, 1), Rung(62_750_000, 8) })));

        var result = svc.GetLootPreview("raid_colossus", "Normal");

        result!.Brackets.Select(b => b.DamageThreshold)
            .Should().ContainInOrder(new[] { 500L, 62_750_000L, 1_000_000_000L });
    }

    [Fact]
    public void APercentageTable_StillSortsOnPercentage()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid());
        loot.Setup(l => l.GetById("lt_colossus")).Returns(Table(
            ("Hard", new[] { Reward(20, 4), Reward(0.1, 2), Reward(5, 3) })));

        var result = svc.GetLootPreview("raid_colossus", "Hard");

        result!.Brackets.Select(b => b.ContributionPercent)
            .Should().ContainInOrder(new[] { 0.1, 5.0, 20.0 });
        result.Brackets.Should().OnlyContain(b => b.DamageThreshold == 0,
            "a percentage table has no damage rungs, and a client picks whichever is non-zero");
    }

    [Fact]
    public void ATimerOnlyRaid_ReportsNoHealth_SoNoClientDrawsABar()
    {
        var (svc, raids, loot, _, _) = Build();
        raids.Setup(r => r.GetById("raid_colossus")).Returns(Raid(baseHp: 0, personalHp: 0));
        loot.Setup(l => l.GetById(It.IsAny<string>())).Returns(Table(("Normal", new[] { Reward(0.1, 1) })));

        var preview = svc.GetPreview("raid_colossus");

        preview!.BaseHp.Should().Be(0);
        preview.PersonalHp.Should().Be(0,
            "the personal fallback must not resurrect health for a raid that has none");
    }
}
