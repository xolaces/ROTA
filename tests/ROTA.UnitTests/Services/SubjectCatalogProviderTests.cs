using FluentAssertions;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class SubjectCatalogProviderTests : IDisposable
{
    private readonly string _tmpDir;

    public SubjectCatalogProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_subjects_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tmpDir, "content"));
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    private string ContentPath => _tmpDir;

    private void WriteJson(string json)
        => File.WriteAllText(Path.Combine(_tmpDir, "content", "subjects.json"), json);

    // -----------------------------------------------------------------------
    // Happy path — loads the shipped catalog
    // -----------------------------------------------------------------------

    [Fact]
    public void Provider_LoadsShippedSubjects()
    {
        var apiContentRoot = FindApiContentRoot();
        var provider = new SubjectCatalogProvider(apiContentRoot);

        provider.BugSubjects.Should().NotBeEmpty();
        provider.ReportSubjects.Should().NotBeEmpty();
        provider.FeedbackCategory.Should().Be("Player Feedback");

        provider.BugSubjects.Should().Contain(s => s.Key == "combat_issue" && s.Label == "Combat Issue");
        provider.ReportSubjects.Should().Contain(s => s.Key == "cheating" && s.Label == "Cheating");
    }

    [Fact]
    public void Provider_Normalizes_KeyAndLabel_ToLabel()
    {
        var provider = new SubjectCatalogProvider(FindApiContentRoot());

        provider.NormalizeBugSubject("combat_issue").Should().Be("Combat Issue");
        provider.NormalizeBugSubject("Combat Issue").Should().Be("Combat Issue");
        provider.NormalizeReportSubject("cheating").Should().Be("Cheating");
        provider.NormalizeReportSubject("Cheating").Should().Be("Cheating");

        provider.IsValidBugSubject("combat_issue").Should().BeTrue();
        provider.IsValidBugSubject("nope").Should().BeFalse();
        provider.IsValidReportSubject("harassment").Should().BeTrue();
        provider.IsValidReportSubject("nope").Should().BeFalse();
        provider.NormalizeBugSubject("nope").Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Startup validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Provider_EmptyBugList_ThrowsOnStartup()
    {
        WriteJson("""
        {
          "bugSubjects": [],
          "reportSubjects": [ { "key": "cheating", "label": "Cheating" } ],
          "feedbackCategory": "Player Feedback"
        }
        """);

        var act = () => new SubjectCatalogProvider(ContentPath);
        act.Should().Throw<InvalidOperationException>().WithMessage("*bugSubjects*empty*");
    }

    [Fact]
    public void Provider_DuplicateKey_ThrowsOnStartup()
    {
        WriteJson("""
        {
          "bugSubjects": [
            { "key": "dup", "label": "One" },
            { "key": "dup", "label": "Two" }
          ],
          "reportSubjects": [ { "key": "cheating", "label": "Cheating" } ],
          "feedbackCategory": "Player Feedback"
        }
        """);

        var act = () => new SubjectCatalogProvider(ContentPath);
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate key*dup*");
    }

    [Fact]
    public void Provider_BlankFeedbackCategory_ThrowsOnStartup()
    {
        WriteJson("""
        {
          "bugSubjects": [ { "key": "combat_issue", "label": "Combat Issue" } ],
          "reportSubjects": [ { "key": "cheating", "label": "Cheating" } ],
          "feedbackCategory": ""
        }
        """);

        var act = () => new SubjectCatalogProvider(ContentPath);
        act.Should().Throw<InvalidOperationException>().WithMessage("*feedbackCategory*blank*");
    }

    [Fact]
    public void Provider_MissingFile_ThrowsOnStartup()
    {
        var act = () => new SubjectCatalogProvider(ContentPath); // no subjects.json written
        act.Should().Throw<InvalidOperationException>().WithMessage("*subjects.json not found*");
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

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
