using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class PlayerService : IPlayerService
{
    private readonly IPlayerRepository _players;
    private readonly IEnergyService _energy;
    private readonly IAuditLogRepository _auditLog;
    private readonly IEquipmentService _equipment;
    private readonly IPlayerMasteryRepository _masteryRepo;
    private readonly IMasteryService _mastery;
    private readonly IAchievementService _achievements;

    public PlayerService(
        IPlayerRepository players,
        IEnergyService energy,
        IAuditLogRepository auditLog,
        IEquipmentService equipment,
        IPlayerMasteryRepository masteryRepo,
        IMasteryService mastery,
        IAchievementService achievements)
    {
        _players      = players;
        _energy       = energy;
        _auditLog     = auditLog;
        _equipment    = equipment;
        _masteryRepo  = masteryRepo;
        _mastery      = mastery;
        _achievements = achievements;
    }

    public async Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdWithResourcesAsync(playerId, ct);
        if (player is null) return null;

        var now = DateTimeOffset.UtcNow;
        var resources = new List<ResourceValueResponse>();
        foreach (var resource in player.Resources)
        {
            var liveValue = await _energy.GetCurrentEnergyAsync(playerId, resource.ResourceType, ct);
            var regenMinutesPerPoint = _energy.GetRegenMinutesPerPoint(player.Class, resource.ResourceType);
            resources.Add(new ResourceValueResponse
            {
                Type                 = resource.ResourceType.ToString(),
                LiveValue            = liveValue,
                MaxValue             = resource.MaxValue,
                RegenPerMinute       = resource.RegenPerMinute,
                RegenMinutesPerPoint = regenMinutesPerPoint,
                SecondsToNextPoint   = ComputeSecondsToNextPoint(liveValue, resource.MaxValue, resource.LastRegenAt, regenMinutesPerPoint, now),
            });
        }

        // Compute effective stats for profile display.
        int effAtk = player.Stats?.BaseAttack ?? 0;
        int effDef = player.Stats?.BaseDefense ?? 0;
        if (player.Stats is not null)
        {
            var combat = await _equipment.GetEffectiveCombatDataAsync(
                playerId, player.Stats.BaseAttack, player.Stats.BaseDefense, ct);
            effAtk = combat.EffectiveAttack;
            effDef = combat.EffectiveDefense;
        }

        // Masteries (System 22) — pledge is free from the loaded player; rating from current levels
        // (no row writes on a profile GET; missing levels default to 1 inside ComputeRating).
        var masteryLevels = (await _masteryRepo.GetForPlayerAsync(playerId, ct))
            .ToDictionary(m => m.Ancient, m => m.Level);
        int masteryRating = _mastery.ComputeRating(masteryLevels);

        // Achievements (TICKET 46) — profile read is one of the two completion-evaluation trigger
        // points (mirrors masteries). Best-effort: a failure here must never break the profile load.
        int achievementPoints = 0;
        try
        {
            await _achievements.EvaluateCompletionsAsync(playerId, ct);
            achievementPoints = await _achievements.GetTotalPointsAsync(playerId, ct);
        }
        catch
        {
            // swallow — AP display is non-critical to the profile load
        }

        return new PlayerProfileResponse
        {
            Id               = player.Id,
            Username         = player.Username,
            DisplayName      = string.IsNullOrWhiteSpace(player.DisplayName) ? player.Username : player.DisplayName,
            Email            = player.Email,
            Level            = player.Level,
            Class            = player.Class.ToString(),
            Experience       = player.Experience,
            Gold             = player.Gold,
            GuildId          = player.GuildId,
            GuildRank        = player.GuildRank,
            CreatedAt        = player.CreatedAt,
            UpdatedAt        = player.UpdatedAt,
            Resources        = resources,
            EffectiveAttack  = effAtk,
            EffectiveDefense = effDef,
            ActivePledge            = player.ActivePledgeAncient?.ToString(),
            MasteryRatingActive     = masteryRating,
            TotalAchievementPoints  = achievementPoints,
        };
    }

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

    public async Task<UpdateDisplayNameResult> UpdateDisplayNameAsync(
        Guid playerId, UpdateDisplayNameRequest request, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null || player.IsDeleted)
            return new UpdateDisplayNameResult(DisplayNameUpdateStatus.NotFound);

        player.UpdateDisplayName(request.DisplayName);
        await _players.UpdateAsync(player, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "DisplayNameUpdated", null,
            $"DisplayName changed to '{request.DisplayName}'.", null), ct);

        return new UpdateDisplayNameResult(DisplayNameUpdateStatus.Success, player.DisplayName, player.UpdatedAt);
    }

    // -------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------

    private static int ComputeSecondsToNextPoint(
        int liveValue,
        int maxValue,
        DateTimeOffset lastRegenAt,
        double regenMinutesPerPoint,
        DateTimeOffset now)
    {
        if (liveValue >= maxValue || regenMinutesPerPoint <= 0)
            return 0;

        var elapsedMinutes = (now - lastRegenAt).TotalMinutes;
        var progressIntoCurrentCycle = elapsedMinutes % regenMinutesPerPoint;
        var secondsRemaining = (regenMinutesPerPoint - progressIntoCurrentCycle) * 60.0;
        return Math.Max(0, (int)Math.Ceiling(secondsRemaining));
    }
}
