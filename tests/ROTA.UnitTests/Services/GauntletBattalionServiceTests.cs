using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class GauntletBattalionServiceTests
{
    private static readonly Guid Pid = Guid.NewGuid();

    private static (GauntletBattalionService svc,
                    Mock<IGauntletBattalionRepository> repo,
                    Mock<ILegionService> legion)
        Build(IReadOnlyList<OwnedUnitResponse> owned, PlayerGauntletBattalion? battalion)
    {
        var repo   = new Mock<IGauntletBattalionRepository>();
        var legion = new Mock<ILegionService>();
        var players = new Mock<IPlayerRepository>();
        var equip  = new Mock<IEquipmentService>();

        legion.Setup(l => l.GetOwnedUnitsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(owned);
        repo.Setup(r => r.GetForPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(battalion);
        // No player stats / no gear → effective base 0 (so SetBattalion-response power reflects units only).
        players.Setup(p => p.FindByIdWithStatsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);
        equip.Setup(e => e.GetEffectiveCombatDataAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Guid _, long a, long d, CancellationToken _) => new EffectiveCombatData(a, d, null, 0.0));

        return (new GauntletBattalionService(repo.Object, legion.Object, players.Object, equip.Object), repo, legion);
    }

    private static OwnedUnitResponse Unit(string id, string type, int atk, int def)
        => new() { UnitDefinitionId = id, UnitType = type, BaseAttack = atk, BaseDefense = def };

    private static PlayerGauntletBattalion Battalion(string generalsJson, string troopsJson)
    {
        var b = PlayerGauntletBattalion.Create(Pid);
        b.SetLoadout(generalsJson, troopsJson);
        return b;
    }

    // ── power ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputePower_empty_battalion_is_player_only()
    {
        var (svc, _, _) = Build(new List<OwnedUnitResponse>(), battalion: null);
        // (50)×4 + (20) = 220
        (await svc.ComputePowerAsync(Pid, 50, 20)).Should().Be(220);
    }

    [Fact]
    public async Task ComputePower_sums_battalion_units_with_locked_weighting()
    {
        var owned = new List<OwnedUnitResponse> { Unit("g1", "General", 100, 50), Unit("t1", "Troop", 10, 5) };
        var (svc, _, _) = Build(owned, Battalion("[\"g1\"]", "[\"t1\"]"));
        // (200 + 110)×4 + (80 + 55) = 1240 + 135 = 1375
        (await svc.ComputePowerAsync(Pid, 200, 80)).Should().Be(1375);
    }

    [Fact]
    public async Task ComputePower_ignores_units_no_longer_owned()
    {
        var owned = new List<OwnedUnitResponse> { Unit("g1", "General", 100, 50) };   // "gGone" not owned
        var (svc, _, _) = Build(owned, Battalion("[\"g1\",\"gGone\"]", "[]"));
        // only g1 counts: (0 + 100)×4 + (0 + 50) = 450
        (await svc.ComputePowerAsync(Pid, 0, 0)).Should().Be(450);
    }

    // ── assign ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetBattalion_success_persists_and_returns_power()
    {
        var owned = new List<OwnedUnitResponse> { Unit("g1", "General", 100, 50), Unit("t1", "Troop", 10, 5) };
        var (svc, repo, _) = Build(owned, battalion: null);

        var result = await svc.SetBattalionAsync(Pid, new[] { "g1" }, new[] { "t1" });

        result.Status.Should().Be(BattalionUpdateStatus.Success);
        // effective base 0 → (110)×4 + (55) = 495
        result.Battalion!.Power.Should().Be(495);
        result.Battalion.Generals.Should().ContainSingle(u => u.UnitDefinitionId == "g1");
        result.Battalion.Troops.Should().ContainSingle(u => u.UnitDefinitionId == "t1");
        repo.Verify(r => r.UpsertAsync(It.IsAny<PlayerGauntletBattalion>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetBattalion_rejects_unowned_unit()
    {
        var (svc, repo, _) = Build(new List<OwnedUnitResponse>(), battalion: null);
        var result = await svc.SetBattalionAsync(Pid, new[] { "gX" }, Array.Empty<string>());
        result.Status.Should().Be(BattalionUpdateStatus.UnitNotOwned);
        repo.Verify(r => r.UpsertAsync(It.IsAny<PlayerGauntletBattalion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetBattalion_rejects_troop_in_a_general_slot()
    {
        var owned = new List<OwnedUnitResponse> { Unit("t1", "Troop", 10, 5) };
        var (svc, _, _) = Build(owned, battalion: null);
        var result = await svc.SetBattalionAsync(Pid, new[] { "t1" }, Array.Empty<string>());
        result.Status.Should().Be(BattalionUpdateStatus.WrongUnitType);
    }

    [Fact]
    public async Task SetBattalion_rejects_the_same_unit_in_two_slots()
    {
        var owned = new List<OwnedUnitResponse> { Unit("g1", "General", 100, 50) };
        var (svc, _, _) = Build(owned, battalion: null);
        // g1 in both bands → duplicate (checked before ownership/type)
        var result = await svc.SetBattalionAsync(Pid, new[] { "g1" }, new[] { "g1" });
        result.Status.Should().Be(BattalionUpdateStatus.DuplicateUnit);
    }

    [Fact]
    public async Task GetBattalion_resolves_units_and_drops_unowned()
    {
        var owned = new List<OwnedUnitResponse> { Unit("g1", "General", 100, 50) };
        var (svc, _, _) = Build(owned, Battalion("[\"g1\",\"gGone\"]", "[]"));
        var resp = await svc.GetBattalionAsync(Pid);
        resp.Generals.Should().ContainSingle(u => u.UnitDefinitionId == "g1");
        resp.GeneralSlots.Should().Be(GauntletBattalionService.GeneralSlots);
        resp.TroopSlots.Should().Be(GauntletBattalionService.TroopSlots);
        resp.Power.Should().Be(450);   // (100)×4 + 50, effective base 0
    }
}
