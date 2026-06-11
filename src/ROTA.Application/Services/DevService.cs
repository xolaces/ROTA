using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class DevService : IDevService
{
    private readonly IPlayerRepository _players;
    private readonly IGemService _gems;
    private readonly IStatService _stats;
    private readonly IEnergyService _energy;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IAuditLogRepository _auditLog;

    public DevService(
        IPlayerRepository players,
        IGemService gems,
        IStatService stats,
        IEnergyService energy,
        IItemDefinitionProvider itemDefs,
        IPlayerInventoryRepository inventory,
        IAuditLogRepository auditLog)
    {
        _players = players;
        _gems = gems;
        _stats = stats;
        _energy = energy;
        _itemDefs = itemDefs;
        _inventory = inventory;
        _auditLog = auditLog;
    }

    public async Task<DevActionResponse> GrantAsync(
        Guid actorId, DevGrantRequest request, string ipAddress, CancellationToken ct = default)
    {
        if (request.Gold < 0 || request.Gems < 0 || request.SkillPoints < 0 || request.Xp < 0)
            return Fail("Amounts must be non-negative.");
        if (request.Gold == 0 && request.Gems == 0 && request.SkillPoints == 0 && request.Xp == 0)
            return Fail("Nothing to grant.");

        var targetId = request.TargetPlayerId ?? actorId;
        var target = await _players.FindByIdAsync(targetId, ct);
        if (target is null) return Fail("Target player not found.");

        int? newLevel = null;

        if (request.Gold > 0 || request.Xp > 0)
        {
            // Same chokepoint as quest/raid rewards (T59) — concurrency-safe; XP fires REAL level-ups.
            var levelUps = await _players.MutateWithRetryAsync<IReadOnlyList<int>>(targetId, p =>
            {
                if (request.Gold > 0) p.AddGold(request.Gold);
                return request.Xp > 0
                    ? p.AddExperience(request.Xp, lvl => _stats.XpToNextLevel(lvl))
                    : Array.Empty<int>();
            }, ct);
            foreach (var lvl in levelUps)
                await _stats.GrantLevelUpPointsAsync(targetId, lvl, ct);
            if (levelUps.Count > 0) newLevel = levelUps[^1];
        }

        if (request.Gems > 0)
            await _gems.GrantGemsAsync(targetId, request.Gems, GemTransactionType.AdminGrant,
                $"dev:{Guid.NewGuid():N}", ct);

        if (request.SkillPoints > 0)
            await _stats.AddUnassignedPointsAsync(targetId, request.SkillPoints, ct);

        var summary = $"gold+{request.Gold} gems+{request.Gems} sp+{request.SkillPoints} xp+{request.Xp}"
                      + (newLevel.HasValue ? $" → level {newLevel}" : "");
        await _auditLog.AppendAsync(AuditLog.Create(
            actorId, "DevGrant", null, $"target={targetId} {summary}", ipAddress), ct);

        return new DevActionResponse { Success = true, Summary = summary, NewLevel = newLevel };
    }

    public async Task<DevActionResponse> GrantItemAsync(
        Guid actorId, DevGrantItemRequest request, string ipAddress, CancellationToken ct = default)
    {
        if (request.Quantity is < 1 or > 1000) return Fail("Quantity must be 1-1000.");
        var def = _itemDefs.GetById(request.ItemDefinitionId);
        if (def is null) return Fail($"Unknown item '{request.ItemDefinitionId}'.");

        var targetId = request.TargetPlayerId ?? actorId;
        if (await _players.FindByIdAsync(targetId, ct) is null) return Fail("Target player not found.");

        var existing = await _inventory.GetAsync(targetId, request.ItemDefinitionId, ct);
        if (existing is null)
            await _inventory.CreateAsync(PlayerInventoryItem.Create(targetId, request.ItemDefinitionId, request.Quantity), ct);
        else
        {
            existing.AddQuantity(request.Quantity);
            await _inventory.UpdateAsync(existing, ct);
        }

        var summary = $"{def.Name} ×{request.Quantity}";
        await _auditLog.AppendAsync(AuditLog.Create(
            actorId, "DevItemGrant", null, $"target={targetId} {summary}", ipAddress), ct);

        return new DevActionResponse { Success = true, Summary = summary };
    }

    public async Task<DevActionResponse> RefillAsync(
        Guid actorId, DevRefillRequest request, string ipAddress, CancellationToken ct = default)
    {
        var targetId = request.TargetPlayerId ?? actorId;
        if (await _players.FindByIdAsync(targetId, ct) is null) return Fail("Target player not found.");

        foreach (var type in new[] { ResourceType.Energy, ResourceType.Stamina, ResourceType.GuildStamina, ResourceType.Health })
            await _energy.RefillToMaxAsync(targetId, type, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            actorId, "DevRefill", null, $"target={targetId} all pools → max", ipAddress), ct);

        return new DevActionResponse { Success = true, Summary = "All resource pools refilled." };
    }

    private static DevActionResponse Fail(string reason)
        => new() { Success = false, Summary = reason };
}
