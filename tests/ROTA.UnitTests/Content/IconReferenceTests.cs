using System.Text.Json;
using FluentAssertions;

namespace ROTA.UnitTests.Content;

/// <summary>
/// Every icon reference in shipped content must resolve to a file that exists on disk.
/// </summary>
/// <remarks>
/// <para>These paths went unchecked for the whole life of the project and 186 of the 362 were
/// wrong. They were wrong in a boring, systematic way — a hand-set path dropped the id's family
/// prefix, so <c>gear_conscript_helm</c> was addressed as <c>icons/gear/conscript_helm.png</c>, and
/// every recipe pointed into <c>icons/craft/</c>, a directory that has never existed.</para>
///
/// <para>Nothing caught it because nothing could. The API served no static files at all, so no
/// request was ever made for one of these paths, so no request ever 404'd. The strings were carried
/// faithfully through three layers of DTO to a client that had no art pipeline to hand them to.
/// A broken reference and a correct one were indistinguishable right up until the moment art
/// existed — which is exactly the kind of bug worth a test rather than a fix.</para>
///
/// <para>Items are checked through <c>artKey</c> instead, and many ids share one file: all four
/// difficulty tiers of a sigil summon the same raid and want the same seal. That collapse is the
/// point of artKey, so the test asserts the file exists rather than that the mapping is one-to-one.
/// </para>
/// </remarks>
public class IconReferenceTests
{
    private static readonly (string File, string Family)[] IconPathFiles =
    {
        ("gear.json", "gear"), ("magics.json", "magic"), ("units.json", "unit"),
        ("legions.json", "legion"), ("recipes.json", "recipe"),
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return dir.FullName;
    }

    private static JsonDocument Content(string file) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "ROTA.Api", "content", file)));

    [Fact]
    public void Every_icon_path_resolves_to_a_file_on_disk()
    {
        var assets = Path.Combine(FindRepoRoot(), "assets");
        var broken = new List<string>();
        var checkedCount = 0;

        foreach (var (file, _) in IconPathFiles)
        {
            using var doc = Content(file);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.GetProperty("id").GetString() ?? "?";
                var path = e.TryGetProperty("iconPath", out var p) ? p.GetString() ?? "" : "";
                checkedCount++;
                if (path.Length == 0)
                    broken.Add($"{file}: {id} has no iconPath");
                else if (!File.Exists(Path.Combine(assets, path.Replace('/', Path.DirectorySeparatorChar))))
                    broken.Add($"{file}: {id} -> {path}");
            }
        }

        checkedCount.Should().BeGreaterThan(200, "the content catalogue should not have shrunk");
        broken.Should().BeEmpty(
            "a DTO that names a picture the server cannot serve renders as a blank square");
    }

    [Fact]
    public void Every_item_art_key_resolves_to_a_file_on_disk()
    {
        var itemIcons = Path.Combine(FindRepoRoot(), "assets", "icons", "item");
        var broken = new List<string>();

        using var doc = Content("items.json");
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = e.GetProperty("id").GetString() ?? "?";
            var key = e.TryGetProperty("artKey", out var k) ? k.GetString() ?? "" : "";
            if (key.Length == 0) key = id;
            if (!File.Exists(Path.Combine(itemIcons, key + ".png")))
                broken.Add($"{id} -> artKey={key}");
        }

        broken.Should().BeEmpty("an item whose artKey names no file draws nothing in the inventory");
    }

    /// <summary>
    /// The atlas is what a web build fetches instead of 362 separate files, so a frame missing from
    /// it is an icon the WebGL client cannot draw even though the loose PNG is right there.
    /// </summary>
    [Fact]
    public void The_atlas_carries_a_frame_for_every_icon_on_disk()
    {
        var icons = new DirectoryInfo(Path.Combine(FindRepoRoot(), "assets", "icons"));
        var atlasPath = Path.Combine(icons.FullName, "atlas.json");
        File.Exists(atlasPath).Should().BeTrue("the atlas is built by tools/art/build_atlas.py");

        using var atlas = JsonDocument.Parse(File.ReadAllText(atlasPath));
        var frames = atlas.RootElement.GetProperty("frames");

        var missing = icons.GetDirectories()
            .SelectMany(d => d.GetFiles("*.png"))
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .Where(stem => !frames.TryGetProperty(stem, out _))
            .OrderBy(s => s)
            .ToList();

        missing.Should().BeEmpty("rebuild it with tools/art/build_atlas.py after adding icons");
    }
}
