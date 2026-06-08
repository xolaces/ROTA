using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
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
    private readonly ILegionService _legionService;
    private readonly IEquipmentService _equipment;
    private readonly IMasteryService _mastery;
    private readonly QuestConfig _questConfig;
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
        ILegionService legionService,
        IEquipmentService equipment,
        IMasteryService mastery,
        IOptions<QuestConfig> questConfig,
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
        _legionService     = legionService;
        _equipment         = equipment;
        _mastery           = mastery;
        _questConfig       = questConfig.Value;
        _random            = random ?? Random.Shared;
    }

    public async Task<IReadOnlyList<QuestAvailabilityResponse>> GetAvailableQuestsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var allProgress = await _questProgress.GetAllForPlayerAsync(playerId, ct);
        var progressByQuestId = allProgress.ToDictionary(p => p.QuestId);

        // A node unlocks the next once the prerequisite has EVER been cleared (permanent latch) — so a
        // chapter-boss reset (T26), which clears IsCleared back to false, never re-locks earned content.
        var unlockedQuestIds = progressByQuestId
            .Where(kv => kv.Value.HasEverCleared)
            .Select(kv => kv.Key)
            .ToHashSet();

        var result = new List<QuestAvailabilityResponse>();
        foreach (var quest in _definitions.GetAll())
        {
            if (quest.PrerequisiteQuestId is not null
                && !unlockedQuestIds.Contains(quest.PrerequisiteQuestId))
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
                Progress           = prog?.Progress ?? _questConfig.NodeStartProgress,
                IsCleared          = prog?.IsCleared ?? false,
                IsBossNode         = quest.IsBoss,
                // Returned nodes have already passed the prerequisite filter above, so they are
                // attemptable. Must be set explicitly: the client disables Attempt when false.
                IsUnlocked         = true,
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

        // 2. Verify node prerequisite has ever been cleared (permanent latch) — difficulty-agnostic.
        if (quest.PrerequisiteQuestId is not null)
        {
            var prereq = await _questProgress.GetAsync(playerId, quest.PrerequisiteQuestId, ct);
            if (prereq is null || !prereq.HasEverCleared)
                return Fail(QuestFailureCode.PrerequisiteNotMet, "Prerequisite quest not yet cleared.");
        }

        // 2b. T26 — a cleared (locked) node can't be attempted until the chapter boss resets it.
        //     Fetched here (before any side effects) and reused for depletion at step 8.
        var progress = await _questProgress.GetAsync(playerId, questId, ct);
        if (progress is not null && progress.IsCleared)
            return Fail(QuestFailureCode.NodeCleared,
                "This node is cleared — defeat the chapter boss to reset it.");

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

        // System 22 Phase A — mastery loot modifiers (Hoard +% drop/gold, Discernment sigil-find).
        // Best-effort: a fetch failure falls back to neutral so the player still gets base rewards.
        MasteryLootModifiers lootMods;
        try { lootMods = await _mastery.GetLootModifiersAsync(playerId, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { lootMods = new MasteryLootModifiers(1.0, 1.0, 1.0, 0.0); }

        // 5. Compute scaled costs and rewards
        float energyMult  = EnergyMultipliers[difficulty];
        float rewardMult  = RewardMultipliers[difficulty];
        int energyCost    = (int)Math.Ceiling(quest.BaseEnergyCost * energyMult);
        int goldReward    = (int)(quest.GoldReward * rewardMult * lootMods.HoardGoldMultiplier);
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

        // 8. Record per-node progress + deplete the node (difficulty-agnostic). Boss nodes deplete
        //    slower (more attempts to clear). Clearing (Progress→0) locks the node and unlocks the next.
        //    `progress` was fetched at step 2b (guaranteed not-cleared here).
        bool isNewProgress = progress is null;
        if (isNewProgress)
            progress = PlayerQuestProgress.Create(playerId, questId, _questConfig.NodeStartProgress);
        bool wasCleared = progress!.IsCleared;
        progress.RecordCompletion();
        double depletion = quest.IsBoss
            ? _questConfig.BossDepletionPerAttempt
            : _questConfig.BattleDepletionPerAttempt;
        progress.Deplete(depletion);
        bool nodeJustCleared = !wasCleared && progress.IsCleared;

        if (isNewProgress)
            await _questProgress.CreateAsync(progress, ct);
        else
            await _questProgress.UpdateAsync(progress, ct);

        // 8b. T26 — completing a chapter boss resets the WHOLE chapter back to fresh (the
        //     deplete→clear→boss→reset farming cycle). HasEverCleared is preserved so unlocks hold.
        bool chapterReset = false;
        if (quest.IsBoss && nodeJustCleared)
        {
            await ResetChapterAsync(playerId, quest.Chapter, progress, ct);
            chapterReset = true;
        }

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

        // 12. Process loot table. Discernment raises chance-drop rates (System 20 Slice 2) — fetch
        //     it once and pass it in; guaranteed drops are unaffected so this read can be skipped
        //     when there's nothing chance-based, but it's a cheap single read on a successful attempt.
        var itemsGranted = new List<ItemGrantDTO>();
        if (quest.LootTableId is not null)
        {
            var lootTable = _lootTables.GetById(quest.LootTableId);
            if (lootTable?.Difficulties is not null
                && lootTable.Difficulties.TryGetValue(difficulty.ToString(), out var diffLoot))
            {
                var stats = await _stats.GetStatsAsync(playerId, ct);
                int discernment = stats?.DiscernmentInvestment ?? 0;
                await ProcessQuestLootAsync(playerId, diffLoot, discernment, lootMods.HoardDropMultiplier, itemsGranted, ct);
            }
        }

        // 13. Sigil drop for Boss nodes
        if (quest.IsBoss && quest.Sigils is not null
            && quest.Sigils.TryGetValue(difficulty.ToString(), out var sigilItemId))
        {
            bool dropSigil = false;

            if (!diffProg.FirstSigilDropped)
            {
                // Guaranteed first-per-difficulty drop — never scaled (locked: SigilFindAppliesToFirstClear=false).
                dropSigil = true;
                diffProg.MarkSigilDropped();
            }
            else if (quest.SigilDropChance > 0)
            {
                // System 22 Phase A — Discernment "sigil find" scales the post-first-clear chance (clamped ≤ 1.0).
                double sigilChance = Math.Min(1.0, quest.SigilDropChance * lootMods.DiscernmentSigilFindMultiplier);
                dropSigil = _random.NextDouble() < sigilChance;
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

        // 14b. System 22 Phase A — mastery challenge counters + tier-up evaluation. BEST-EFFORT: the
        //      quest reward path is non-transactional by design, so a counter/leveling failure must
        //      never fail the attempt. Node/boss clears gate on the just-cleared transition (counted
        //      once per clear cycle). This is one of the two off-hot-path tier-up trigger points.
        try
        {
            await _mastery.RecordActivityAsync(playerId, MasteryActivityType.GoldEarned, goldReward, ct: ct);
            if (nodeJustCleared)
            {
                await _mastery.RecordActivityAsync(playerId, MasteryActivityType.QuestNodeCleared, 1, ct: ct);
                if (quest.IsBoss)
                    await _mastery.RecordActivityAsync(playerId, MasteryActivityType.QuestBossCleared, 1, ct: ct);
            }
            await _mastery.EvaluateTierUpsAsync(playerId, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* best-effort — never break the attempt */ }

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
            NodeProgress      = progress.Progress,
            NodeCleared       = progress.IsCleared,
            NodeJustCleared   = nodeJustCleared,
            ChapterReset      = chapterReset,
        };
    }

    // T26 — reset every node in `chapter` back to fresh (Progress→start, IsCleared→false). Called when
    // a chapter boss is cleared. HasEverCleared is preserved on each node so forward unlocks survive.
    // The just-cleared boss's own progress instance is passed in to avoid a redundant re-fetch/retrack.
    private async Task ResetChapterAsync(
        Guid playerId, int chapter, PlayerQuestProgress bossProgress, CancellationToken ct)
    {
        foreach (var node in _definitions.GetAll())
        {
            if (node.Chapter != chapter) continue;

            var p = node.Id == bossProgress.QuestId
                ? bossProgress
                : await _questProgress.GetAsync(playerId, node.Id, ct);
            if (p is null) continue; // node never attempted → already fresh

            p.Reset(_questConfig.NodeStartProgress);
            await _questProgress.UpdateAsync(p, ct);
        }
    }

    // -------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------

    private async Task ProcessQuestLootAsync(
        Guid playerId,
        Application.Models.LootTableDifficulty loot,
        int discernmentInvestment,
        double hoardDropMultiplier,
        List<ItemGrantDTO> itemsGranted,
        CancellationToken ct)
    {
        // Discernment raises every chance-drop's effective rate, capped at MaxDropChance — but the
        // cap never *lowers* a base that's already high (a base 1.0 "always" drop stays 1.0). It only
        // bounds the boost on low-base drops. Guaranteed drops never pass through here.
        // System 22 Phase A — Hoard +% drop rate stacks multiplicatively into the same scaled chance.
        double Scale(double baseChance)
        {
            double boosted = baseChance * (1 + discernmentInvestment * _questConfig.DiscernmentDropMultiplier) * hoardDropMultiplier;
            double cap = Math.Max(baseChance, _questConfig.MaxDropChance);
            return Math.Min(boosted, cap);
        }

        if (loot.GuaranteedDrops is not null)
        {
            foreach (var drop in loot.GuaranteedDrops)
                await GrantItemAsync(playerId, drop.ItemId, drop.Quantity, itemsGranted, ct);
        }

        if (loot.ChanceDrops is not null)
        {
            foreach (var drop in loot.ChanceDrops)
            {
                if (_random.NextDouble() < Scale(drop.Chance))
                    await GrantItemAsync(playerId, drop.ItemId, drop.Quantity, itemsGranted, ct);
            }
        }

        // Magic drops — idempotent grant (duplicate = no-op).
        if (loot.MagicDrops is not null)
        {
            foreach (var drop in loot.MagicDrops)
            {
                if (_random.NextDouble() < Scale(drop.Chance))
                    await _magicService.GrantMagicAsync(playerId, drop.MagicId, ct);
            }
        }

        // Unit drops — idempotent grant.
        if (loot.UnitDrops is not null)
        {
            foreach (var drop in loot.UnitDrops)
            {
                if (_random.NextDouble() < Scale(drop.Chance))
                    await _legionService.GrantUnitAsync(playerId, drop.UnitId, ct);
            }
        }

        // Legion drops — idempotent grant.
        if (loot.LegionDrops is not null)
        {
            foreach (var drop in loot.LegionDrops)
            {
                if (_random.NextDouble() < Scale(drop.Chance))
                    await _legionService.GrantLegionAsync(playerId, drop.LegionId, ct);
            }
        }

        // Gear drops — idempotent upsert.
        if (loot.GearDrops is not null)
        {
            foreach (var drop in loot.GearDrops)
                if (_random.NextDouble() < Scale(drop.Chance))
                    await _equipment.GrantGearAsync(playerId, drop.GearDefinitionId, drop.Quantity, ct);
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
