using System.Text.Json;
using FluentAssertions;

namespace ROTA.UnitTests.Infrastructure;

/// <summary>
/// R7b — pins which curve the shipped quest drops land on.
///
/// A quest chance-drop is scaled one of two ways, and the `rareScaling` flag is the only thing that
/// chooses between them:
///
///     unflagged   base x (1 + 0.03 x Discernment), clamped at MaxDropChance 0.95
///     flagged     base + 0.045 x d/(d + 111,111), d clamped at 10,000,000
///
/// The unflagged curve saturates as a function of the drop's OWN base rate, and it does so absurdly
/// early: solving base x (1 + 0.03D) >= 0.95 gives
///
///     base 0.5    -> D =     30
///     base 0.1    -> D =    283
///     base 0.01   -> D =  3,133
///     base 0.0005 -> D = 63,300
///
/// A 50%-base drop stops responding to Discernment at THIRTY points. So on the quest path the flag is
/// not a tuning preference, it is the difference between a stat that keeps working and one that is
/// finished before the tutorial is.
///
/// Today the shipped pack is at 100% coverage — 832 of 832 — and nothing keeps it there. A content
/// pass that adds one unflagged chance drop lands it on the dead curve silently: it still drops, it
/// simply stops caring about Discernment almost immediately, which is invisible until someone does
/// this arithmetic again. Audited in docs/eval/DROP_CURVE_COVERAGE_AUDIT.md.
///
/// This is a guard, not a decision. If an unflagged quest drop is ever WANTED — a drop deliberately
/// meant to reach its ceiling early — this test is where that choice gets made explicitly instead of
/// by omission.
/// </summary>
public class DropCurveCoverageTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return dir.FullName;
    }

    private static JsonDocument LootTables()
        => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "ROTA.Api", "content", "loot_tables.json")));

    /// <summary>
    /// Walks the quest-side chance entries. A quest difficulty block is one WITHOUT thresholdRewards;
    /// the raid path reads a different set of fields and does not consult this flag at all.
    /// </summary>
    private static IEnumerable<(string Table, string Difficulty, string Kind, string Id, bool Flagged)>
        QuestChanceEntries(JsonDocument doc)
    {
        foreach (var table in doc.RootElement.EnumerateArray())
        {
            var tableId = table.GetProperty("id").GetString() ?? "?";
            if (!table.TryGetProperty("difficulties", out var diffs)
                || diffs.ValueKind != JsonValueKind.Object) continue;

            foreach (var diff in diffs.EnumerateObject())
            {
                if (diff.Value.TryGetProperty("thresholdRewards", out var tr)
                    && tr.ValueKind == JsonValueKind.Array) continue;   // raid table

                foreach (var (prop, kind, idProp) in new[]
                         {
                             ("chanceDrops", "chanceItem", "itemId"),
                             ("gearDrops",   "gear",       "gearDefinitionId"),
                         })
                {
                    if (!diff.Value.TryGetProperty(prop, out var arr)
                        || arr.ValueKind != JsonValueKind.Array) continue;

                    foreach (var entry in arr.EnumerateArray())
                    {
                        var id = entry.TryGetProperty(idProp, out var idv) ? idv.GetString() ?? "?" : "?";
                        var flagged = entry.TryGetProperty("rareScaling", out var rs)
                                      && rs.ValueKind == JsonValueKind.True;
                        yield return (tableId, diff.Name, kind, id, flagged);
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryQuestChanceDrop_UsesTheAsymptoticCurve()
    {
        using var doc = LootTables();
        var unflagged = QuestChanceEntries(doc).Where(e => !e.Flagged).ToList();

        unflagged.Should().BeEmpty(
            "an unflagged quest drop rides base x (1 + 0.03D) clamped at 0.95, which for a 1% base is "
            + "finished at 3,133 Discernment and for a 50% base at 30. Offenders: "
            + string.Join(", ", unflagged.Take(10).Select(e => $"{e.Table}/{e.Difficulty}/{e.Id}")));
    }

    [Fact]
    public void TheQuestDropPopulation_IsNotAccidentallyEmpty()
    {
        // The guard above passes trivially if the walk finds nothing — a rename of chanceDrops or
        // gearDrops would silently turn it into a no-op. This is the canary for that.
        using var doc = LootTables();
        var all = QuestChanceEntries(doc).ToList();

        all.Should().HaveCountGreaterThan(500,
            "the shipped pack carried 832 quest chance/gear entries when this was written; a sudden "
            + "collapse means the walk stopped matching the content shape, not that the content shrank");
        all.Select(e => e.Kind).Distinct().Should().Contain(new[] { "chanceItem", "gear" });
    }

    [Fact]
    public void TheUnflaggedCurve_SaturatesWhereTheAuditSaysItDoes()
    {
        // The arithmetic the guard exists to protect against, asserted rather than asserted-about, so
        // that retuning DiscernmentDropMultiplier or MaxDropChance moves this test too.
        var cfg = new ROTA.Application.Configuration.QuestConfig();

        double SaturationD(double baseChance)
            => (cfg.MaxDropChance / baseChance - 1) / cfg.DiscernmentDropMultiplier;

        SaturationD(0.5).Should().BeApproximately(30, 1);
        SaturationD(0.01).Should().BeApproximately(3_133, 1);
        SaturationD(0.0005).Should().BeApproximately(63_300, 1);
    }
}
