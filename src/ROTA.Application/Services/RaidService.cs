using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA — reward steps are not wrapped in a single DB transaction. Stamina is spent first;
//        a crash mid-reward is unfair but acceptable. Phase 2: explicit transaction scope.
public sealed class RaidService : IRaidService
{
    // HP multipliers per difficulty
    private static readonly IReadOnlyDictionary<RaidDifficulty, double> HpMultipliers =
        new Dictionary<RaidDifficulty, double>
        {
            [RaidDifficulty.Normal]    = 1.0,
            [RaidDifficulty.Hard]      = 1.4,
            [RaidDifficulty.Legendary] = 2.0,
            [RaidDifficulty.Nightmare] = 3.6,
        };

    // Contribution tier multipliers — applied to ALL rewards
    private static readonly IReadOnlyDictionary<string, decimal> TierMultipliers =
        new Dictionary<string, decimal>
        {
            ["Legendary1"]  = 1.50m,
            ["Legendary2"]  = 1.25m,
            ["Legendary3"]  = 1.10m,
            ["Epic"]        = 1.00m,
            ["Rare"]        = 0.75m,
            ["Participant"] = 0.25m,
        };

    private static readonly IReadOnlyDictionary<RaidDifficulty, string> DifficultyColors =
        new Dictionary<RaidDifficulty, string>
        {
            [RaidDifficulty.Normal]    = "Green",
            [RaidDifficulty.Hard]      = "Yellow",
            [RaidDifficulty.Legendary] = "Red",
            [RaidDifficulty.Nightmare] = "Purple",
        };

    private readonly IActiveRaidRepository _raids;
    private readonly IRaidParticipantRepository _participants;
    private readonly IPlayerRepository _players;
    private readonly IPlayerResourceRepository _resources;
    private readonly IEnergyService _energy;
    private readonly IGemService _gems;
    private readonly IStatService _stats;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly ILootTableProvider _lootTables;
    private readonly IAuditLogRepository _auditLog;
    private readonly IRaidDefinitionProvider _raidDefinitions;
    private readonly IRaidHitCache _hitCache;
    private readonly Random _random;

    public RaidService(
        IActiveRaidRepository raids,
        IRaidParticipantRepository participants,
        IPlayerRepository players,
        IPlayerResourceRepository resources,
        IEnergyService energy,
        IGemService gems,
        IStatService stats,
        IPlayerInventoryRepository inventory,
        IItemDefinitionProvider itemDefs,
        ILootTableProvider lootTables,
        IAuditLogRepository auditLog,
        IRaidDefinitionProvider raidDefinitions,
        IRaidHitCache hitCache,
        Random? random = null)
    {
        _raids           = raids;
        _participants    = participants;
        _players         = players;
        _resources       = resources;
        _energy          = energy;
        _gems            = gems;
        _stats           = stats;
        _inventory       = inventory;
        _itemDefs        = itemDefs;
        _lootTables      = lootTables;
        _auditLog        = auditLog;
        _raidDefinitions = raidDefinitions;
        _hitCache        = hitCache;
        _random          = random ?? Random.Shared;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var activeRaids = await _raids.GetAllActiveAsync(ct);
        var result = new List<ActiveRaidResponse>(activeRaids.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var raid in activeRaids)
        {
            var definition = _raidDefinitions.GetById(raid.RaidDefinitionId);
            var participant = await _participants.FindByRaidAndPlayerAsync(raid.Id, playerId, ct);

            result.Add(new ActiveRaidResponse
            {
                ActiveRaidId          = raid.Id,
                RaidDefinitionId      = raid.RaidDefinitionId,
                Name                  = definition?.Name ?? raid.RaidDefinitionId,
                CurrentHp             = raid.CurrentHp,
                MaxHp                 = raid.MaxHp,
                HpPercent             = raid.MaxHp > 0 ? (double)raid.CurrentHp / raid.MaxHp * 100.0 : 0,
                IsDefeated            = raid.IsDefeated,
                ExpiresAt             = raid.ExpiresAt,
                TimerRemainingSeconds = (long)Math.Max(0, (raid.ExpiresAt - now).TotalSeconds),
                SummonedByUsername    = raid.SummonedByPlayer?.Username ?? string.Empty,
                ParticipantCount      = raid.ParticipantCount,
                YourTotalDamage       = participant?.TotalDamageDealt ?? 0,
                YourHitCount          = participant?.HitCount ?? 0,
                Tier                  = definition?.Tier ?? "Standard",
                Difficulty            = raid.Difficulty.ToString(),
                DifficultyColor       = DifficultyColors[raid.Difficulty],
                YourCurrentTier       = ComputeTier(participant?.TotalDamageDealt ?? 0, activeRaids.Count, participant, null),
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SummonRaidResult> SummonRaidAsync(
        Guid playerId, string raidDefinitionId, RaidDifficulty difficulty, CancellationToken ct = default)
    {
        var definition = _raidDefinitions.GetById(raidDefinitionId);
        if (definition is null)
            return new SummonRaidResult
            {
                FailureCode   = SummonRaidFailureCode.DefinitionNotFound,
                FailureReason = $"Raid definition '{raidDefinitionId}' not found.",
            };

        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null)
            return new SummonRaidResult
            {
                FailureCode   = SummonRaidFailureCode.PlayerNotFound,
                FailureReason = "Player not found.",
            };

        long finalHp = (long)(definition.BaseHp * HpMultipliers[difficulty]);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(definition.TimerHours);
        var raid = ActiveRaid.Create(raidDefinitionId, playerId, finalHp, expiresAt, difficulty);
        await _raids.CreateAsync(raid, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidSummon", null,
            $"Summoned '{definition.Name}' [{difficulty}] (id={raid.Id}). HP={raid.MaxHp}, expires={expiresAt:O}",
            null), ct);

        return new SummonRaidResult
        {
            Success  = true,
            Response = new SummonRaidResponse
            {
                ActiveRaidId          = raid.Id,
                Name                  = definition.Name,
                MaxHp                 = raid.MaxHp,
                ExpiresAt             = raid.ExpiresAt,
                TimerRemainingSeconds = (long)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds,
                Difficulty            = difficulty.ToString(),
                DifficultyColor       = DifficultyColors[difficulty],
            },
        };
    }

    /// <inheritdoc />
    public async Task<RaidHitResult> HitRaidAsync(
        Guid playerId, Guid activeRaidId, int hitSize, string idempotencyKey,
        CancellationToken ct = default)
    {
        // 1. Load raid
        var raid = await _raids.FindByIdAsync(activeRaidId, ct);
        if (raid is null)
            return HitFail(RaidHitFailureCode.RaidNotFound, "Raid not found.");

        // 2. Timer expired
        if (raid.ExpiresAt < DateTimeOffset.UtcNow)
            return HitFail(RaidHitFailureCode.RaidExpired, "Raid timer has expired.");

        // 3. Already defeated
        if (raid.IsDefeated)
            return HitFail(RaidHitFailureCode.RaidAlreadyDefeated, "Raid has already been defeated.");

        // 4. Idempotency check
        var cached = await _hitCache.GetAsync(idempotencyKey, ct);
        if (cached is not null)
            return new RaidHitResult { Success = true, Response = cached };

        // 5. Validate hit size
        if (hitSize != 1 && hitSize != 5 && hitSize != 20)
            return HitFail(RaidHitFailureCode.InvalidHitSize, "Hit size must be 1, 5, or 20.");

        var definition = _raidDefinitions.GetById(raid.RaidDefinitionId)
            ?? throw new InvalidOperationException($"Raid definition '{raid.RaidDefinitionId}' not found.");

        // 6. Spend stamina
        int staminaCost = hitSize * definition.StaminaCostPerHit;
        var staminaSpent = await _energy.SpendEnergyAsync(playerId, ResourceType.Stamina, staminaCost, ct);
        if (!staminaSpent)
            return HitFail(RaidHitFailureCode.InsufficientStamina, "Insufficient stamina.");

        // 7. Compute damage — server-seeded RNG, never client
        var player = await _players.FindByIdWithStatsAsync(playerId, ct)
            ?? throw new InvalidOperationException($"Player {playerId} not found after stamina spend.");

        var multiplier = 0.85 + _random.NextDouble() * 0.30; // uniform [0.85, 1.15]
        long baseValue = (player.Stats!.BaseAttack * 4L) + player.Stats.BaseDefense;
        long damage = Math.Max(1, (long)(baseValue * hitSize * multiplier));

        // 8. Deduct HP
        raid.TakeDamage(damage);

        // 9. Upsert RaidParticipant
        var participant = await _participants.FindByRaidAndPlayerAsync(activeRaidId, playerId, ct);
        bool isNewParticipant = participant is null;
        if (isNewParticipant)
        {
            participant = RaidParticipant.Create(activeRaidId, playerId);
            raid.IncrementParticipantCount();
        }
        participant!.RecordHit(damage);

        if (isNewParticipant)
            await _participants.CreateAsync(participant, ct);
        else
            await _participants.UpdateAsync(participant, ct);

        // 10. Kill processing
        bool isKill = raid.CurrentHp == 0;
        if (isKill)
            raid.MarkDefeated();

        await _raids.UpdateAsync(raid, ct);

        RaidRewards? rewards = null;
        if (isKill)
        {
            var allParticipants = await _participants.GetAllForRaidAsync(activeRaidId, ct);
            rewards = await DistributeKillRewardsAsync(
                playerId, player, raid, definition, allParticipants, ct);
        }

        // 11. Audit
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidHit", null,
            $"Hit raid {activeRaidId} ({definition.Name}) [{raid.Difficulty}] for {damage} dmg (x{hitSize}). HP: {raid.CurrentHp}/{raid.MaxHp}. Kill: {isKill}",
            null), ct);

        var newStaminaValue = await _energy.GetCurrentEnergyAsync(playerId, ResourceType.Stamina, ct);
        var staminaResource = await _resources.GetAsync(playerId, ResourceType.Stamina, ct);
        int newStaminaMax = staminaResource?.MaxValue ?? 0;

        var allParts = isKill ? null : (IReadOnlyList<RaidParticipant>?)null;
        string callerTier = rewards?.ContributionTier ?? "Participant";

        var response = new RaidHitResponse
        {
            Success          = true,
            DamageDealt      = damage,
            CurrentHp        = raid.CurrentHp,
            MaxHp            = raid.MaxHp,
            HpPercent        = raid.MaxHp > 0 ? (double)raid.CurrentHp / raid.MaxHp * 100.0 : 0,
            IsDefeated       = raid.IsDefeated,
            YourTotalDamage  = participant.TotalDamageDealt,
            YourHitCount     = participant.HitCount,
            ParticipantCount = raid.ParticipantCount,
            NewStaminaValue  = newStaminaValue,
            NewStaminaMax    = newStaminaMax,
            Rewards          = rewards,
            ExpiresAt        = raid.ExpiresAt,
            Difficulty       = raid.Difficulty.ToString(),
            DifficultyColor  = DifficultyColors[raid.Difficulty],
            YourCurrentTier  = callerTier,
        };

        await _hitCache.SetAsync(idempotencyKey, response, ct);

        return new RaidHitResult { Success = true, Response = response };
    }

    // -------------------------------------------------------------------
    // KILL REWARD DISTRIBUTION
    // -------------------------------------------------------------------

    private async Task<RaidRewards> DistributeKillRewardsAsync(
        Guid callerPlayerId,
        Player callerPlayer,
        ActiveRaid raid,
        RaidDefinition definition,
        IReadOnlyList<RaidParticipant> allParticipants,
        CancellationToken ct)
    {
        var sorted = allParticipants.OrderByDescending(p => p.TotalDamageDealt).ToList();
        long totalDamage = sorted.Sum(p => p.TotalDamageDealt);

        // Assign tiers
        var tierAssignments = new Dictionary<Guid, (string tier, decimal multiplier)>();
        int epicCutoff = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.10));

        for (int i = 0; i < sorted.Count; i++)
        {
            var p = sorted[i];
            string tierKey;
            if (i == 0)      tierKey = "Legendary1";
            else if (i == 1) tierKey = "Legendary2";
            else if (i == 2) tierKey = "Legendary3";
            else if (i < epicCutoff) tierKey = "Epic";
            else
            {
                double pct = totalDamage > 0 ? (double)p.TotalDamageDealt / totalDamage * 100.0 : 0;
                // Load loot table to get minContributionPercent
                var lt = _lootTables.GetById(definition.LootTableId);
                double minPct = lt?.Difficulties?.GetValueOrDefault(raid.Difficulty.ToString())
                    ?.MinContributionPercent ?? 0.1;
                tierKey = pct >= minPct ? "Rare" : "Participant";
            }
            tierAssignments[p.PlayerId] = (tierKey, TierMultipliers[tierKey]);
        }

        string callerTier = "Participant";
        decimal callerMultiplier = 0.25m;
        int callerGemsGranted = 0;
        int callerUnassignedSP = 0;
        var callerItems = new List<ItemGrantDTO>();

        foreach (var p in allParticipants)
        {
            var (tier, multiplier) = tierAssignments.GetValueOrDefault(p.PlayerId, ("Participant", 0.25m));
            string displayTier = tier.StartsWith("Legendary") ? "Legendary" : tier;

            Player? participantPlayer = p.PlayerId == callerPlayerId
                ? callerPlayer
                : await _players.FindByIdAsync(p.PlayerId, ct);
            if (participantPlayer is null) continue;

            int levelBefore = participantPlayer.Level;

            // Gold and XP scaled by difficulty (same multiplier as HP) then by tier
            double diffMult = HpMultipliers[raid.Difficulty];
            long gold = (long)Math.Round(definition.BaseGoldReward * diffMult * (double)multiplier);
            int xp    = (int)Math.Round(definition.BaseExperienceReward * diffMult * (double)multiplier);

            participantPlayer.AddGold(gold);
            participantPlayer.AddExperience(xp);
            await _players.UpdateAsync(participantPlayer, ct);

            // Grant level-up points for each level gained
            for (int lvl = levelBefore + 1; lvl <= participantPlayer.Level; lvl++)
                await _stats.GrantLevelUpPointsAsync(p.PlayerId, lvl, ct);

            // Gems — Rare+ only
            if (displayTier is not "Participant")
            {
                int gemAmount = (int)Math.Round(definition.BaseGemReward * (double)multiplier);
                if (gemAmount > 0)
                {
                    var gemRef = $"raid:{raid.Id}:{p.PlayerId}";
                    var granted = await _gems.GrantGemsAsync(
                        p.PlayerId, gemAmount, GemTransactionType.RaidReward, gemRef, ct);
                    if (p.PlayerId == callerPlayerId && granted)
                        callerGemsGranted = gemAmount;
                }
            }

            // Loot table — stat points and items (cumulative thresholds)
            int unassignedSP = 0;
            var items = new List<ItemGrantDTO>();
            if (!string.IsNullOrEmpty(definition.LootTableId))
            {
                var lt = _lootTables.GetById(definition.LootTableId);
                if (lt?.Difficulties is not null
                    && lt.Difficulties.TryGetValue(raid.Difficulty.ToString(), out var diffLoot)
                    && diffLoot.ThresholdRewards is not null)
                {
                    double contribPct = totalDamage > 0
                        ? (double)p.TotalDamageDealt / totalDamage * 100.0
                        : 0;

                    // Cumulative: collect all threshold tiers the player qualifies for
                    foreach (var threshold in diffLoot.ThresholdRewards
                        .OrderBy(t => t.ContributionPercent)
                        .Where(t => contribPct >= t.ContributionPercent))
                    {
                        unassignedSP += (int)Math.Round(threshold.UnassignedStatPoints * (double)multiplier);

                        foreach (var drop in threshold.ItemDrops)
                        {
                            if (_random.NextDouble() < drop.Chance)
                            {
                                int qty = (int)Math.Max(1, Math.Round(drop.Quantity * (double)multiplier));
                                await GrantInventoryItemAsync(p.PlayerId, drop.ItemId, qty, items, ct);
                            }
                        }
                    }
                }
            }

            if (unassignedSP > 0)
                await _stats.AddUnassignedPointsAsync(p.PlayerId, unassignedSP, ct);

            if (p.PlayerId == callerPlayerId)
            {
                callerTier = displayTier;
                callerMultiplier = multiplier;
                callerUnassignedSP = unassignedSP;
                callerItems = items;
            }
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            callerPlayerId, "RaidKill", null,
            $"Raid {raid.Id} ({definition.Name}) [{raid.Difficulty}] defeated. {allParticipants.Count} participants rewarded.",
            null), ct);

        var caller = callerPlayer;
        return new RaidRewards
        {
            GoldGranted              = (long)Math.Round(definition.BaseGoldReward * HpMultipliers[raid.Difficulty] * (double)callerMultiplier),
            ExperienceGranted        = (int)Math.Round(definition.BaseExperienceReward * HpMultipliers[raid.Difficulty] * (double)callerMultiplier),
            GemsGranted              = callerGemsGranted,
            NewPlayerGold            = caller.Gold,
            NewPlayerExperience      = caller.Experience,
            NewPlayerLevel           = caller.Level,
            ContributionTier         = callerTier,
            TierMultiplier           = callerMultiplier,
            UnassignedStatPointsGranted = callerUnassignedSP,
            ItemsGranted             = callerItems,
        };
    }

    // -------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------

    private static string ComputeTier(long damage, int totalParticipants, RaidParticipant? p, IReadOnlyList<RaidParticipant>? all)
        => "Participant"; // placeholder for live tier shown before kill

    private async Task GrantInventoryItemAsync(
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

    private static RaidHitResult HitFail(RaidHitFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
