using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// T52 — verifies the priority column round-trips and the dashboard list defaults to a priority-first
// sort (High → Low, newest within a band) plus a narrowing priority filter.
public class OutboundEmailRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_email_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

        // Apply all EF migrations (includes AddEmailPriority) so the priority column + index exist.
        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new RotaDbContext(options);
    }

    private static OutboundEmail Make(EmailPriority priority, EmailType type = EmailType.GeneralTicket)
        => OutboundEmail.Create(type, $"[ROTA] {priority}", "operator@example.com",
            $"summary {priority}", triggeringPlayerId: null, triggeringSystem: "T52",
            detailJson: null, metadataJson: null, priority: priority);

    [Fact]
    public async Task DefaultSort_OrdersHighBeforeNormalBeforeLow()
    {
        await using var db = NewDbContext();
        var repo = new OutboundEmailRepository(db);

        // Insert in deliberately mixed order.
        await repo.AddAsync(Make(EmailPriority.Low));
        await repo.AddAsync(Make(EmailPriority.High));
        await repo.AddAsync(Make(EmailPriority.Normal));

        var page = await repo.ListAsync(type: null, reviewStatus: null, search: null, page: 1, pageSize: 50);

        page.Items.Should().HaveCount(3);
        page.Items.Select(e => e.Priority).Should()
            .ContainInOrder(EmailPriority.High, EmailPriority.Normal, EmailPriority.Low);
    }

    [Fact]
    public async Task PriorityFilter_NarrowsToMatchingRows()
    {
        await using var db = NewDbContext();
        var repo = new OutboundEmailRepository(db);

        await repo.AddAsync(Make(EmailPriority.High));
        await repo.AddAsync(Make(EmailPriority.High));
        await repo.AddAsync(Make(EmailPriority.Low));

        var highOnly = await repo.ListAsync(type: null, reviewStatus: null, search: null,
            page: 1, pageSize: 50, priority: EmailPriority.High);

        highOnly.Total.Should().Be(2);
        highOnly.Items.Should().OnlyContain(e => e.Priority == EmailPriority.High);
    }

    [Fact]
    public async Task CreatedSort_IgnoresPriority()
    {
        await using var db = NewDbContext();
        var repo = new OutboundEmailRepository(db);

        var low = Make(EmailPriority.Low);
        await repo.AddAsync(low);
        var high = Make(EmailPriority.High);
        await repo.AddAsync(high);

        var page = await repo.ListAsync(type: null, reviewStatus: null, search: null,
            page: 1, pageSize: 50, priority: null, sort: "created");

        // Newest-first regardless of priority: the High row was inserted last, so it comes first.
        page.Items.First().Id.Should().Be(high.Id);
    }

    // T71 ops hardening — the startup recovery sweep's query: Queued rows and retryable Failed rows
    // are owed an attempt; exhausted-Failed and Sent rows are not. (Other tests share this container,
    // so assert membership, not exact set equality.)
    [Fact]
    public async Task GetPendingSendIds_ReturnsQueuedAndRetryableFailed_Only()
    {
        const int maxAttempts = 3;
        await using var db = NewDbContext();
        var repo = new OutboundEmailRepository(db);

        var queued = Make(EmailPriority.Normal);
        var retryableFailed = Make(EmailPriority.Normal);
        retryableFailed.MarkFailed("smtp down");                       // 1 attempt < 3
        var exhausted = Make(EmailPriority.Normal);
        for (int i = 0; i < maxAttempts; i++) exhausted.MarkFailed("smtp down");
        var sent = Make(EmailPriority.Normal);
        sent.MarkSent();

        await repo.AddAsync(queued);
        await repo.AddAsync(retryableFailed);
        await repo.AddAsync(exhausted);
        await repo.AddAsync(sent);

        var pending = await repo.GetPendingSendIdsAsync(maxAttempts);

        pending.Should().Contain(queued.Id, "Queued rows were stranded by a restart");
        pending.Should().Contain(retryableFailed.Id, "Failed with attempts remaining retries");
        pending.Should().NotContain(exhausted.Id, "exhausted rows stay Failed for dashboard triage");
        pending.Should().NotContain(sent.Id);
    }
}
