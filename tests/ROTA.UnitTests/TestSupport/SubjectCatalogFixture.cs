using ROTA.Application.Interfaces;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.TestSupport;

/// <summary>
/// Shared real <see cref="ISubjectCatalogProvider"/> built from the shipped <c>content/subjects.json</c>
/// so validator / controller / service tests exercise the same catalog as production.
/// </summary>
public static class SubjectCatalogFixture
{
    public static readonly ISubjectCatalogProvider Real = new SubjectCatalogProvider(FindApiContentRoot());

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
