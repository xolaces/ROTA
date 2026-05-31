using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class MagicServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private record ServiceBundle(
        MagicService                    Service,
        Mock<IPlayerMagicRepository>    MagicRepo,
        Mock<IRaidMagicRepository>      RaidMagics,
        Mock<IActiveRaidRepository>     Raids,
        Mock<IRaidDefinitionProvider>   RaidDefs,
        Mock<IRaidParticipantRepository> Participants,
        Mock<IMagicDefinitionProvider>  Defs,
        Mock<IAuditLogRepository>       AuditLog,
        Mock<IGemService>               Gems);

    private static ServiceBundle BuildService(MagicConfig? config = null)
    {
        var magicRepo    = new Mock<IPlayerMagicRepository>();
        var raidMagics   = new Mock<IRaidMagicRepository>();
        var raids        = new Mock<IActiveRaidRepository>();
        var raidDefs     = new Mock<IRaidDefinitionProvider>();
        var participants = new Mock<IRaidParticipantRepository>();
        var defs         = new Mock<IMagicDefinitionProvider>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var gems         = new Mock<IGemService>();

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Invoke the advisory-lock delegate so unit tests exercise logic inside the lock.
        // Tests that return early (RaidNotFound, WorldGateBlocked, etc.) never call the lock.
        raids.Setup(r => r.AtomicWithAdvisoryLockAsync(
                It.IsAny<Guid>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<Task<bool>>, CancellationToken>((_, action, _) => action());

        var cfg = Options.Create(config ?? new MagicConfig());

        return new ServiceBundle(
            new MagicService(magicRepo.Object, raidMagics.Object, raids.Object,
                             raidDefs.Object, participants.Object, defs.Object,
                             auditLog.Object, gems.Object, cfg),
            magicRepo, raidMagics, raids, raidDefs, participants, defs, auditLog, gems);
    }

    private static PlayerMagic MakeOwned(Guid playerId, string defId)
        => PlayerMagic.Create(playerId, defId);

    private static MagicDefinition MakeDef(string id, string name = "Test Magic")
        => new()
        {
            Id         = id,
            Name       = name,
            Description = "desc",
            Rarity     = ItemRarity.Green,
            Category   = MagicCategory.Damage,
            EffectType = MagicEffectType.DamageProc,
            ProcChance = 0.10,
            ProcAmount = 0.5,
            Stacks     = true,
        };

    private static ActiveRaid MakeRaid(
        Guid? summonerId = null,
        RaidSize size = RaidSize.Large,
        bool defeated = false,
        DateTimeOffset? expires = null)
    {
        var sId = summonerId ?? Guid.NewGuid();
        var raid = ActiveRaid.Create(
            "raid_test", sId,
            maxHp: 100_000L,
            expiresAt: expires ?? DateTimeOffset.UtcNow.AddHours(48),
            size: size);
        if (defeated) raid.MarkDefeated();
        return raid;
    }

    private static RaidDefinition MakeRaidDef(string tier = "Standard")
        => new() { Id = "raid_test", Name = "Test Raid", Tier = tier, BaseHp = 100_000 };

    // -----------------------------------------------------------------------
    // GetOwnedMagicsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetOwnedMagicsAsync_ReturnsHydratedMagics()
    {
        var svc      = BuildService();
        var playerId = Guid.NewGuid();
        var def      = MakeDef("magic_smite", "Smite");
        var owned    = MakeOwned(playerId, def.Id);

        svc.MagicRepo.Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMagic> { owned });
        svc.Defs.Setup(d => d.GetById("magic_smite")).Returns(def);

        var result = await svc.Service.GetOwnedMagicsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].MagicDefinitionId.Should().Be("magic_smite");
        result[0].Name.Should().Be("Smite");
        result[0].EffectType.Should().Be("DamageProc");
        result[0].ProcChance.Should().BeApproximately(0.10, 0.001);
    }

    [Fact]
    public async Task GetOwnedMagicsAsync_ZeroOwned_ReturnsEmpty()
    {
        var svc = BuildService();
        var playerId = Guid.NewGuid();

        svc.MagicRepo.Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMagic>());

        var result = await svc.Service.GetOwnedMagicsAsync(playerId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOwnedMagicsAsync_MissingDefinition_SkipsRow()
    {
        var svc      = BuildService();
        var playerId = Guid.NewGuid();
        var owned    = MakeOwned(playerId, "magic_unknown");

        svc.MagicRepo.Setup(r => r.GetOwnedAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMagic> { owned });
        svc.Defs.Setup(d => d.GetById("magic_unknown")).Returns((MagicDefinition?)null);

        var result = await svc.Service.GetOwnedMagicsAsync(playerId);

        result.Should().BeEmpty("orphaned rows with no matching definition are silently skipped");
    }

    // -----------------------------------------------------------------------
    // ApplyMagicAsync — access / ownership / uniqueness guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyMagic_RaidNotFound_ReturnsFail()
    {
        var svc = BuildService();
        svc.Raids.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid?)null);

        var result = await svc.Service.ApplyMagicAsync(Guid.NewGuid(), Guid.NewGuid(), "magic_smite", false);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MagicApplyFailureCode.RaidNotFound);
    }

    [Fact]
    public async Task ApplyMagic_WorldRaid_NonAdmin_Denied()
    {
        var svc    = BuildService();
        var raid   = MakeRaid();
        var raidId = raid.Id;

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef("World"));

        var result = await svc.Service.ApplyMagicAsync(Guid.NewGuid(), raidId, "magic_smite", isAdmin: false);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MagicApplyFailureCode.WorldGateBlocked);
    }

    [Fact]
    public async Task ApplyMagic_WorldRaid_Admin_PassesWorldGate()
    {
        // Admin passes the world gate; further checks (ownership, slot) should run.
        var svc      = BuildService();
        var playerId = Guid.NewGuid();
        var raid     = MakeRaid();
        var raidId   = raid.Id;

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef("World"));
        // Not owned → next gate fires
        svc.MagicRepo.Setup(r => r.FindAsync(playerId, "magic_smite", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerMagic?)null);

        var result = await svc.Service.ApplyMagicAsync(playerId, raidId, "magic_smite", isAdmin: true);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MagicApplyFailureCode.MagicNotOwned,
            "admin passed world gate but does not own the magic");
    }

    [Fact]
    public async Task ApplyMagic_MagicNotOwned_ReturnsFail()
    {
        var svc      = BuildService();
        var playerId = Guid.NewGuid();
        var raid     = MakeRaid(summonerId: playerId);
        var raidId   = raid.Id;

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef());
        svc.MagicRepo.Setup(r => r.FindAsync(playerId, "magic_smite", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerMagic?)null);

        var result = await svc.Service.ApplyMagicAsync(playerId, raidId, "magic_smite", false);

        result.FailureCode.Should().Be(MagicApplyFailureCode.MagicNotOwned);
    }

    [Fact]
    public async Task ApplyMagic_NotParticipant_NonWorld_ReturnsFail()
    {
        var svc      = BuildService();
        var playerId = Guid.NewGuid();
        var summoner = Guid.NewGuid();
        var raid     = MakeRaid(summonerId: summoner);  // different summoner
        var raidId   = raid.Id;

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef());
        svc.MagicRepo.Setup(r => r.FindAsync(playerId, "magic_smite", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOwned(playerId, "magic_smite"));
        svc.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raidId, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);

        var result = await svc.Service.ApplyMagicAsync(playerId, raidId, "magic_smite", false);

        result.FailureCode.Should().Be(MagicApplyFailureCode.NotAParticipant);
    }

    [Fact]
    public async Task ApplyMagic_AlreadyAppliedByPlayer_NonWorld_ReturnsFail()
    {
        // One-per-player check is inside the advisory lock. The lock delegate is invoked
        // by the BuildService setup, so FindByPlayerAsync fires and returns AlreadyApplied.
        var svc      = BuildService();
        var playerId = Guid.NewGuid();
        var raid     = MakeRaid(summonerId: playerId);  // summoner = player
        var raidId   = raid.Id;
        var existing = RaidMagic.Create(raidId, "magic_smite", playerId);

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef());
        svc.MagicRepo.Setup(r => r.FindAsync(playerId, "magic_poison", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOwned(playerId, "magic_poison"));
        svc.RaidMagics.Setup(r => r.FindByPlayerAsync(raidId, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);  // player already has a magic on this raid

        var result = await svc.Service.ApplyMagicAsync(playerId, raidId, "magic_poison", false);

        result.FailureCode.Should().Be(MagicApplyFailureCode.AlreadyAppliedByPlayer);
    }

    [Fact]
    public async Task ApplyMagic_SamePlayer_OnlyFirstApplySucceeds()
    {
        // Verifies that after a successful first apply, a second apply by the same player
        // with a DIFFERENT magic is rejected by the one-per-player check inside the lock.
        // This is the logic path that closes the pre-lock race: moving FindByPlayerAsync
        // inside AtomicWithAdvisoryLockAsync prevents two concurrent calls from both
        // passing the check and inserting.
        // Integration test for this race is impractical (all raid defs are World tier, and
        // Admin-callers are exempt from one-per-player by design).
        var svc      = BuildService(new MagicConfig
        {
            SlotsBySize = new() { ["Large"] = 4 }  // plenty of slots — not the limiting factor
        });
        var playerId = Guid.NewGuid();
        var raid     = MakeRaid(summonerId: playerId, size: RaidSize.Large);
        var raidId   = raid.Id;

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef("Standard")); // non-world

        // Both magics are owned.
        svc.MagicRepo.Setup(r => r.FindAsync(playerId, "magic_whetstone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOwned(playerId, "magic_whetstone"));
        svc.MagicRepo.Setup(r => r.FindAsync(playerId, "magic_poison", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOwned(playerId, "magic_poison"));

        // Slot count and no-dup for both magics.
        svc.RaidMagics.Setup(r => r.CountForRaidAsync(raidId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        svc.RaidMagics.Setup(r => r.FindAsync(raidId, "magic_whetstone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidMagic?)null);
        svc.RaidMagics.Setup(r => r.FindAsync(raidId, "magic_poison", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidMagic?)null);
        svc.RaidMagics.Setup(r => r.CreateAsync(It.IsAny<RaidMagic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidMagic rm, CancellationToken _) => rm);

        // First call: no existing magic for this player on this raid.
        svc.RaidMagics.Setup(r => r.FindByPlayerAsync(raidId, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidMagic?)null);

        var result1 = await svc.Service.ApplyMagicAsync(playerId, raidId, "magic_whetstone", false);
        result1.Success.Should().BeTrue("first apply should succeed");

        // Simulate state: player now has magic_whetstone on this raid (as would be true post-insert).
        var applied = RaidMagic.Create(raidId, "magic_whetstone", playerId);
        svc.RaidMagics.Setup(r => r.FindByPlayerAsync(raidId, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(applied);

        // Second call (different magic, same player) — must be rejected inside the lock.
        var result2 = await svc.Service.ApplyMagicAsync(playerId, raidId, "magic_poison", false);
        result2.Success.Should().BeFalse("same player may not apply a second magic");
        result2.FailureCode.Should().Be(MagicApplyFailureCode.AlreadyAppliedByPlayer);
    }

    // -----------------------------------------------------------------------
    // Slice 6 — GrantMagicAsync and BuyMagicAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GrantMagicAsync_CallsUpsert_IdempotentOnRepeat()
    {
        var svc = BuildService();
        var playerId = Guid.NewGuid();

        svc.MagicRepo.Setup(r => r.UpsertAsync(playerId, "magic_whetstone", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await svc.Service.GrantMagicAsync(playerId, "magic_whetstone");
        await svc.Service.GrantMagicAsync(playerId, "magic_whetstone");

        svc.MagicRepo.Verify(r => r.UpsertAsync(playerId, "magic_whetstone", It.IsAny<CancellationToken>()),
            Times.Exactly(2), "UpsertAsync is always called; the idempotency lives in the repo (second call is a no-op)");
    }

    [Fact]
    public async Task BuyMagicAsync_Success_SpendsGemsAndGrantsMagic()
    {
        var svc      = BuildService();
        var playerId = Guid.NewGuid();

        svc.Defs.Setup(d => d.GetById("magic_smite")).Returns(new MagicDefinition
        {
            Id         = "magic_smite",
            Name       = "Smite",
            EffectType = MagicEffectType.DamageProc,
            GemPrice   = 25,
        });
        svc.Gems.Setup(g => g.SpendGemsAsync(playerId, 25, GemTransactionType.MagicPurchase, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        svc.MagicRepo.Setup(r => r.UpsertAsync(playerId, "magic_smite", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await svc.Service.BuyMagicAsync(playerId, "magic_smite");

        result.Success.Should().BeTrue();
        result.GemPrice.Should().Be(25);
        svc.Gems.Verify(g => g.SpendGemsAsync(playerId, 25, GemTransactionType.MagicPurchase, null, It.IsAny<CancellationToken>()), Times.Once);
        svc.MagicRepo.Verify(r => r.UpsertAsync(playerId, "magic_smite", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuyMagicAsync_InsufficientBalance_ReturnsFail()
    {
        var svc      = BuildService();
        var playerId = Guid.NewGuid();

        svc.Defs.Setup(d => d.GetById("magic_smite")).Returns(new MagicDefinition
        {
            Id       = "magic_smite",
            GemPrice = 50,
        });
        svc.Gems.Setup(g => g.SpendGemsAsync(playerId, 50, GemTransactionType.MagicPurchase, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await svc.Service.BuyMagicAsync(playerId, "magic_smite");

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(BuyMagicFailureCode.InsufficientBalance);
        svc.MagicRepo.Verify(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "magic must not be granted when gem spend fails");
    }

    [Fact]
    public async Task BuyMagicAsync_NotForSale_ReturnsFail_WhenGemPriceIsZero()
    {
        var svc      = BuildService();
        var playerId = Guid.NewGuid();

        svc.Defs.Setup(d => d.GetById("magic_impending_doom")).Returns(new MagicDefinition
        {
            Id       = "magic_impending_doom",
            GemPrice = 0,  // not in gem shop
        });

        var result = await svc.Service.BuyMagicAsync(playerId, "magic_impending_doom");

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(BuyMagicFailureCode.NotForSale);
    }

    // -----------------------------------------------------------------------
    // RemoveMagicAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveMagic_SummonerOnly_NonWorld_Success()
    {
        var svc      = BuildService();
        var summoner = Guid.NewGuid();
        var raid     = MakeRaid(summonerId: summoner);
        var raidId   = raid.Id;
        var raidMagic = RaidMagic.Create(raidId, "magic_smite", summoner);

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidMagics.Setup(r => r.FindAsync(raidId, "magic_smite", It.IsAny<CancellationToken>()))
            .ReturnsAsync(raidMagic);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef());
        svc.RaidMagics.Setup(r => r.SoftDeleteAsync(raidMagic, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await svc.Service.RemoveMagicAsync(summoner, raidId, "magic_smite", false);

        result.Success.Should().BeTrue();
        svc.RaidMagics.Verify(r => r.SoftDeleteAsync(raidMagic, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveMagic_NonSummoner_NonWorld_Denied()
    {
        var svc      = BuildService();
        var summoner = Guid.NewGuid();
        var other    = Guid.NewGuid();
        var raid     = MakeRaid(summonerId: summoner);
        var raidId   = raid.Id;
        var raidMagic = RaidMagic.Create(raidId, "magic_smite", summoner);

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidMagics.Setup(r => r.FindAsync(raidId, "magic_smite", It.IsAny<CancellationToken>()))
            .ReturnsAsync(raidMagic);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef());

        var result = await svc.Service.RemoveMagicAsync(other, raidId, "magic_smite", false);

        result.FailureCode.Should().Be(MagicApplyFailureCode.RemoveNotAllowed);
    }

    [Fact]
    public async Task RemoveMagic_WorldRaid_NonAdmin_Denied()
    {
        var svc      = BuildService();
        var summoner = Guid.NewGuid();
        var raid     = MakeRaid(summonerId: summoner);
        var raidId   = raid.Id;
        var raidMagic = RaidMagic.Create(raidId, "magic_smite", summoner);

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidMagics.Setup(r => r.FindAsync(raidId, "magic_smite", It.IsAny<CancellationToken>()))
            .ReturnsAsync(raidMagic);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef("World"));

        var result = await svc.Service.RemoveMagicAsync(summoner, raidId, "magic_smite", isAdmin: false);

        result.FailureCode.Should().Be(MagicApplyFailureCode.WorldGateBlocked);
    }

    [Fact]
    public async Task RemoveMagic_WorldRaid_Admin_Success()
    {
        var svc       = BuildService();
        var admin     = Guid.NewGuid();
        var summoner  = Guid.NewGuid();
        var raid      = MakeRaid(summonerId: summoner);
        var raidId    = raid.Id;
        var raidMagic = RaidMagic.Create(raidId, "magic_smite", admin);

        svc.Raids.Setup(r => r.FindByIdAsync(raidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        svc.RaidMagics.Setup(r => r.FindAsync(raidId, "magic_smite", It.IsAny<CancellationToken>()))
            .ReturnsAsync(raidMagic);
        svc.RaidDefs.Setup(d => d.GetById("raid_test")).Returns(MakeRaidDef("World"));
        svc.RaidMagics.Setup(r => r.SoftDeleteAsync(raidMagic, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await svc.Service.RemoveMagicAsync(admin, raidId, "magic_smite", isAdmin: true);

        result.Success.Should().BeTrue();
    }
}
