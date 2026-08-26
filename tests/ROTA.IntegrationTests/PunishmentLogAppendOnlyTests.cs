using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// Northstar §6 is binding: "Every punishment, by any role, against any player, is logged -- actor,
// role, target, type, reason, duration/expiry, timestamp. Append-only, like the audit log.
// Non-negotiable."
//
// "Like the audit log" is taken literally, so the same two layers are proven here, because each
// covers what the other cannot:
//   * RotaDbContext.SaveChanges  - catches EF-based tampering with a readable error, in-process.
//   * a database trigger         - catches everything that never touches EF, including psql and
//                                  TRUNCATE (which does not fire row-level DELETE triggers at all).
//
// The lookup tests matter as much as the tamper tests. The §6 reversal gates ("a moderator may not
// lift an admin-placed mute") are only as good as FindActivePunishmentAsync, and its correctness is
// an ORDERING question that an in-memory mock cannot honestly answer.
//
// HOW TO VERIFY THESE TESTS CATCH THE BUG: delete the PunishmentLog loop from GuardAppendOnlyTables
// and the EF tests fail; drop the triggers and the raw-SQL ones do.
public class PunishmentLogAppendOnlyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer      _redis    = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_punishmentlog_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();

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

    private async Task<long> AppendAsync(
        Guid target, PunishmentType type, string reason,
        string actorRole = "Admin", DateTimeOffset? expiresAt = null, long? reversalOf = null)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPunishmentLogRepository>();
        var entry = PunishmentLog.Create(
            Guid.NewGuid(), actorRole, target, "target-" + type, type, reason, expiresAt, reversalOf, null);
        await repo.AppendAsync(entry);
        return entry.Id;
    }

    // ---- the record itself -------------------------------------------------------------------

    [Fact]
    public async Task EveryGovernanceFieldSurvivesTheRoundTrip()
    {
        var target  = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddDays(3);
        var id = await AppendAsync(target, PunishmentType.Ban, "Botting.", "Moderator", expires);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var row = await db.PunishmentLogs.AsNoTracking().FirstAsync(p => p.Id == id);

        // §6 names these fields explicitly; a missing one is a governance failure, not a nit.
        row.ActorPlayerId.Should().NotBeNull();
        row.ActorRole.Should().Be("Moderator");
        row.TargetPlayerId.Should().Be(target);
        row.TargetUsername.Should().NotBeNullOrWhiteSpace();
        row.Type.Should().Be(PunishmentType.Ban);
        row.Reason.Should().Be("Botting.");
        row.ExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
        row.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ---- the lookup the §6 reversal gates depend on ------------------------------------------

    [Fact]
    public async Task FindActivePunishment_ReturnsTheMute_WhenNothingHasLiftedIt()
    {
        var target = Guid.NewGuid();
        var muteId = await AppendAsync(target, PunishmentType.Mute, "Spam.");

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPunishmentLogRepository>();

        var active = await repo.FindActivePunishmentAsync(target, PunishmentType.Mute);

        active.Should().NotBeNull();
        active!.Id.Should().Be(muteId);
    }

    [Fact]
    public async Task FindActivePunishment_ReturnsNull_OnceLifted()
    {
        var target = Guid.NewGuid();
        var muteId = await AppendAsync(target, PunishmentType.Mute, "Spam.");
        await AppendAsync(target, PunishmentType.Unmute, "Warned instead.", reversalOf: muteId);

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPunishmentLogRepository>();

        (await repo.FindActivePunishmentAsync(target, PunishmentType.Mute)).Should().BeNull();
    }

    [Fact]
    public async Task FindActivePunishment_FindsTheSECOND_WhenAPlayerIsPunishedClearedAndPunishedAgain()
    {
        // The naive implementation -- "the newest Mute with no Unmute anywhere after it" -- is easy to
        // write as "any Mute with no Unmute at all", which returns nothing here and would silently
        // disable the reversal gate for every repeat offender.
        var target = Guid.NewGuid();
        var first  = await AppendAsync(target, PunishmentType.Mute, "Spam.", "Moderator");
        await AppendAsync(target, PunishmentType.Unmute, "Warned.", "Moderator", reversalOf: first);
        var second = await AppendAsync(target, PunishmentType.Mute, "Spam, again.", "Admin");

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPunishmentLogRepository>();

        var active = await repo.FindActivePunishmentAsync(target, PunishmentType.Mute);

        active.Should().NotBeNull();
        active!.Id.Should().Be(second);
        active.ActorRole.Should().Be("Admin", "this is exactly what the moderator gate reads");
    }

    [Fact]
    public async Task FindActivePunishment_KeepsBansAndMutesSeparate()
    {
        var target = Guid.NewGuid();
        await AppendAsync(target, PunishmentType.Mute, "Spam.");
        var banId = await AppendAsync(target, PunishmentType.Ban, "Botting.");

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPunishmentLogRepository>();

        (await repo.FindActivePunishmentAsync(target, PunishmentType.Ban))!.Id.Should().Be(banId);
        (await repo.FindActivePunishmentAsync(target, PunishmentType.Mute)).Should().NotBeNull(
            "a later ban does not lift an existing mute");
    }

    [Fact]
    public async Task History_IsScopedToOnePlayer_NewestFirst()
    {
        var target = Guid.NewGuid();
        var other  = Guid.NewGuid();
        await AppendAsync(target, PunishmentType.Mute, "First.");
        await AppendAsync(other,  PunishmentType.Mute, "Someone else entirely.");
        var newest = await AppendAsync(target, PunishmentType.Ban, "Second.");

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPunishmentLogRepository>();

        var history = await repo.GetHistoryAsync(target);

        history.Should().HaveCount(2, "another player's record must never leak into this one");
        history[0].Id.Should().Be(newest);
    }

    // ---- append-only, layer one: EF ------------------------------------------------------------

    [Fact]
    public async Task EditingAPunishmentRow_ThroughEf_Throws_AndChangesNothing()
    {
        var id = await AppendAsync(Guid.NewGuid(), PunishmentType.Ban, "original");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var tracked = await db.PunishmentLogs.FirstAsync(p => p.Id == id);
        // PunishmentLog has no mutators, so tamper the way real drift would: through EF's metadata.
        db.Entry(tracked).Property(p => p.Reason).CurrentValue = "tampered";

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");

        using var verify = _factory.Services.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<RotaDbContext>();
        var row = await db2.PunishmentLogs.AsNoTracking().FirstAsync(p => p.Id == id);
        row.Reason.Should().Be("original", "a refused edit must not reach the database");
    }

    [Fact]
    public async Task DeletingAPunishmentRow_ThroughEf_Throws_AndChangesNothing()
    {
        var id = await AppendAsync(Guid.NewGuid(), PunishmentType.Ban, "keep me");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        db.PunishmentLogs.Remove(await db.PunishmentLogs.FirstAsync(p => p.Id == id));

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");

        using var verify = _factory.Services.CreateScope();
        var db2 = verify.ServiceProvider.GetRequiredService<RotaDbContext>();
        (await db2.PunishmentLogs.AsNoTracking().AnyAsync(p => p.Id == id)).Should().BeTrue();
    }

    // ---- append-only, layer two: the database --------------------------------------------------
    // The layer that matters for anything not written in C#: the API connects as the schema owner, so
    // without a database-level guard a psql session could rewrite a punishment record freely -- and a
    // punishment record that can be quietly edited is worthless in precisely the dispute it exists for.

    [Fact]
    public async Task RawUpdate_IsRejectedByTheDatabase()
    {
        var id = await AppendAsync(Guid.NewGuid(), PunishmentType.Ban, "original");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE punishment_log SET reason = 'tampered' WHERE id = {0}", id);

        (await act.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("append-only");

        var row = await db.PunishmentLogs.AsNoTracking().FirstAsync(p => p.Id == id);
        row.Reason.Should().Be("original");
    }

    [Fact]
    public async Task RawDelete_IsRejectedByTheDatabase()
    {
        var id = await AppendAsync(Guid.NewGuid(), PunishmentType.Ban, "keep me");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM punishment_log WHERE id = {0}", id);

        (await act.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("append-only");
        (await db.PunishmentLogs.AsNoTracking().AnyAsync(p => p.Id == id)).Should().BeTrue();
    }

    // TRUNCATE does not fire row-level DELETE triggers, so it needs its own statement-level guard.
    // Without it a player's entire moderation history could be erased in a single statement.
    [Fact]
    public async Task Truncate_IsRejectedByTheDatabase()
    {
        await AppendAsync(Guid.NewGuid(), PunishmentType.Ban, "keep me");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync("TRUNCATE punishment_log");

        (await act.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("append-only");
        (await db.PunishmentLogs.AsNoTracking().AnyAsync()).Should().BeTrue();
    }
}
