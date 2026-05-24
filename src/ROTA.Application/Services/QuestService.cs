using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA — reward steps (player update, progress save, gem grant) are not wrapped in a single
//        DB transaction. Energy is spent first; a server crash mid-reward would be unfair
//        but acceptable. Phase 2: wrap in an explicit transaction.
public sealed class QuestService : IQuestService
{
    private readonly IQuestDefinitionProvider _definitions;
    private readonly IQuestProgressRepository _questProgress;
    private readonly IPlayerRepository _players;
    private readonly IEnergyService _energy;
    private readonly IGemService _gems;
    private readonly IAuditLogRepository _auditLog;

    public QuestService(
        IQuestDefinitionProvider definitions,
        IQuestProgressRepository questProgress,
        IPlayerRepository players,
        IEnergyService energy,
        IGemService gems,
        IAuditLogRepository auditLog)
    {
        _definitions   = definitions;
        _questProgress = questProgress;
        _players       = players;
        _energy        = energy;
        _gems          = gems;
        _auditLog      = auditLog;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuestAvailabilityResponse>> GetAvailableQuestsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var allProgress = await _questProgress.GetAllForPlayerAsync(playerId, ct);
        var progressByQuestId = allProgress.ToDictionary(p => p.QuestId);

        var completedQuestIds = progressByQuestId
            .Where(kv => kv.Value.CompletionCount > 0)
            .Select(kv => kv.Key)
            .ToHashSet();

        var result = new List<QuestAvailabilityResponse>();
        foreach (var quest in _definitions.GetAll())
        {
            if (quest.PrerequisiteQuestId is not null
                && !completedQuestIds.Contains(quest.PrerequisiteQuestId))
                continue;

            progressByQuestId.TryGetValue(quest.Id, out var prog);
            result.Add(new QuestAvailabilityResponse
            {
                Id = quest.Id,
                Name = quest.Name,
                Chapter = quest.Chapter,
                EnergyCost = quest.EnergyCost,
                GoldReward = quest.GoldReward,
                ExperienceReward = quest.ExperienceReward,
                GemReward = quest.GemReward,
                PrerequisiteQuestId = quest.PrerequisiteQuestId,
                CompletionCount = prog?.CompletionCount ?? 0,
                LastCompletedAt = prog?.LastCompletedAt,
            });
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<QuestResultResponse> AttemptQuestAsync(
        Guid playerId, string questId, CancellationToken ct = default)
    {
        // 1. Verify quest exists in static definitions
        var quest = _definitions.GetById(questId);
        if (quest is null)
            return Fail(QuestFailureCode.QuestNotFound, "Quest not found.");

        // 2. Verify prerequisite is completed
        if (quest.PrerequisiteQuestId is not null)
        {
            var prereq = await _questProgress.GetAsync(playerId, quest.PrerequisiteQuestId, ct);
            if (prereq is null || prereq.CompletionCount == 0)
                return Fail(QuestFailureCode.PrerequisiteNotMet, "Prerequisite quest not completed.");
        }

        // 3. Verify player is active
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null)
            return Fail(QuestFailureCode.PlayerNotFound, "Player not found.");
        if (player.IsBanned)
            return Fail(QuestFailureCode.PlayerBanned, "Account is banned.");

        // 4. Spend energy — if insufficient, return immediately with no side effects
        var energySpent = await _energy.SpendEnergyAsync(playerId, ResourceType.Energy, quest.EnergyCost, ct);
        if (!energySpent)
            return Fail(QuestFailureCode.InsufficientEnergy, "Insufficient energy.");

        // 5-8. Apply rewards (energy committed — errors from here propagate as 500)
        player.AddGold(quest.GoldReward);
        player.AddExperience(quest.ExperienceReward);
        await _players.UpdateAsync(player, ct);

        // Record completion
        var progress = await _questProgress.GetAsync(playerId, questId, ct);
        bool isNew = progress is null;
        if (isNew)
            progress = PlayerQuestProgress.Create(playerId, questId);
        progress!.RecordCompletion();

        if (isNew)
            await _questProgress.CreateAsync(progress, ct);
        else
            await _questProgress.UpdateAsync(progress, ct);

        // 9. Grant gems if applicable — referenceId uses the new completion count
        int gemsGranted = 0;
        if (quest.GemReward > 0)
        {
            var referenceId = $"quest:{questId}:{playerId}:{progress.CompletionCount}";
            var granted = await _gems.GrantGemsAsync(
                playerId, quest.GemReward, GemTransactionType.QuestReward, referenceId, ct);
            if (granted) gemsGranted = quest.GemReward;
        }

        // 10. Audit log
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "QuestAttempt", null,
            $"Quest {questId} completed #{progress.CompletionCount}. Gold +{quest.GoldReward}, XP +{quest.ExperienceReward}, Gems +{gemsGranted}",
            null), ct);

        return new QuestResultResponse
        {
            Success          = true,
            GoldGranted      = quest.GoldReward,
            ExperienceGranted = quest.ExperienceReward,
            GemsGranted      = gemsGranted,
            NewLevel         = player.Level,
            NewExperience    = player.Experience,
            NewGold          = player.Gold,
            CompletionCount  = progress.CompletionCount,
        };
    }

    private static QuestResultResponse Fail(QuestFailureCode code, string reason)
        => new QuestResultResponse { Success = false, FailureCode = code, FailureReason = reason };
}
