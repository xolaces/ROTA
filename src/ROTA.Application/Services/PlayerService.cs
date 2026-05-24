using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA — profile GET always calls IEnergyService per resource; never returns stored checkpoint values.
public sealed class PlayerService : IPlayerService
{
    private readonly IPlayerRepository _players;
    private readonly IEnergyService _energy;
    private readonly IAuditLogRepository _auditLog;

    public PlayerService(
        IPlayerRepository players,
        IEnergyService energy,
        IAuditLogRepository auditLog)
    {
        _players = players;
        _energy = energy;
        _auditLog = auditLog;
    }

    /// <inheritdoc />
    public async Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdWithResourcesAsync(playerId, ct);
        if (player is null) return null;

        var resources = new List<ResourceValueResponse>();
        foreach (var resource in player.Resources)
        {
            var liveValue = await _energy.GetCurrentEnergyAsync(playerId, resource.ResourceType, ct);
            resources.Add(new ResourceValueResponse
            {
                Type = resource.ResourceType.ToString(),
                LiveValue = liveValue,
                MaxValue = resource.MaxValue,
                RegenPerMinute = resource.RegenPerMinute
            });
        }

        return new PlayerProfileResponse
        {
            Id = player.Id,
            Username = player.Username,
            Email = player.Email,
            Level = player.Level,
            Experience = player.Experience,
            Gold = player.Gold,
            GuildId = player.GuildId,
            GuildRank = player.GuildRank,
            CreatedAt = player.CreatedAt,
            UpdatedAt = player.UpdatedAt,
            Resources = resources
        };
    }

    /// <inheritdoc />
    public async Task<UpdateUsernameResult> UpdateUsernameAsync(Guid playerId, UpdateUsernameRequest request, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null)
            return new UpdateUsernameResult(PlayerUpdateStatus.NotFound);

        // Skip uniqueness check when setting the same username (case-insensitive).
        if (!string.Equals(player.Username, request.Username, StringComparison.OrdinalIgnoreCase)
            && await _players.UsernameExistsAsync(request.Username, ct))
            return new UpdateUsernameResult(PlayerUpdateStatus.UsernameTaken);

        player.UpdateUsername(request.Username);
        await _players.UpdateAsync(player, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "UpdateUsername", null,
            $"Username changed to {request.Username}", null), ct);

        return new UpdateUsernameResult(PlayerUpdateStatus.Success, player.Username, player.UpdatedAt);
    }
}
