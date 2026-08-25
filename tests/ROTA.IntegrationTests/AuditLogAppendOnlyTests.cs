using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// CLAUDE.md has always said audit_log is append-only. Nothing enforced it: AuditLog has no mutators
// and IAuditLogRepository exposes only AppendAsync, so the rule held purely by convention while
// RotaDbContext exposed a DbSet anyone could Remove() from.
//
// Two layers now enforce it, and both are tested here because each covers what the other cannot:
//   * RotaDbContext.SaveChanges  - catches EF-based tampering with a readable error, in-process.
//   * a database trigger         - catches everything that never touches EF, including psql and
//                                  TRUNCATE (which does not fire row-level DELETE triggers at all).
//
// HOW TO VERIFY THESE TESTS CATCH THE BUG: delete GuardAuditLogAppendOnly from RotaDbContext and the
// EF tests below fail; roll back the EnforceAuditLogAppendOnly migration and the raw-SQL ones do.
public class AuditLogAppendOnlyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_auditlog_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder().Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var rsa = RSA.Create(2048);
        var publicKeyPem  = rsa.ExportSubjectPublicKeyInfoPem();
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"]             = _redis.GetConnectionString(),
                        ["Jwt:PublicKey"]                       = publicKeyPem,
                        ["Jwt:PrivateKey"]                      = privateKeyPem,
                        ["Jwt:Issuer"]                          = "rota-test",
                        ["Jwt:Audience"]                        = "rota-test",
                        ["Seed:AdminPassword"]                  = "",
                    });
                });
            });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return Path.Combine(dir.FullName, "src", "ROTA.Api");
    }

    private async Task<long> AppendAsync(string action, string summary)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var entry = AuditLog.Create(null, action, null, summary, null);
        await repo.AppendAsync(entry);
        return entry.Id;
    }

    [Fact]
    public async Task Appending_Works_TheRuleBlocksOnlyRewrites()
    {
        var id = await AppendAsync("TestAppend", "baseline");

        id.Should().BeGreaterThan(0, "the append path must keep working");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var row = await db.AuditLogs.AsNoTracking().FirstAsync(a => a.Id == id);
        row.ResultSummary.Should().Be("baseline");
    }

    [Fact]
    public async Task EditingAnAuditRow_ThroughEf_Throws_AndChangesNothing()
    {
        var id = await AppendAsync("TestEdit", "original");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var tracked = await db.AuditLogs.FirstAsync(a => a.Id == id);
        // AuditLog has no mutators, so tamper the way real drift would: through EF's own metadata.
        db.Entry(tracked).Property(a => a.ResultSummary).CurrentValue = "tampered";

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");

        using var verify = _factory.Services.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<RotaDbContext>();
        var row = await db2.AuditLogs.AsNoTracking().FirstAsync(a => a.Id == id);
        row.ResultSummary.Should().Be("original", "a refused edit must not reach the database");
    }

    [Fact]
    public async Task DeletingAnAuditRow_ThroughEf_Throws_AndChangesNothing()
    {
        var id = await AppendAsync("TestDelete", "keep me");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        db.AuditLogs.Remove(await db.AuditLogs.FirstAsync(a => a.Id == id));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");

        using var verify = _factory.Services.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<RotaDbContext>();
        (await db2.AuditLogs.AsNoTracking().AnyAsync(a => a.Id == id)).Should().BeTrue();
    }

    // The layer that matters for anything not written in C#: the API connects as the schema owner, so
    // without a database-level guard a psql session could rewrite history freely.

    [Fact]
    public async Task RawUpdate_IsRejectedByTheDatabase()
    {
        var id = await AppendAsync("TestRawUpdate", "original");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE audit_log SET result_summary = 'tampered' WHERE id = {0}", id);

        (await act.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("append-only");

        var row = await db.AuditLogs.AsNoTracking().FirstAsync(a => a.Id == id);
        row.ResultSummary.Should().Be("original");
    }

    [Fact]
    public async Task RawDelete_IsRejectedByTheDatabase()
    {
        var id = await AppendAsync("TestRawDelete", "keep me");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM audit_log WHERE id = {0}", id);

        (await act.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("append-only");
        (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Id == id)).Should().BeTrue();
    }

    // TRUNCATE does not fire row-level DELETE triggers, so it needs its own statement-level guard.
    // Without it the entire table could be emptied in a single statement.
    [Fact]
    public async Task Truncate_IsRejectedByTheDatabase()
    {
        await AppendAsync("TestTruncate", "keep me");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync("TRUNCATE audit_log");

        (await act.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("append-only");
        (await db.AuditLogs.AsNoTracking().AnyAsync()).Should().BeTrue();
    }
}
