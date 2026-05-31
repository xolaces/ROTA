using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

//        a server crash mid-reward would be unfair but acceptable. Phase 2: wrap in an explicit transaction.
public sealed class QuestService : IQuestService
{
    private static readonly IReadOnlyDictionary<QuestDifficulty, float> EnergyMultipliers =
        new Dictionary<QuestDifficulty, float>
        {
            [QuestDifficulty.Normal]    = 1.0f,
            [QuestDifficulty.Hard]      = 1.5f,
            [QuestDifficulty.Legendary] = 2.0f,
            [QuestDifficulty.Nightmare] = 3.0f,
        };

    private static readonly IReadOnlyDictionary<QuestDifficulty, float> RewardMultipliers =
        new Dictionary<QuestDifficulty, float>
        {
            [QuestDifficulty.Normal]    = 1.0f,
            [QuestDifficulty.Hard]      = 1.5f,
            [QuestDifficulty.Legendary] = 2.0f,
            [QuestDifficulty.Nightmare] = 3.5f,
        };

    private static readonly IReadOnlyDictionary<QuestDifficulty, string> DifficultyColors =
        new Dictionary<QuestDifficulty, string>
        {
            [QuestDifficulty.Normal]    = "Green",
            [QuestDifficulty.Hard]      = "Yellow",
            [QuestDifficulty.Legendary] = "Red",
            [QuestDifficulty.Nightmare] = "Purple",
        };

    private static readonly IReadOnlyDictionary<QuestDifficulty, QuestDifficulty?> DifficultyGates =
        new Dictionary<QuestDifficulty, QuestDifficulty?>
        {
            [QuestDifficulty.Normal]    = null,
            [QuestDifficulty.Hard]      = QuestDifficulty.Normal,
            [QuestDifficulty.Legendary] = QuestDifficulty.Hard,
            [QuestDifficulty.Nightmare] = QuestDifficulty.Legendary,
        };

    private readonly IQuestDefinitionProvider _definitions;
    private readonly IQuestProgressRepository _questProgress;
    private readonly IQuestDifficultyProgressRepository _difficultyProgress;
    private readonly IPlayerRepository _players;
    private readonly IEnergyService _energy;
    private readonly IGemService _gems;
    private readonly IStatService _stats;
    private readonly ILootTableProvider _lootTables;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IAuditLogRepository _auditLog;
    private readonly IMagicService _magicService;
    private readonly Random _random;

    public QuestService(
        IQuestDefinitionProvider definitions,
        IQuestProgressRepository questProgress,
        IQuestDifficultyProgressRepository difficultyProgress,
        IPlayerRepository players,
        IEnergyService energy,
        IGemService gems,
        IStatService stats,
        ILootTableProvider lootTables,
        IItemDefinitionProvider itemDefs,
        IPlayerInventoryRepository inventory,
        IAuditLogRepository auditLog,
        IMagicService magicService,
        Random? random = null)
    {
        _definitions       = definitions;
        _questProgress     = questProgress;
        _difficultyProgress = difficultyProgress;
        _players           = players;
        _energy            = energy;
        _gems              = gems;
        _stats             = stats;
        _lootTables        = lootTables;
        _itemDefs          = itemDefs;
        _inventory         = inventory;
        _auditLog          = auditLog;
        _magicService      = magicService;
        _random            = random ?? Random.Shared;
    }

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
                Id                 = quest.Id,
                Name               = quest.Name,
                Chapter            = quest.Chapter,
                NodeType           = quest.NodeType,
                BaseEnergyCost     = quest.BaseEnergyCost,
                GoldReward         = quest.GoldReward,
                ExperienceReward   = quest.ExperienceReward,
                GemReward          = quest.GemReward,
                PrerequisiteQuestId = quest.PrerequisiteQuestId,
                CompletionCount    = prog?.CompletionCount ?? 0,
                LastCompletedAt    = prog?.LastCompletedAt,
                IsBossNode         = quest.IsBoss,
            });
        }
        return result;
    }

    public async Task<QuestResultResponse> AttemptQuestAsync(
        Guid playerId, string questId, QuestDifficulty difficulty, CancellationToken ct = default)
    {
        // 1. Verify quest exists
        var quest = _definitions.GetById(questId);
        if (quest is null)
            return Fail(QuestFailureCode.QuestNotFound, "Quest not found.");

        // 2. Verify node prerequisite is completed (difficulty-agnostic)
        if (quest.PrerequisiteQuestId is not null)
        {
            var prereq = await _questProgress.GetAsync(playerId, quest.PrerequisiteQuestId, ct);
            if (prereq is null || prereq.CompletionCount == 0)
                return Fail(QuestFailureCode.PrerequisiteNotMet, "Prerequisite quest not completed.");
        }

        // 3. Verify difficulty gate (Hard requires Normal, etc.)
        var requiredDifficulty = DifficultyGates[difficulty];
        if (requiredDifficulty.HasValue)
        {
            var gate = await _difficultyProgress.GetAsync(playerId, questId, requiredDifficulty.Value, ct);
            if (gate is null || gate.CompletionCount == 0)
                return Fail(QuestFailureCode.DifficultyLocked,
                    $"Complete {requiredDifficulty.Value} first to unlock {difficulty}.");
        }

        // 4. Verify player is active
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null)
            return Fail(QuestFailureCode.PlayerNotFound, "Player not found.");
        if (player.IsBanned)
            return Fail(QuestFailureCode.PlayerBanned, "Account is banned.");

        // 5. Compute scaled costs and rewards
        float energyMult  = EnergyMultipliers[difficulty];
        float rewardMult  = RewardMultipliers[difficulty];
        int energyCost    = (int)Math.Ceiling(quest.BaseEnergyCost * energyMult);
        int goldReward    = (int)(quest.GoldReward * rewardMult);
        int xpReward      = (int)(quest.ExperienceReward * rewardMult);
        int gemReward     = (int)Math.Round(quest.GemReward * rewardMult);

        // 6. Spend energy — if insufficient, return immediately with no side effects
        var spent = await _energy.SpendEnergyAsync(playerId, ResourceType.Energy, energyCost, ct);
        if (!spent)
            return Fail(QuestFailureCode.InsufficientEnergy, "Insufficient energy.");

        // 7. Apply rewards (energy committed — errors from here propagate as 500)
        player.AddGold(goldReward);
        var levelUps = player.AddExperience(xpReward, lvl => _stats.XpToNextLevel(lvl));
        await _players.UpdateAsync(player, ct);

        // Fire level-up side effects for each level gained
        foreach (var newLevel in levelUps)
            await _stats.GrantLevelUpPointsAsync(playerId, newLevel, ct);

        // 8. Record per-node progress (prerequisite tracking — difficulty agnostic)
        var progress = await _questProgress.GetAsync(playerId, questId, ct);
        bool isNewProgress = progress is null;
        if (isNewProgress)
            progress = PlayerQuestProgress.Create(playerId, questId);
        progress!.RecordCompletion();

        if (isNewProgress)
            await _questProgress.CreateAsync(progress, ct);
        else
            await _questProgress.UpdateAsync(progress, ct);

        // 9. Record per-difficulty progress
        var diffProg = await _difficultyProgress.GetAsync(playerId, questId, difficulty, ct);
        bool isNewDiffProg = diffProg is null;
        if (isNewDiffProg)
            diffProg = PlayerQuestDifficultyProgress.Create(playerId, questId, difficulty);
        diffProg!.RecordCompletion();

        // 10. Grant gems if applicable
        int gemsGranted = 0;
        if (gemReward > 0)
        {
            var referenceId = $"quest:{questId}:{playerId}:{progress.CompletionCount}:{difficulty}";
            var granted = await _gems.GrantGemsAsync(
                playerId, gemReward, GemTransactionType.QuestReward, referenceId, ct);
            if (granted) gemsGranted = gemReward;
        }

        // 12. Process loot table
        var itemsGranted = new List<ItemGrantDTO>();
        if (quest.LootTableId is not null)
        {
            var lootTable = _lootTables.GetById(quest.LootTableId);
            if (lootTable?.Difficulties is not null
                && lootTable.Difficulties.TryGetValue(difficulty.ToString(), out var diffLoot))
            {
                await ProcessQuestLootAsync(playerId, diffLoot, itemsGranted, ct);
            }
        }

        // 13. Sigil drop for Boss nodes
        if (quest.IsBoss && quest.Sigils is not null
            && quest.Sigils.TryGetValue(difficulty.ToString(), out var sigilItemId))
        {
            bool dropSigil = false;

            if (!diffProg.FirstSigilDropped)
            {
                dropSigil = true;
                diffProg.MarkSigilDropped();
            }
            else if (quest.SigilDropChance > 0)
            {
                dropSigil = _random.NextDouble() < quest.SigilDropChance;
            }

            if (dropSigil)
            {
                await GrantItemAsync(playerId, sigilItemId, 1, itemsGranted, ct);
            }
        }

        // 14. Save difficulty progress (after sigil tracking)
        if (isNewDiffProg)
            await _difficultyProgress.CreateAsync(diffProg, ct);
        else
            await _difficultyProgress.UpdateAsync(diffProg, ct);

        // 15. Audit log
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "QuestAttempt", null,
            $"Quest {questId} [{difficulty}] completed #{progress.CompletionCount}. Gold +{goldReward}, XP +{xpReward}, Gems +{gemsGranted}",
            null), ct);

        return new QuestResultResponse
        {
            Success           = true,
            GoldGranted       = goldReward,
            ExperienceGranted = xpReward,
            GemsGranted       = gemsGranted,
            NewLevel          = player.Level,
            NewExperience     = player.Experience,
            NewGold           = player.Gold,
            CompletionCount   = progress.CompletionCount,
            Difficulty        = difficulty.ToString(),
            DifficultyColor   = DifficultyColors[difficulty],
            ItemsGranted      = itemsGranted,
            XpGained          = xpReward,
            CurrentLevelXp    = player.Experience,
            XpToNextLevel     = _stats.XpToNextLevel(player.Level),
            LevelsGained      = levelUps.Count,
        };
    }

    // -------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------

    private async Task ProcessQuestLootAsync(
        Guid playerId,
        Application.Models.LootTableDifficulty loot,
        List<ItemGrantDTO> itemsGranted,
        CancellationToken ct)
    {
        if (loot.GuaranteedDrops is not null)
        {
            foreach (var drop in loot.GuaranteedDrops)
                await GrantItemAsync(playerId, drop.ItemId, drop.Quantity, itemsGranted, ct);
        }

        if (loot.ChanceDrops is not null)
        {
            foreach (var drop in loot.ChanceDrops)
            {
                if (_random.NextDouble() < drop.Chance)
                    await GrantItemAsync(playerId, drop.ItemId, drop.Quantity, itemsGranted, ct);
            }
        }

        // Magic drops — idempotent grant (duplicate = no-op).
        if (loot.MagicDrops is not null)
        {
            foreach (var drop in loot.MagicDrops)
            {
                if (_random.NextDouble() < drop.Chance)
                    await _magicService.GrantMagicAsync(playerId, drop.MagicId, ct);
            }
        }
    }

    private async Task GrantItemAsync(
        Guid playerId, string itemDefId, int quantity,
        List<ItemGrantDTO> itemsGranted, CancellationToken ct)
    {
        var existing = await _inventory.GetAsync(playerId, itemDefId, ct);
        if (existing is not null)
        {
            existing.AddQuantity(quantity);
            await _inventory.UpdateAsync(existing, ct);
        }
        else
        {
            var newItem = PlayerInventoryItem.Create(playerId, itemDefId, quantity);
            await _inventory.CreateAsync(newItem, ct);
        }

        var def = _itemDefs.GetById(itemDefId);
        if (def is not null)
        {
            itemsGranted.Add(new ItemGrantDTO
            {
                ItemId   = itemDefId,
                ItemName = def.Name,
                Quantity = quantity,
                Rarity   = def.Rarity.ToString(),
                ArtKey   = def.ArtKey,
            });
        }
    }

    private static QuestResultResponse Fail(QuestFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}
