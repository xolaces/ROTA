using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class SocialServiceTests
{
    private static (SocialService service,
                    Mock<IPlayerRepository> players,
                    Mock<IFriendshipRepository> friends,
                    Mock<IBlockRepository> blocks,
                    Mock<IPrivateMessageRepository> messages,
                    Mock<IAuditLogRepository> audit,
                    Mock<IEmailNotificationService> emails,
                    Mock<ISubmissionRateLimiter> limiter)
        Build()
    {
        var players = new Mock<IPlayerRepository>();
        var friends = new Mock<IFriendshipRepository>();
        var blocks = new Mock<IBlockRepository>();
        var messages = new Mock<IPrivateMessageRepository>();
        var audit = new Mock<IAuditLogRepository>();
        var emails = new Mock<IEmailNotificationService>();
        var limiter = new Mock<ISubmissionRateLimiter>();
        limiter.Setup(l => l.TryConsumeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new SocialService(players.Object, friends.Object, blocks.Object,
            messages.Object, audit.Object, emails.Object, limiter.Object);
        return (service, players, friends, blocks, messages, audit, emails, limiter);
    }

    private static Player MakePlayer(string username) => Player.Create(username, $"{username}@rota.test", "hash");

    // ---- friends ----

    [Fact]
    public async Task SendFriendRequest_Success_CreatesPending()
    {
        var (service, players, friends, blocks, _, _, _, _) = Build();
        var requester = Guid.NewGuid();
        var target = MakePlayer("bob");
        players.Setup(p => p.FindByUsernameAsync("bob", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        blocks.Setup(b => b.EitherBlockedAsync(requester, target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        friends.Setup(f => f.FindBetweenAsync(requester, target.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Friendship?)null);

        var result = await service.SendFriendRequestAsync(requester, "bob");

        result.Success.Should().BeTrue();
        friends.Verify(f => f.AddAsync(It.Is<Friendship>(x => x.Status == FriendshipStatus.Pending), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendFriendRequest_Self_Fails()
    {
        var (service, players, _, _, _, _, _, _) = Build();
        var me = MakePlayer("me");
        players.Setup(p => p.FindByUsernameAsync("me", It.IsAny<CancellationToken>())).ReturnsAsync(me);

        var result = await service.SendFriendRequestAsync(me.Id, "me");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("yourself");
    }

    [Fact]
    public async Task SendFriendRequest_WhenBlocked_Fails()
    {
        var (service, players, _, blocks, _, _, _, _) = Build();
        var requester = Guid.NewGuid();
        var target = MakePlayer("bob");
        players.Setup(p => p.FindByUsernameAsync("bob", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        blocks.Setup(b => b.EitherBlockedAsync(requester, target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await service.SendFriendRequestAsync(requester, "bob");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("block");
    }

    [Fact]
    public async Task AcceptFriendRequest_OnlyAddresseeCanAccept()
    {
        var (service, _, friends, _, _, _, _, _) = Build();
        var requester = Guid.NewGuid();
        var addressee = Guid.NewGuid();
        var f = Friendship.Create(requester, addressee);
        friends.Setup(r => r.GetByIdAsync(f.Id, It.IsAny<CancellationToken>())).ReturnsAsync(f);

        // Requester tries to accept their own request → rejected.
        var byRequester = await service.AcceptFriendRequestAsync(requester, f.Id);
        byRequester.Success.Should().BeFalse();

        // Addressee accepts → ok.
        var byAddressee = await service.AcceptFriendRequestAsync(addressee, f.Id);
        byAddressee.Success.Should().BeTrue();
        f.Status.Should().Be(FriendshipStatus.Accepted);
    }

    // ---- blocks ----

    [Fact]
    public async Task Block_Self_Fails()
    {
        var (service, players, _, _, _, _, _, _) = Build();
        var me = MakePlayer("me");
        players.Setup(p => p.FindByUsernameAsync("me", It.IsAny<CancellationToken>())).ReturnsAsync(me);

        var result = await service.BlockAsync(me.Id, "me");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("yourself");
    }

    // ---- private messages ----

    [Fact]
    public async Task SendMessage_NotFriends_Fails()
    {
        var (service, players, friends, blocks, messages, _, _, _) = Build();
        var sender = Guid.NewGuid();
        var target = MakePlayer("bob");
        players.Setup(p => p.FindByUsernameAsync("bob", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        blocks.Setup(b => b.EitherBlockedAsync(sender, target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        friends.Setup(f => f.FindBetweenAsync(sender, target.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Friendship?)null);

        var result = await service.SendMessageAsync(sender, "bob", "hi");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("friends");
        messages.Verify(m => m.AddAsync(It.IsAny<PrivateMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessage_BetweenFriends_PersistsAndReturnsMessage()
    {
        var (service, players, friends, blocks, messages, _, _, _) = Build();
        var sender = Guid.NewGuid();
        var target = MakePlayer("bob");
        var friendship = Friendship.Create(sender, target.Id);
        friendship.Accept();
        players.Setup(p => p.FindByUsernameAsync("bob", It.IsAny<CancellationToken>())).ReturnsAsync(target);
        blocks.Setup(b => b.EitherBlockedAsync(sender, target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        friends.Setup(f => f.FindBetweenAsync(sender, target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(friendship);

        var result = await service.SendMessageAsync(sender, "bob", "hi friend");

        result.Success.Should().BeTrue();
        result.Message.Should().NotBeNull();
        result.Message!.Body.Should().Be("hi friend");
        messages.Verify(m => m.AddAsync(It.IsAny<PrivateMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- report ----

    [Fact]
    public async Task ReportPlayer_Success_QueuesPlayerReportEmail()
    {
        var (service, players, _, _, _, _, emails, _) = Build();
        var reporter = Guid.NewGuid();
        var target = MakePlayer("bad");
        players.Setup(p => p.FindByUsernameAsync("bad", It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await service.ReportPlayerAsync(reporter, "bad", "cheating", "saw them fly", "1.2.3.4");

        result.Success.Should().BeTrue();
        emails.Verify(e => e.QueueAsync(
            It.Is<EmailPayload>(p => p.Type == EmailType.PlayerReport && p.TriggeringSystem == "T37"),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportPlayer_RateLimited_Fails_NoEmail()
    {
        var (service, players, _, _, _, _, emails, limiter) = Build();
        limiter.Setup(l => l.TryConsumeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await service.ReportPlayerAsync(Guid.NewGuid(), "bad", "x", null, null);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Too many");
        emails.Verify(e => e.QueueAsync(It.IsAny<EmailPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        players.Verify(p => p.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "rate-limit is checked before any work");
    }
}
