namespace ROTA.IntegrationTests;

/// <summary>
/// The container images every integration test runs against, pinned in one place.
///
/// These deliberately mirror <c>docker-compose.yml</c>. Before Testcontainers 4.x the tests used the
/// parameterless builders, which meant the suite ran on whatever default image that library version
/// happened to ship -- Postgres 15 while development and production both ran 16. Behaviour that
/// differs between major Postgres versions (and this codebase leans on advisory locks, xmin
/// concurrency tokens and triggers) was therefore being verified against the wrong engine.
///
/// Keep these in step with docker-compose.yml.
/// </summary>
internal static class TestContainerImages
{
    public const string Postgres = "postgres:16-alpine";
    public const string Redis    = "redis:7-alpine";
}
