using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA
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

    public async Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var allRaids = await _raids.GetAllActiveAsync(ct);
        // Personal raids are private — only visible to their summoner.
        var activeRaids = allRaids
            .Where(r => r.Size != RaidSize.Personal || r.SummonedByPlayerId == playerId)
            .ToList();
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
                Size                  = raid.Size.ToString(),
                YourCurrentTier       = ComputeTier(participant?.TotalDamageDealt ?? 0, activeRaids.Count, participant, null),
            });
        }

        return result;
    }

    public async Task<SummonRaidResult> SummonRaidAsync(
        Guid playerId, string raidDefinitionId, RaidDifficulty difficulty,
        RaidSize size = RaidSize.Large, CancellationToken ct = default)
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

        // Personal raids use a solo-balanced HP pool; fall back to BaseHp when PersonalBaseHp is unset.
        long baseHp = size == RaidSize.Personal && definition.PersonalBaseHp > 0
            ? definition.PersonalBaseHp
            : definition.BaseHp;
        long finalHp = (long)(baseHp * HpMultipliers[difficulty]);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(definition.TimerHours);
        var raid = ActiveRaid.Create(raidDefinitionId, playerId, finalHp, expiresAt, difficulty, size);
        await _raids.CreateAsync(raid, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidSummon", null,
            $"Summoned '{definition.Name}' [{difficulty}] [{size}] (id={raid.Id}). HP={raid.MaxHp}, expires={expiresAt:O}",
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
                Size                  = size.ToString(),
            },
        };
    }

    public async Task<RaidHitResult> HitRaidAsync(
        Guid playerId, Guid activeRaidId, int hitSize, string idempotencyKey,
        CancellationToken ct = default)
    {
        // 1. Load raid — fast pre-check before any expensive work.
        var raid = await _raids.FindByIdAsync(activeRaidId, ct);
        if (raid is null)
            return HitFail(RaidHitFailureCode.RaidNotFound, "Raid not found.");

        // 2. Timer expired (pre-spend, no cost to player).
        if (raid.ExpiresAt < DateTimeOffset.UtcNow)
            return HitFail(RaidHitFailureCode.RaidExpired,
                "The raid has faded into the void — no rewards, no stamina spent.");

        // 3. Already defeated (pre-spend, no cost to player).
        if (raid.IsDefeated)
            return HitFail(RaidHitFailureCode.RaidAlreadyDefeated,
                "The creature has already fallen. Your blade finds only silence — no stamina spent.");

        // 3a. Personal raid access gate — only the summoner may strike their own sigil raid.
        if (raid.Size == RaidSize.Personal && raid.SummonedByPlayerId != playerId)
            return HitFail(RaidHitFailureCode.AccessDenied,
                "This is a private raid. Only the summoner may strike it.");

        // 4. Atomic idempotency: reserve the slot (SET NX) or return cached response.
        //    Closes the check-then-set race from the previous GetAsync/SetAsync pattern.
        var (slotAcquired, existingResponse) = await _hitCache.TryAcquireSlotAsync(idempotencyKey, ct);
        if (!slotAcquired)
        {
            if (existingResponse is not null)
                return new RaidHitResult { Success = true, Response = existingResponse };
            // Concurrent in-flight duplicate — treat as duplicate-in-progress.
            return HitFail(RaidHitFailureCode.RaidNotFound, "Request already in progress.");
        }

        // 5. Validate hit size.
        if (hitSize != 1 && hitSize != 5 && hitSize != 20)
            return HitFail(RaidHitFailureCode.InvalidHitSize, "Hit size must be 1, 5, or 20.");

        var definition = _raidDefinitions.GetById(raid.RaidDefinitionId)
            ?? throw new InvalidOperationException($"Raid definition '{raid.RaidDefinitionId}' not found.");

        // 6. Spend stamina — EnergyService opens its OWN transaction (AtomicUpdateAsync).
        //    It is NOT enrolled in the AtomicApplyHitAsync transaction that follows.
        //    The refund-on-race path (step 8) closes the fairness gap.
        //    STAMINA-TRANSACTION CAVEAT (BETA): a process crash between spend and refund
        //    could lose stamina — acceptable for beta, Phase 2.
        int staminaCost = hitSize * definition.StaminaCostPerHit;
        var staminaSpent = await _energy.SpendEnergyAsync(playerId, ResourceType.Stamina, staminaCost, ct);
        if (!staminaSpent)
            return HitFail(RaidHitFailureCode.InsufficientStamina, "Insufficient stamina.");

        // 7. Load player stats for the damage formula.
        var player = await _players.FindByIdWithStatsAsync(playerId, ct)
            ?? throw new InvalidOperationException($"Player {playerId} not found after stamina spend.");

        // 8. Apply hit atomically.
        //    AtomicApplyHitAsync begins a PostgreSQL transaction then acquires an advisory
        //    lock on raidId (pg_advisory_xact_lock), which blocks any concurrent call for
        //    the same raid until the transaction ends.  After the lock is held the entity
        //    is reloaded from the DB so the loser always sees the winner's committed state
        //    (IsDefeated=true) and returns false, triggering the refund below.
        //    All repositories sharing this DbContext participate in the same transaction,
        //    so kill rewards are committed exactly once.
        bool raceCondition   = false;
        long damageFinal     = 0;
        long finalHp         = 0;
        bool finalDefeated   = false;
        RaidParticipant? participantFinal = null;
        RaidRewards? rewards = null;

        var applied = await _raids.AtomicApplyHitAsync(activeRaidId, async lockedRaid =>
        {
            // Re-check under lock — covers the window between the pre-spend check and now.
            if (lockedRaid.IsDefeated || lockedRaid.ExpiresAt < DateTimeOffset.UtcNow)
            {
                raceCondition = true;
                return false;
            }

            // Compute damage — server RNG, never client.
            var multiplier = 0.85 + _random.NextDouble() * 0.30; // uniform [0.85, 1.15]
            long baseValue = (player.Stats!.BaseAttack * 4L) + player.Stats.BaseDefense;
            damageFinal = Math.Max(1, (long)(baseValue * hitSize * multiplier));

            lockedRaid.TakeDamage(damageFinal);

            // Upsert participant — CreateAsync/UpdateAsync call SaveChanges on the shared
            // DbContext, flushing all tracked changes within the open transaction.
            var existingPart = await _participants.FindByRaidAndPlayerAsync(activeRaidId, playerId, ct);
            bool isNew = existingPart is null;
            if (isNew)
            {
                participantFinal = RaidParticipant.Create(activeRaidId, playerId);
                lockedRaid.IncrementParticipantCount();
            }
            else
            {
                participantFinal = existingPart;
            }
            participantFinal!.RecordHit(damageFinal);

            if (isNew)
                await _participants.CreateAsync(participantFinal, ct);
            else
                await _participants.UpdateAsync(participantFinal, ct);

            // Kill detection and reward distribution — fully inside the advisory lock.
            bool isKill = lockedRaid.CurrentHp == 0;
            if (isKill)
            {
                lockedRaid.MarkDefeated();
                // GetAllForRaidAsync sees the participant saved above (same tx).
                var allParticipants = await _participants.GetAllForRaidAsync(activeRaidId, ct);
                rewards = await DistributeKillRewardsAsync(
                    playerId, player, lockedRaid, definition, allParticipants, ct);
            }

            finalHp       = lockedRaid.CurrentHp;
            finalDefeated = lockedRaid.IsDefeated;
            return true;
        }, ct);

        // 9. Handle race: the loser was blocked by the advisory lock, then loaded the fresh
        //    entity (IsDefeated=true), and mutate returned false.
        //    Refund stamina so the loser loses nothing.
        if (!applied)
        {
            await _energy.RefillEnergyAsync(playerId, ResourceType.Stamina, staminaCost, ct);
            await _auditLog.AppendAsync(AuditLog.Create(
                playerId, "RaidHitRefund", null,
                $"Refunded {staminaCost} stamina — raid {activeRaidId} ended in race window.",
                null), ct);

            return raceCondition
                ? HitFail(RaidHitFailureCode.RaidAlreadyDefeated,
                    "The creature fell just before your strike landed. Your stamina has been returned — the battle is over.")
                : HitFail(RaidHitFailureCode.RaidNotFound,
                    "Raid no longer available — your stamina has been refunded.");
        }

        // 10. Audit the successful hit.
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidHit", null,
            $"Hit raid {activeRaidId} ({definition.Name}) [{raid.Difficulty}] for {damageFinal} dmg (x{hitSize}). " +
            $"HP: {finalHp}/{raid.MaxHp}. Kill: {finalDefeated}",
            null), ct);

        var newStaminaValue = await _energy.GetCurrentEnergyAsync(playerId, ResourceType.Stamina, ct);
        var staminaResource = await _resources.GetAsync(playerId, ResourceType.Stamina, ct);
        int newStaminaMax   = staminaResource?.MaxValue ?? 0;
        string callerTier   = rewards?.ContributionTier ?? "Participant";

        var response = new RaidHitResponse
        {
            Success         = true,
            DamageDealt     = damageFinal,
            CurrentHp       = finalHp,
            MaxHp           = raid.MaxHp,
            HpPercent       = raid.MaxHp > 0 ? (double)finalHp / raid.MaxHp * 100.0 : 0,
            IsDefeated      = finalDefeated,
            YourTotalDamage = participantFinal!.TotalDamageDealt,
            YourHitCount    = participantFinal.HitCount,
            NewStaminaValue = newStaminaValue,
            NewStaminaMax   = newStaminaMax,
            Rewards         = rewards,
            ExpiresAt       = raid.ExpiresAt,
            Difficulty      = raid.Difficulty.ToString(),
            DifficultyColor = DifficultyColors[raid.Difficulty],
            YourCurrentTier = callerTier,
        };

        // 11. Store the completed response — replaces the "pending" placeholder.
        await _hitCache.StoreResultAsync(idempotencyKey, response, ct);

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
        IReadOnlyList<int> callerLevelUps = Array.Empty<int>();

        foreach (var p in allParticipants)
        {
            var (tier, multiplier) = tierAssignments.GetValueOrDefault(p.PlayerId, ("Participant", 0.25m));
            string displayTier = tier.StartsWith("Legendary") ? "Legendary" : tier;

            Player? participantPlayer = p.PlayerId == callerPlayerId
                ? callerPlayer
                : await _players.FindByIdAsync(p.PlayerId, ct);
            if (participantPlayer is null) continue;

            // Gold and XP scaled by difficulty (same multiplier as HP) then by tier
            double diffMult = HpMultipliers[raid.Difficulty];
            long gold = (long)Math.Round(definition.BaseGoldReward * diffMult * (double)multiplier);
            int xp    = (int)Math.Round(definition.BaseExperienceReward * diffMult * (double)multiplier);

            participantPlayer.AddGold(gold);
            var levelUps = participantPlayer.AddExperience(xp, lvl => _stats.XpToNextLevel(lvl));
            await _players.UpdateAsync(participantPlayer, ct);

            // Fire level-up side effects for each level gained
            foreach (var newLevel in levelUps)
                await _stats.GrantLevelUpPointsAsync(p.PlayerId, newLevel, ct);

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
                callerTier     = displayTier;
                callerMultiplier = multiplier;
                callerUnassignedSP = unassignedSP;
                callerItems    = items;
                callerLevelUps = levelUps;
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
            XpToNextLevel            = _stats.XpToNextLevel(caller.Level),
            CurrentLevelXp           = caller.Experience,
            LevelsGained             = callerLevelUps.Count,
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
