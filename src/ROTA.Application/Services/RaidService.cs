using System.Text.Json;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
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

    // Participant caps per raid size (Personal cap=1 enforced by the access gate, not this map)
    private static readonly IReadOnlyDictionary<RaidSize, int> ParticipantCaps =
        new Dictionary<RaidSize, int>
        {
            [RaidSize.Personal] = 1,
            [RaidSize.Small]    = 10,
            [RaidSize.Medium]   = 25,
            [RaidSize.Large]    = 50,
            [RaidSize.Titanic]  = 250,
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
    private readonly IEquipmentService _equipment;
    private readonly IRaidMagicRepository _raidMagics;
    private readonly IMagicDefinitionProvider _magicDefs;
    private readonly IMagicService _magicService;
    private readonly MagicConfig _magicConfig;
    // Slice 4 — legion combat deps
    private readonly IPlayerLegionRepository        _playerLegions;
    private readonly IPlayerLegionSlotRepository    _legionSlots;
    private readonly IUnitDefinitionProvider        _unitDefs;
    private readonly ILegionDefinitionProvider      _legionDefs;
    private readonly LegionConfig                   _legionConfig;
    // Slice 5 — commander gear
    private readonly IPlayerCommanderGearRepository _commanderGear;
    private readonly IGearDefinitionProvider        _gearDefs;
    // Slice 6 — unit/legion drop grants
    private readonly ILegionService _legionService;
    // System 17 Slice 4 — leaderboard write hooks
    private readonly ILeaderboardService _leaderboards;
    private readonly CombatConfig _combatConfig;
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
        IEquipmentService equipment,
        IRaidMagicRepository raidMagics,
        IMagicDefinitionProvider magicDefs,
        IMagicService magicService,
        IOptions<MagicConfig> magicConfig,
        IPlayerLegionRepository playerLegions,
        IPlayerLegionSlotRepository legionSlots,
        IUnitDefinitionProvider unitDefs,
        ILegionDefinitionProvider legionDefs,
        IOptions<LegionConfig> legionConfig,
        IPlayerCommanderGearRepository commanderGear,
        IGearDefinitionProvider gearDefs,
        ILegionService legionService,
        ILeaderboardService leaderboards,
        IOptions<CombatConfig> combatConfig,
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
        _equipment       = equipment;
        _raidMagics      = raidMagics;
        _magicDefs       = magicDefs;
        _magicService    = magicService;
        _magicConfig     = magicConfig.Value;
        _playerLegions   = playerLegions;
        _legionSlots     = legionSlots;
        _unitDefs        = unitDefs;
        _legionDefs      = legionDefs;
        _legionConfig    = legionConfig.Value;
        _commanderGear   = commanderGear;
        _gearDefs        = gearDefs;
        _legionService   = legionService;
        _leaderboards    = leaderboards;
        _combatConfig    = combatConfig.Value;
        _random          = random ?? Random.Shared;
    }

    public async Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var allRaids = await _raids.GetAllActiveAsync(ct);
        // Visibility (System 19): list public shared non-Personal raids to everyone, plus the
        // caller's own raids (so they can re-open and share their still-private summons).
        // Private non-Personal raids stay hidden until shared; the raid id remains the invite token.
        var activeRaids = allRaids
            .Where(r => (r.IsPublic && r.Size != RaidSize.Personal) || r.SummonedByPlayerId == playerId)
            .ToList();
        var result = new List<ActiveRaidResponse>(activeRaids.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var raid in activeRaids)
            result.Add(await MapToResponseAsync(raid, playerId, activeRaids.Count, now, ct));

        return result;
    }

    // Shared ActiveRaidResponse projection — used by the list, get-by-id, and share paths so the
    // caller-stat mapping (YourTotalDamage/YourHitCount/YourCurrentTier) stays identical everywhere.
    // totalParticipants only feeds the (placeholder) live-tier computation.
    private async Task<ActiveRaidResponse> MapToResponseAsync(
        ActiveRaid raid, Guid callerId, int totalParticipants, DateTimeOffset now, CancellationToken ct)
    {
        var definition = _raidDefinitions.GetById(raid.RaidDefinitionId);
        var participant = await _participants.FindByRaidAndPlayerAsync(raid.Id, callerId, ct);

        return new ActiveRaidResponse
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
            YourCurrentTier       = ComputeTier(participant?.TotalDamageDealt ?? 0, totalParticipants, participant, null),
            IsPublic              = raid.IsPublic,
        };
    }

    public async Task<IReadOnlyList<CompletedRaidResponse>> GetCompletedRaidsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        const int Limit = 50;
        var rows = await _participants.GetCompletedForPlayerAsync(playerId, Limit, ct);
        var result = new List<CompletedRaidResponse>(rows.Count);

        foreach (var p in rows)
        {
            var raid = p.ActiveRaid!;
            var definition = _raidDefinitions.GetById(raid.RaidDefinitionId);

            var items = string.IsNullOrWhiteSpace(p.ItemsEarnedJson)
                ? new List<ItemGrantDTO>()
                : JsonSerializer.Deserialize<List<ItemGrantDTO>>(p.ItemsEarnedJson) ?? new List<ItemGrantDTO>();

            result.Add(new CompletedRaidResponse
            {
                ActiveRaidId     = raid.Id,
                RaidDefinitionId = raid.RaidDefinitionId,
                Name             = definition?.Name ?? raid.RaidDefinitionId,
                Difficulty       = raid.Difficulty.ToString(),
                DifficultyColor  = DifficultyColors[raid.Difficulty],
                DefeatedAt       = p.RewardedAt!.Value,
                YourTotalDamage  = p.TotalDamageDealt,
                ContributionTier = p.ContributionTier,
                GoldEarned       = p.GoldEarned,
                XpEarned         = p.XpEarned,
                GemsEarned       = p.GemsEarned,
                StatPointsEarned = p.StatPointsEarned,
                ItemsEarned      = items,
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

    public async Task<ActiveRaidResponse?> GetRaidByIdAsync(
        Guid activeRaidId, Guid callerId, CancellationToken ct = default)
    {
        // Join-by-UID: the GUID is the access token, so visibility (IsPublic) is NOT checked here.
        var raid = await _raids.FindByIdWithSummonerAsync(activeRaidId, ct);

        // Not joinable: missing / deleted (repo already filters IsDeleted) / defeated / expired.
        if (raid is null || raid.IsDeleted || raid.IsDefeated || raid.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        // Don't leak someone else's Personal (solo) raid — only the summoner may resolve it.
        if (raid.Size == RaidSize.Personal && raid.SummonedByPlayerId != callerId)
            return null;

        return await MapToResponseAsync(raid, callerId, totalParticipants: 1, DateTimeOffset.UtcNow, ct);
    }

    public async Task<ShareRaidResult> ShareRaidAsync(
        Guid callerId, Guid activeRaidId, CancellationToken ct = default)
    {
        var raid = await _raids.FindByIdWithSummonerAsync(activeRaidId, ct);

        // Treat missing / deleted / defeated / expired uniformly as NotFound.
        if (raid is null || raid.IsDeleted || raid.IsDefeated || raid.ExpiresAt <= DateTimeOffset.UtcNow)
            return new ShareRaidResult
            {
                FailureCode   = ShareRaidFailureCode.NotFound,
                FailureReason = "Raid not found.",
            };

        if (raid.SummonedByPlayerId != callerId)
            return new ShareRaidResult
            {
                FailureCode   = ShareRaidFailureCode.NotSummoner,
                FailureReason = "Only the summoner can share this raid.",
            };

        if (raid.Size == RaidSize.Personal)
            return new ShareRaidResult
            {
                FailureCode   = ShareRaidFailureCode.CannotSharePersonal,
                FailureReason = "Personal raids are solo and cannot be shared.",
            };

        raid.Share();
        await _raids.UpdateAsync(raid, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            callerId, "RaidShared", null,
            $"Shared raid {raid.Id} ({raid.RaidDefinitionId}) [{raid.Size}] to the public list.",
            null), ct);

        var response = await MapToResponseAsync(raid, callerId, totalParticipants: 1, DateTimeOffset.UtcNow, ct);
        return new ShareRaidResult { Success = true, Raid = response };
    }

    public async Task<IReadOnlyList<RaidParticipantRankDto>> GetParticipantsAsync(
        Guid activeRaidId, int top, CancellationToken ct = default)
    {
        if (top < 1)   top = 1;
        if (top > 100) top = 100;

        var rows = await _participants.GetTopByDamageAsync(activeRaidId, top, ct);
        var result = new List<RaidParticipantRankDto>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            result.Add(new RaidParticipantRankDto
            {
                Rank        = i + 1,
                PlayerId    = r.PlayerId,
                DisplayName = r.DisplayName,
                TotalDamage = r.TotalDamageDealt,
                HitCount    = r.HitCount,
            });
        }
        return result;
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

        // 3b. Participant cap enforcement (pre-spend — no stamina cost on rejection).
        //     Personal raids are already gated above (access gate = effective cap of 1).
        //     A small over-cap race is acceptable — not security-critical.
        if (raid.Size != RaidSize.Personal)
        {
            var existingEntry = await _participants.FindByRaidAndPlayerAsync(activeRaidId, playerId, ct);
            if (existingEntry is null && raid.ParticipantCount >= ParticipantCaps[raid.Size])
                return HitFail(RaidHitFailureCode.RaidFull, "This raid is at its participant cap.");
        }

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

        // 6. Stamina cost — computed here so it's available for audit log and on-hit calcs
        //    even though the actual deduction happens inside the advisory-lock transaction.
        int staminaCost = hitSize * definition.StaminaCostPerHit;

        // 7. Apply hit atomically.
        //    AtomicApplyHitAsync begins a PostgreSQL transaction then acquires an advisory
        //    lock on raidId (pg_advisory_xact_lock), which blocks any concurrent call for
        //    the same raid until the transaction ends.  After the lock is held the entity
        //    is reloaded from the DB so the loser always sees the winner's committed state
        //    (IsDefeated=true) and returns false before touching stamina.
        //
        //    Stamina spend is now INSIDE this transaction.  PlayerResourceRepository.AtomicUpdateAsync
        //    detects the ambient transaction and reuses it, so the stamina deduction and the
        //    raid damage commit or roll back together — no phantom stamina loss on crash.
        bool raceCondition       = false;
        bool staminaInsufficient = false;
        long damageFinal         = 0;
        long finalHp             = 0;
        bool finalDefeated       = false;
        bool isCrit              = false;
        double appliedCritMult   = 1.0;
        bool procFired           = false;
        long procBonus           = 0;
        long magicProcBonus      = 0;
        var  magicProcs          = new List<MagicProcDTO>();
        double magicCritBonus    = 0.0; // flat crit-chance addition from CritChanceFlat magics
        long legionPowerTerm        = 0;   // scaled legion contribution added to preProc
        long unitProcBonus          = 0;   // capped total unit-ability proc bonus
        var  unitProcs              = new List<MagicProcDTO>();
        bool commanderProcFired     = false;
        long commanderProcBonus     = 0;   // damage bonus from commander gear proc
        RaidParticipant? participantFinal = null;
        RaidRewards? rewards = null;
        int xpGained         = 0;
        long goldGained      = 0;
        // Running player totals after the hit (captured inside the lock; used to build the response).
        long newPlayerExperience = 0;
        int  newPlayerLevel      = 0;
        long newPlayerGold       = 0;

        var applied = await _raids.AtomicApplyHitAsync(activeRaidId, async lockedRaid =>
        {
            // Re-check under lock — covers the window between the pre-spend check and now.
            if (lockedRaid.IsDefeated || lockedRaid.ExpiresAt < DateTimeOffset.UtcNow)
            {
                raceCondition = true;
                return false;
            }

            // Spend stamina inside the advisory-lock transaction.
            // AtomicUpdateAsync detects the ambient transaction and participates in it,
            // so a failed or rolled-back hit also rolls back the spend.
            var staminaSpent = await _energy.SpendEnergyAsync(
                playerId, ResourceType.Stamina, staminaCost, ct);
            if (!staminaSpent)
            {
                staminaInsufficient = true;
                return false;
            }

            // Load player stats for the damage formula (after successful spend — skipped on failure).
            var player = await _players.FindByIdWithStatsAsync(playerId, ct)
                ?? throw new InvalidOperationException($"Player {playerId} not found after stamina spend.");

            // Compute effective stats — base + gear bonuses + conditional bonuses.
            var combat = await _equipment.GetEffectiveCombatDataAsync(
                playerId, player.Stats!.BaseAttack, player.Stats.BaseDefense, ct);

            var multiplier = 0.85 + _random.NextDouble() * 0.30; // uniform [0.85, 1.15]
            long baseValue = (combat.EffectiveAttack * 4L) + combat.EffectiveDefense;
            long charBase  = Math.Max(1, (long)(baseValue * hitSize * multiplier));

            // Legion power — uses the SAME RNG multiplier and hitSize as charBase (no second roll).
            // Computed inline from the directly-injected repos; does NOT call
            // LegionService.ComputeLegionPowerAsync (that method returns raw power without
            // hitSize/multiplier/PowerScaling, so reusing it would give the wrong result).
            var activeLegion = await _playerLegions.GetActiveAsync(playerId, ct);
            if (activeLegion is not null)
            {
                var legionContentDef = _legionDefs.GetById(activeLegion.LegionDefinitionId);
                var filledSlots      = await _legionSlots.GetForLegionAsync(
                    playerId, activeLegion.LegionDefinitionId, ct);

                double unitSum          = 0;
                double totalLegionBonus = legionContentDef?.PowerBonus ?? 0;  // legion def's own bonus %

                foreach (var slot in filledSlots)
                {
                    var unitDef = _unitDefs.GetById(slot.UnitDefinitionId);
                    if (unitDef is null) continue;

                    var coeffKey = unitDef.UnitType == UnitType.General ? "General" : "Troop";
                    var coeffs   = _legionConfig.UnitCoefficients.TryGetValue(coeffKey, out var c)
                        ? c : new UnitCoefficients { Atk = 1.44, Def = 0.36 };

                    unitSum += coeffs.Atk * unitDef.BaseAttack + coeffs.Def * unitDef.BaseDefense;

                    if (unitDef.UnitType == UnitType.General)
                        totalLegionBonus += unitDef.LegionBonus;
                }

                double bonusFraction  = totalLegionBonus / 100.0;
                double rawLegionPower = unitSum * (1.0 + bonusFraction);
                // Apply PowerScaling (combat-only dial); multiply by same hitSize and multiplier.
                legionPowerTerm = Math.Max(0,
                    (long)(rawLegionPower * _legionConfig.PowerScaling * hitSize * multiplier));
            }

            // preProc = charBase + legionPower. Mount proc, magic procs, and unit procs all
            // scale off this combined base so mounts stay significant (as in DotD).
            long preProc = charBase + legionPowerTerm;
            damageFinal  = preProc;

            // Mount proc — once per hit, adds procPercent × pre-proc base damage as a bonus.
            if (combat.MountProc is not null && _random.NextDouble() < combat.MountProc.ProcChance)
            {
                procBonus   = Math.Max(0, (long)(preProc * combat.MountProc.ProcPercent));
                damageFinal += procBonus;
                procFired   = true;
            }

            // Magic DamageProcs — each applied magic with effectType=DamageProc rolls
            // independently; all bonuses accumulate against preProc then are capped.
            // Loaded inside the advisory lock so we see the current applied-magic list.
            var appliedMagics = await _raidMagics.GetForRaidAsync(activeRaidId, ct);
            long magicBonusRaw = 0;
            foreach (var raidMagic in appliedMagics)
            {
                var magicDef = _magicDefs.GetById(raidMagic.MagicDefinitionId);
                if (magicDef is null || magicDef.EffectType != MagicEffectType.DamageProc) continue;

                double chance = magicDef.ProcChance;
                double amount = magicDef.ProcAmount;

                // Ownership-scaling conditions: evaluate only when declared.
                // Starter magics all have empty conditions — this branch defers inventory load.
                if (magicDef.Conditions.Count > 0)
                {
                    var inventoryItems = await _inventory.GetAllForPlayerAsync(playerId, ct);
                    var ownedById = inventoryItems.ToDictionary(
                        i => i.ItemDefinitionId, i => i.Quantity);
                    var ownedByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var inv in inventoryItems)
                    {
                        var itemDef = _itemDefs.GetById(inv.ItemDefinitionId);
                        if (itemDef is null) continue;
                        foreach (var tag in itemDef.Tags)
                        {
                            ownedByTag.TryGetValue(tag, out int c);
                            ownedByTag[tag] = c + inv.Quantity;
                        }
                    }
                    var evaluated = ConditionalBonusEvaluator.Evaluate(
                        magicDef.Conditions, ownedById, ownedByTag, new HashSet<string>());
                    chance = Math.Min(1.0, chance + evaluated.ProcChanceFlat);
                    amount += evaluated.ProcAmountFlat;
                }

                if (_random.NextDouble() < chance)
                {
                    long bonus = Math.Max(0, (long)(amount * preProc));
                    magicBonusRaw += bonus;
                    magicProcs.Add(new MagicProcDTO { Name = magicDef.Name, Bonus = bonus });
                }
            }

            // Cap aggregate magic proc bonus at MaxAggregateProcBonus × preProc.
            long magicBonusCap = (long)(_magicConfig.MaxAggregateProcBonus * preProc);
            magicProcBonus  = Math.Min(magicBonusRaw, magicBonusCap);
            damageFinal    += magicProcBonus;

            // Unit-ability procs — for each filled legion slot whose unit has a non-passive
            // DamageProc-style ability, roll procChance independently. Passive abilities
            // (IsPassive=true) are already folded into legionPower; rolling them here would
            // double-count. Uses a separate cap (MaxUnitProcBonus × preProc) distinct from
            // the magic cap so the two pools don't interfere.
            if (activeLegion is not null)
            {
                var unitFilledSlots = await _legionSlots.GetForLegionAsync(
                    playerId, activeLegion.LegionDefinitionId, ct);
                long unitBonusRaw = 0;
                foreach (var slot in unitFilledSlots)
                {
                    var unitDef = _unitDefs.GetById(slot.UnitDefinitionId);
                    if (unitDef?.Ability is null || unitDef.IsPassive) continue;

                    double chance = unitDef.Ability.ProcChance;
                    double amount = unitDef.Ability.ProcAmount;

                    if (unitDef.Ability.Conditions.Count > 0)
                    {
                        var invItems  = await _inventory.GetAllForPlayerAsync(playerId, ct);
                        var ownedById = invItems.ToDictionary(i => i.ItemDefinitionId, i => i.Quantity);
                        var ownedByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var inv in invItems)
                        {
                            var itemDef = _itemDefs.GetById(inv.ItemDefinitionId);
                            if (itemDef is null) continue;
                            foreach (var tag in itemDef.Tags)
                            {
                                ownedByTag.TryGetValue(tag, out int cnt);
                                ownedByTag[tag] = cnt + inv.Quantity;
                            }
                        }
                        var evaluated = ConditionalBonusEvaluator.Evaluate(
                            unitDef.Ability.Conditions, ownedById, ownedByTag, new HashSet<string>());
                        chance = Math.Min(1.0, chance + evaluated.ProcChanceFlat);
                        amount += evaluated.ProcAmountFlat;
                    }

                    if (_random.NextDouble() < chance)
                    {
                        long bonus  = Math.Max(0, (long)(amount * preProc));
                        unitBonusRaw += bonus;
                        unitProcs.Add(new MagicProcDTO { Name = unitDef.Name, Bonus = bonus });
                    }
                }
                long unitBonusCap = (long)(_legionConfig.MaxUnitProcBonus * preProc);
                unitProcBonus = Math.Min(unitBonusRaw, unitBonusCap);
                damageFinal  += unitProcBonus;
            }

            // Commander gear proc (Slice 5 — procs-only).
            // The commander's BonusAttack/BonusDefense are NOT added to charBase — they never
            // reach EffectiveCombatData because the row is in player_commander_gear, not
            // player_equipment. Only the ProcChance/ProcPercent are read here.
            // The proc fires off preProc (same as the mount proc) and is added to damageFinal.
            var commanderRow = await _commanderGear.FindAsync(playerId, ct);
            if (commanderRow is not null && !commanderRow.IsDeleted)
            {
                var gearDef = _gearDefs.GetById(commanderRow.GearDefinitionId);
                if (gearDef?.ProcChance is not null && gearDef.ProcPercent is not null
                    && _random.NextDouble() < gearDef.ProcChance.Value)
                {
                    commanderProcBonus  = Math.Max(0, (long)(preProc * gearDef.ProcPercent.Value));
                    damageFinal        += commanderProcBonus;
                    commanderProcFired  = true;
                }
            }

            // CritChanceFlat magics — always-on (ProcChance is ignored); sum all applied.
            foreach (var rm in appliedMagics)
            {
                var mDef = _magicDefs.GetById(rm.MagicDefinitionId);
                if (mDef?.EffectType == MagicEffectType.CritChanceFlat)
                    magicCritBonus += mDef.ProcAmount;
            }

            // Apply discernment crit — adjusted by magic CritChanceFlat, capped at 1.0.
            var crit = _stats.GetCritProfile(player.Stats.DiscernmentInvestment);
            double adjustedCritChance = Math.Min(1.0, crit.Chance + magicCritBonus);
            isCrit = _random.NextDouble() < adjustedCritChance;
            if (isCrit)
            {
                damageFinal      = Math.Max(1, (long)(damageFinal * crit.Multiplier));
                appliedCritMult  = crit.Multiplier;
            }
            else
            {
                appliedCritMult = 1.0;
            }

            // Conditional flat damage percent — applied last, after crit.
            if (combat.FlatDamagePercent > 0)
                damageFinal = Math.Max(1, (long)(damageFinal * (1.0 + combat.FlatDamagePercent)));

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

            // System 17 Slice 4 — leaderboard write hook (inside advisory-lock tx).
            // Rides the same ambient transaction as RecordHit: the board increments and the
            // damage commit are atomic.  NOT reached on the Redis cached-replay path (the early-
            // return at step 4 fires before AtomicApplyHitAsync is entered).  No second damage
            // computation — damageFinal is the authoritative value already computed above.
            await _leaderboards.RecordRaidHitAsync(playerId, damageFinal, DateTimeOffset.UtcNow, ct);

            if (isNew)
                await _participants.CreateAsync(participantFinal, ct);
            else
                await _participants.UpdateAsync(participantFinal, ct);

            // On-hit XP and gold — granted every hit, inside the advisory lock.
            // XP: single uniform roll [min, max] × stamina spent — CombatConfig-driven, not per-stamina.
            // Defaults [1.0, 4.0] preserve the shipped curve (avg ~2.5/stamina ⇒ ~50 on a 20-stamina hit).
            double xpMin  = _combatConfig.XpPerStaminaRollMin;
            double xpMax  = _combatConfig.XpPerStaminaRollMax;
            double xpRoll = xpMin + _random.NextDouble() * (xpMax - xpMin);
            xpGained  = Math.Max(1, (int)Math.Round(staminaCost * xpRoll));
            // Gold mirrors the XP roll — staminaCost × Uniform[min,max] (default 3-8/stamina).
            double goldMin  = _combatConfig.GoldPerStaminaRollMin;
            double goldMax  = _combatConfig.GoldPerStaminaRollMax;
            double goldRoll = goldMin + _random.NextDouble() * (goldMax - goldMin);
            goldGained = Math.Max(1, (long)Math.Round(staminaCost * goldRoll));

            // GoldProc and XpProc magics — modify grants before they're applied.
            // Stacks=false: only the first proc of that effectType is applied per hit.
            bool goldProcFired = false;
            bool xpProcFired   = false;
            foreach (var rm in appliedMagics)
            {
                var mDef = _magicDefs.GetById(rm.MagicDefinitionId);
                if (mDef is null) continue;

                if (mDef.EffectType == MagicEffectType.GoldProc)
                {
                    if (!mDef.Stacks && goldProcFired) continue;
                    if (_random.NextDouble() < mDef.ProcChance)
                    {
                        goldGained    += (long)(mDef.ProcAmount * goldGained);
                        goldProcFired  = true;
                    }
                }
                else if (mDef.EffectType == MagicEffectType.XpProc)
                {
                    if (!mDef.Stacks && xpProcFired) continue;
                    if (_random.NextDouble() < mDef.ProcChance)
                    {
                        xpGained    = Math.Max(1, (int)Math.Round(xpGained * mDef.ProcAmount));
                        xpProcFired = true;
                    }
                }
            }

            var hitLevelUps = player.AddExperience(xpGained, lvl => _stats.XpToNextLevel(lvl));
            player.AddGold(goldGained);
            await _players.UpdateAsync(player, ct);

            // Fire level-up side effects for each level gained (mirrors DistributeKillRewardsAsync)
            foreach (var newLevel in hitLevelUps)
                await _stats.GrantLevelUpPointsAsync(playerId, newLevel, ct);

            // Kill detection and reward distribution — fully inside the advisory lock.
            // On a killing hit, these kill rewards stack on top of the on-hit grant above.
            bool isKill = lockedRaid.CurrentHp == 0;
            if (isKill)
            {
                lockedRaid.MarkDefeated();
                // GetAllForRaidAsync sees the participant saved above (same tx).
                var allParticipants = await _participants.GetAllForRaidAsync(activeRaidId, ct);
                rewards = await DistributeKillRewardsAsync(
                    playerId, player, lockedRaid, definition, allParticipants, ct);
            }

            // Capture running totals after on-hit grant + any kill rewards (player mutated in both).
            newPlayerExperience = player.Experience;
            newPlayerLevel      = player.Level;
            newPlayerGold       = player.Gold;

            finalHp       = lockedRaid.CurrentHp;
            finalDefeated = lockedRaid.IsDefeated;
            return true;
        }, ct);

        // 8. Handle failure outcomes.
        //    Stamina is deducted inside the advisory-lock transaction, so rollback (race or error)
        //    also rolls back the spend — no refund is needed.
        if (!applied)
        {
            if (staminaInsufficient)
                return HitFail(RaidHitFailureCode.InsufficientStamina, "Insufficient stamina.");

            return raceCondition
                ? HitFail(RaidHitFailureCode.RaidAlreadyDefeated,
                    "The creature fell just before your strike landed. The battle is over.")
                : HitFail(RaidHitFailureCode.RaidNotFound,
                    "Raid no longer available.");
        }

        // 9. Audit the successful hit.
        string critSuffix      = isCrit ? $" CRIT x{appliedCritMult:F2}" : string.Empty;
        string procSuffix      = procFired ? $" PROC +{procBonus}" : string.Empty;
        string magicSuffix     = magicProcBonus > 0 ? $" MAGIC +{magicProcBonus}({magicProcs.Count})" : string.Empty;
        string legionSuffix    = legionPowerTerm > 0 ? $" LEGION +{legionPowerTerm}" : string.Empty;
        string unitSuffix      = unitProcBonus  > 0 ? $" UNITPROC +{unitProcBonus}({unitProcs.Count})" : string.Empty;
        string commanderSuffix = commanderProcFired ? $" CMDR +{commanderProcBonus}" : string.Empty;
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidHit", null,
            $"Hit raid {activeRaidId} ({definition.Name}) [{raid.Difficulty}] for {damageFinal} dmg (x{hitSize}){critSuffix}{procSuffix}{magicSuffix}{legionSuffix}{unitSuffix}{commanderSuffix}. " +
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
            XpGained        = xpGained,
            GoldGained      = goldGained,
            // Running totals after this hit (incl. any kill rewards) so the client header/state can
            // reflect the new XP/level/gold without a profile re-fetch on the hot hit path.
            NewPlayerExperience = newPlayerExperience,
            NewPlayerLevel      = newPlayerLevel,
            NewPlayerGold       = newPlayerGold,
            IsCrit          = isCrit,
            CritMultiplier  = appliedCritMult,
            ProcFired       = procFired,
            ProcBonus       = procBonus,
            MagicProcBonus  = magicProcBonus,
            MagicProcs      = magicProcs,
            MagicCritBonus  = magicCritBonus,
            LegionPower          = legionPowerTerm,
            UnitProcBonus        = unitProcBonus,
            UnitProcs            = unitProcs,
            CommanderProcFired   = commanderProcFired,
            CommanderProcBonus   = commanderProcBonus,
        };

        // 10. Store the completed response — replaces the "pending" placeholder.
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
            int participantGemsGranted = 0;
            if (displayTier is not "Participant")
            {
                int gemAmount = (int)Math.Round(definition.BaseGemReward * (double)multiplier);
                if (gemAmount > 0)
                {
                    var gemRef = $"raid:{raid.Id}:{p.PlayerId}";
                    var granted = await _gems.GrantGemsAsync(
                        p.PlayerId, gemAmount, GemTransactionType.RaidReward, gemRef, ct);
                    if (granted)
                    {
                        participantGemsGranted = gemAmount;
                        if (p.PlayerId == callerPlayerId)
                            callerGemsGranted = gemAmount;
                    }
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

                        // Magic drops per threshold — idempotent grant (duplicate = no-op).
                        foreach (var drop in threshold.MagicDrops)
                        {
                            if (_random.NextDouble() < drop.Chance)
                                await _magicService.GrantMagicAsync(p.PlayerId, drop.MagicId, ct);
                        }

                        // Unit drops per threshold — idempotent grant.
                        foreach (var drop in threshold.UnitDrops)
                        {
                            if (_random.NextDouble() < drop.Chance)
                                await _legionService.GrantUnitAsync(p.PlayerId, drop.UnitId, ct);
                        }

                        // Legion drops per threshold — idempotent grant.
                        foreach (var drop in threshold.LegionDrops)
                        {
                            if (_random.NextDouble() < drop.Chance)
                                await _legionService.GrantLegionAsync(p.PlayerId, drop.LegionId, ct);
                        }

                        // Gear drops per threshold — idempotent upsert.
                        foreach (var drop in threshold.GearDrops)
                            await _equipment.GrantGearAsync(p.PlayerId, drop.GearDefinitionId, drop.Quantity, ct);
                    }
                }
            }

            if (unassignedSP > 0)
                await _stats.AddUnassignedPointsAsync(p.PlayerId, unassignedSP, ct);

            // Persist reward summary onto the participant row — same advisory-lock transaction.
            var itemsJson = items.Count > 0
                ? JsonSerializer.Serialize(items)
                : string.Empty;
            p.RecordRewards(
                tier:        displayTier,
                gold:        gold,
                xp:          xp,
                gems:        participantGemsGranted,
                statPoints:  unassignedSP,
                itemsJson:   itemsJson,
                rewardedAt:  DateTimeOffset.UtcNow);
            await _participants.UpdateAsync(p, ct);

            if (p.PlayerId == callerPlayerId)
            {
                callerTier         = displayTier;
                callerMultiplier   = multiplier;
                callerUnassignedSP = unassignedSP;
                callerItems        = items;
                callerLevelUps     = levelUps;
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
