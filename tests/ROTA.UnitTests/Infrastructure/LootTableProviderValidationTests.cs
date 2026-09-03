using System.Text.Json;
using FluentAssertions;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Infrastructure;

// Startup validation of the shipped content pack.
//
// LootTableProvider already validated loot tables AGAINST raids (onHitDrops, stat-point tiers). Nothing
// validated raids against loot tables, so a typo in a raid's lootTableId failed silently: GetById
// returns null, the loot pass short-circuits, and the raid pays gold and XP only — outwardly identical
// to a raid designed to carry no loot. These tests drive the real content pack rather than a synthetic
// fixture, so they also fail if the shipped JSON itself ever breaks.
public class LootTableProviderValidationTests : IDisposable
{
    private readonly string _root;

    public LootTableProviderValidationTests()
    {
        // A throwaway copy of the real content pack, so a test can mutate one field without touching
        // the file the game ships.
        _root = Path.Combine(Path.GetTempPath(), "rota_content_" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(FindRepoRoot(), "src", "ROTA.Api", "content");
        var dst = Path.Combine(_root, "content");
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src, "*.json"))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return dir.FullName;
    }

    private string RaidsPath => Path.Combine(_root, "content", "raids.json");

    private void RewriteFirstRaidLootTableId(string value)
    {
        var raids = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(
            File.ReadAllText(RaidsPath))!;
        raids[0]["lootTableId"] = JsonSerializer.SerializeToElement(value);
        File.WriteAllText(RaidsPath, JsonSerializer.Serialize(raids));
    }

    private LootTableProvider BuildProviders()
    {
        var raids = new RaidDefinitionProvider(_root);
        return new LootTableProvider(_root, raids);
    }

    [Fact]
    public void TheShippedContentPack_PassesValidation()
    {
        var act = () => BuildProviders();
        act.Should().NotThrow("the content the game ships must load cleanly at startup");
    }

    [Fact]
    public void ARaid_PointingAtALootTableThatDoesNotExist_IsRejectedAtStartup()
    {
        RewriteFirstRaidLootTableId("lt_raid_this_table_does_not_exist");

        var act = () => BuildProviders();

        act.Should().Throw<InvalidOperationException>(
                "a dangling lootTableId pays nothing at runtime and looks exactly like a raid with no "
                + "loot, so it has to be caught at startup instead")
            .WithMessage("*lt_raid_this_table_does_not_exist*");
    }

    [Fact]
    public void ARaid_WithAnEmptyLootTableId_IsAccepted()
    {
        // Empty is how a raid says it carries no threshold loot. It is legitimate — every Gauntlet
        // ladder stage relies on it — and must NOT be confused with a dangling reference.
        RewriteFirstRaidLootTableId("");

        var act = () => BuildProviders();
        act.Should().NotThrow();
    }
}
