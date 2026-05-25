using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA — reward steps are not wrapped in a single DB transaction. Stamina is spent first;
//        a crash mid-reward is unfair but acceptable. Phase 2: explicit transaction scope.
public sealed class RaidService : IRaidService
{
    private readonly IActiveRaidRepository _raids;
    private readonly IRaidParticipantRepository _participants;
    private readonly IPlayerRepository _players;
    private readonly IPlayerResourceRepository _resources;
    private readonly IEnergyService _energy;
    private readonly IGemService _gems;
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
                Tier                  = definition?.Tier ?? "World",
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SummonRaidResult> SummonRaidAsync(
        Guid playerId, string raidDefinitionId, CancellationToken ct = default)
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

        var expiresAt = DateTimeOffset.UtcNow.AddHours(definition.TimerHours);
        var raid = ActiveRaid.Create(raidDefinitionId, playerId, definition.HpPool, expiresAt);
        await _raids.CreateAsync(raid, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidSummon", null,
            $"Summoned raid '{definition.Name}' (id={raid.Id}). HP={raid.MaxHp}, expires={expiresAt:O}",
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

        // 2. Timer expired — 410 Gone
        if (raid.ExpiresAt < DateTimeOffset.UtcNow)
            return HitFail(RaidHitFailureCode.RaidExpired, "Raid timer has expired.");

        // 3. Already defeated — 409 Conflict
        if (raid.IsDefeated)
            return HitFail(RaidHitFailureCode.RaidAlreadyDefeated, "Raid has already been defeated.");

        // 4. Idempotency — check Redis before any processing, zero reprocessing on duplicate
        var cached = await _hitCache.GetAsync(idempotencyKey, ct);
        if (cached is not null)
            return new RaidHitResult { Success = true, Response = cached };

        // 5. Validate hit size — mirrors DotD ×1 / ×5 / ×20 exactly
        if (hitSize != 1 && hitSize != 5 && hitSize != 20)
            return HitFail(RaidHitFailureCode.InvalidHitSize, "Hit size must be 1, 5, or 20.");

        var definition = _raidDefinitions.GetById(raid.RaidDefinitionId)
            ?? throw new InvalidOperationException($"Raid definition '{raid.RaidDefinitionId}' not found.");

        // 6. Spend stamina — if insufficient, return immediately (zero side effects)
        int staminaCost = hitSize * definition.StaminaCostPerHit;
        var staminaSpent = await _energy.SpendEnergyAsync(playerId, ResourceType.Stamina, staminaCost, ct);
        if (!staminaSpent)
            return HitFail(RaidHitFailureCode.InsufficientStamina, "Insufficient stamina.");

        // 7. Compute damage — server-seeded RNG, never client
        // Formula mirrors DotD: base = ATK×4 + DEF per stamina hit
        var player = await _players.FindByIdWithStatsAsync(playerId, ct)
            ?? throw new InvalidOperationException($"Player {playerId} not found after stamina spend.");

        var multiplier = 0.85 + _random.NextDouble() * 0.30; // uniform [0.85, 1.15]
        long baseValue = (player.Stats!.BaseAttack * 4L) + player.Stats.BaseDefense;
        long damage = Math.Max(1, (long)(baseValue * hitSize * multiplier));

        // 8. Deduct from CurrentHp, floor at 0
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
                playerId, player, raid.Id, definition, allParticipants, ct);
        }

        // 11. Audit
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidHit", null,
            $"Hit raid {activeRaidId} ({definition.Name}) for {damage} damage (×{hitSize}). HP: {raid.CurrentHp}/{raid.MaxHp}. Kill: {isKill}",
            null), ct);

        // Get live stamina for header bar — no second round-trip needed by client
        var newStaminaValue = await _energy.GetCurrentEnergyAsync(playerId, ResourceType.Stamina, ct);
        var staminaResource = await _resources.GetAsync(playerId, ResourceType.Stamina, ct);
        int newStaminaMax = staminaResource?.MaxValue ?? 0;

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
        };

        // 12. Cache in Redis with 24h TTL — duplicate submissions return this response immediately
        await _hitCache.SetAsync(idempotencyKey, response, ct);

        return new RaidHitResult { Success = true, Response = response };
    }

    // -------------------------------------------------------------------
    // KILL REWARD DISTRIBUTION
    // -------------------------------------------------------------------

    private async Task<RaidRewards> DistributeKillRewardsAsync(
        Guid callerPlayerId,
        Player callerPlayer,
        Guid activeRaidId,
        Models.RaidDefinition definition,
        IReadOnlyList<RaidParticipant> allParticipants,
        CancellationToken ct)
    {
        // Assign contribution tiers
        var sorted = allParticipants.OrderByDescending(p => p.TotalDamageDealt).ToList();
        var top3Ids = sorted.Take(3).Select(p => p.PlayerId).ToHashSet();
        int epicCutoff = (int)Math.Ceiling(sorted.Count * 0.10);
        var epicIds = sorted.Take(epicCutoff).Select(p => p.PlayerId).ToHashSet();

        string callerTier = "Participant";
        int callerGemsGranted = 0;
        int levelBefore = callerPlayer.Level;

        foreach (var p in allParticipants)
        {
            string tier;
            if (top3Ids.Contains(p.PlayerId))
                tier = "Legendary";
            else if (epicIds.Contains(p.PlayerId))
                tier = "Epic";
            else if (p.TotalDamageDealt >= definition.MinContributionForLoot)
                tier = "Rare";
            else
                tier = "Participant";

            Player? participantPlayer;
            if (p.PlayerId == callerPlayerId)
            {
                participantPlayer = callerPlayer; // already in memory
            }
            else
            {
                participantPlayer = await _players.FindByIdAsync(p.PlayerId, ct);
                if (participantPlayer is null) continue;
            }

            participantPlayer.AddGold(definition.GoldReward);
            participantPlayer.AddExperience(definition.ExperienceReward);
            await _players.UpdateAsync(participantPlayer, ct);

            if (tier is "Legendary" or "Epic" or "Rare")
            {
                var gemRef = $"raid:{activeRaidId}:{p.PlayerId}";
                var granted = await _gems.GrantGemsAsync(
                    p.PlayerId, definition.GemReward, GemTransactionType.RaidReward, gemRef, ct);

                if (p.PlayerId == callerPlayerId && granted)
                    callerGemsGranted = definition.GemReward;
            }

            if (p.PlayerId == callerPlayerId)
                callerTier = tier;
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            callerPlayerId, "RaidKill", null,
            $"Raid {activeRaidId} ({definition.Name}) defeated. {allParticipants.Count} participants rewarded.",
            null), ct);

        bool leveledUp = callerPlayer.Level > levelBefore;

        return new RaidRewards
        {
            GoldGranted       = definition.GoldReward,
            ExperienceGranted = definition.ExperienceReward,
            GemsGranted       = callerGemsGranted,
            NewPlayerGold     = callerPlayer.Gold,
            NewPlayerExperience = callerPlayer.Experience,
            NewPlayerLevel    = leveledUp ? callerPlayer.Level : null,
            ContributionTier  = callerTier,
        };
    }

    // -------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------

    private static RaidHitResult HitFail(RaidHitFailureCode code, string reason)
        => new RaidHitResult { FailureCode = code, FailureReason = reason };
}
