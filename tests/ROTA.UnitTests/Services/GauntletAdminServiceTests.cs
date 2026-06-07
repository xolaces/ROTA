using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

// BETA (System 16 Slice 2) — unit tests for GauntletAdminService: open (≤1 active enforced),
// close (guard Active), settle (guard Closed; idempotent no-op if already Settled).
public class GauntletAdminServiceTests
{
    private sealed class Bundle
    {
        public Mock<IGauntletEventRepository> Events = new();
        public Mock<IAuditLogRepository> Audit = new();
        public GauntletAdminService Build() => new(Events.Object, Audit.Object);
    }

    private static GauntletEvent EventInState(GauntletEventState state)
    {
        var ev = GauntletEvent.Create("Cycle",
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7));
        if (state >= GauntletEventState.Active)  ev.Activate();
        if (state >= GauntletEventState.Closed)  ev.Close();
        if (state >= GauntletEventState.Settled) ev.MarkSettled();
        return ev;
    }

    // ── Open: ≤1 active ────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_Succeeds_WhenNoActiveEvent()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);
        b.Events.Setup(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEvent e, CancellationToken _) => e);

        var result = await b.Build().OpenEventAsync(
            "Cycle 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        result.Success.Should().BeTrue();
        result.Event!.State.Should().Be(GauntletEventState.Active.ToString());
        b.Events.Verify(r => r.CreateAsync(
            It.Is<GauntletEvent>(e => e.State == GauntletEventState.Active), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Open_Fails_WhenAnActiveEventAlreadyExists()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EventInState(GauntletEventState.Active));

        var result = await b.Build().OpenEventAsync(
            "Cycle 2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("already exists");
        b.Events.Verify(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Open_Fails_WhenEndsAtNotAfterStartsAt()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);
        var now = DateTimeOffset.UtcNow;

        var result = await b.Build().OpenEventAsync("Bad", now, now); // equal → invalid

        result.Success.Should().BeFalse();
        b.Events.Verify(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Close: guard Active ─────────────────────────────────────────────────────

    [Fact]
    public async Task Close_Succeeds_FromActive()
    {
        var b = new Bundle();
        var ev = EventInState(GauntletEventState.Active);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().CloseEventAsync(ev.Id);

        result.Success.Should().BeTrue();
        result.Event!.State.Should().Be(GauntletEventState.Closed.ToString());
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(GauntletEventState.Scheduled)]
    [InlineData(GauntletEventState.Closed)]
    [InlineData(GauntletEventState.Settled)]
    public async Task Close_Fails_WhenNotActive(GauntletEventState state)
    {
        var b = new Bundle();
        var ev = EventInState(state);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().CloseEventAsync(ev.Id);

        result.Success.Should().BeFalse();
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Close_Fails_WhenEventNotFound()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);

        var result = await b.Build().CloseEventAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    // ── Settle: guard Closed + idempotent ───────────────────────────────────────

    [Fact]
    public async Task Settle_Succeeds_FromClosed_TransitionsToSettled()
    {
        var b = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Success.Should().BeTrue();
        result.Event!.State.Should().Be(GauntletEventState.Settled.ToString());
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_IsNoOp_WhenAlreadySettled()
    {
        var b = new Bundle();
        var ev = EventInState(GauntletEventState.Settled);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Success.Should().BeTrue("re-settle on an already-settled event is an idempotent no-op");
        result.Event!.State.Should().Be(GauntletEventState.Settled.ToString());
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never,
            "no state change is persisted when already settled");
    }

    [Theory]
    [InlineData(GauntletEventState.Scheduled)]
    [InlineData(GauntletEventState.Active)]
    public async Task Settle_Fails_WhenNotClosedAndNotSettled(GauntletEventState state)
    {
        var b = new Bundle();
        var ev = EventInState(state);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Success.Should().BeFalse();
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
