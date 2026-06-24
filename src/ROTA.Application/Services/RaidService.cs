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
    // Shared boss-gem reward rules (flat amount + chapter-scaled drop chance) — unified across quest
    // bosses and raid bosses (owner 2026-06-23). Lives in QuestConfig; raids read it for parity.
    private readonly QuestConfig _questConfig;
    // System 16 Slice 4 — Gauntlet combat amplifiers (trophies, off-cap auras, strikes, scoring).
    private readonly IPlayerGauntletTrophyRepository _trophyRepo;
    private readonly IGauntletContentProvider        _gauntletContent;
    private readonly IPlayerEventMagicRepository      _playerEventMagics;
    private readonly IPlayerMagicHonorRepository      _playerMagicHonors;
    private readonly IStrikeRepository                _strikes;
    private readonly IGauntletScoringService          _gauntletScoring;
    private readonly IGauntletBattalionService        _battalion;   // System 24 (D8) — Gauntlet strike power
    private readonly GauntletConfig                   _gauntletConfig;
    // System 16 Slice 5 — per-Gauntlet-raid-defeat Token reward (credited inside the advisory-lock tx).
    private readonly IGauntletCurrencyRepository       _gauntletCurrency;
    // System 21 Slice 3b — guild raids: membership gate + contribution accrual; pooled-sigil summon.
    private readonly IGuildMembershipRepository _guildMemberships;
    private readonly IGuildEconomyRepository    _guildEconomy;
    // Ticket 50 — accepted-friend lookup for the FriendsOnly visibility tier in GetActiveRaidsAsync.
    private readonly IFriendshipRepository _friendships;
    // System 22 Phase A — mastery challenge-counter hooks (enlisted in the advisory-lock tx).
    private readonly IMasteryService _mastery;
    // TICKET 46 — achievement metric hook (RaidCompletions, recorded on a kill inside the tx).
    private readonly IAchievementService _achievements;
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
        IPlayerGauntletTrophyRepository trophyRepo,
        IGauntletContentProvider gauntletContent,
        IPlayerEventMagicRepository playerEventMagics,
        IPlayerMagicHonorRepository playerMagicHonors,
        IStrikeRepository strikes,
        IGauntletScoringService gauntletScoring,
        IOptions<GauntletConfig> gauntletConfig,
        IGauntletCurrencyRepository gauntletCurrency,
        IGuildMembershipRepository guildMemberships,
        IGuildEconomyRepository guildEconomy,
        IMasteryService mastery,
        IAchievementService achievements,
        IFriendshipRepository friendships,
        IGauntletBattalionService battalion,
        IOptions<QuestConfig> questConfig,
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
        _questConfig     = questConfig.Value;
        _trophyRepo        = trophyRepo;
        _gauntletContent   = gauntletContent;
        _playerEventMagics = playerEventMagics;
        _playerMagicHonors = playerMagicHonors;
        _strikes           = strikes;
        _gauntletScoring   = gauntletScoring;
        _gauntletConfig    = gauntletConfig.Value;
        _gauntletCurrency  = gauntletCurrency;
        _guildMemberships  = guildMemberships;
        _guildEconomy      = guildEconomy;
        _mastery           = mastery;
        _achievements      = achievements;
        _friendships       = friendships;
        _battalion         = battalion;
        _random          = random ?? Random.Shared;
    }

    public async Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var allRaids = await _raids.GetAllActiveAsync(ct);

        // Ticket 50 — visibility TIERS replace the old IsPublic bool. The list is the set of raids
        // INDEXED for this caller. NOTE: "active raid" (alive + hittable by its GUID) is DISTINCT from
        // "listed raid" — a Private/GuildOnly/FriendsOnly raid is still joinable by id (the invite token),
        // it just doesn't appear in someone else's list. The id-join path is GetRaidByIdAsync.
        //
        // Tiers (a non-Personal raid in Active lifecycle, plus the caller's own raids):
        //   Public       → everyone
        //   GuildOnly    → members of the summoner's guild (compared in-memory off the Include-loaded
        //                  SummonedByPlayer.GuildId — zero extra per-raid queries)
        //   FriendsOnly  → the summoner's accepted friends
        //   own raid     → always visible to the summoner (so they can re-open/share their private summons)
        //
        // The caller's guildId + accepted-friend set are resolved ONCE here, before the in-memory filter.
        // Lootable/Looted raids never list (LifecycleState == Active gate).
        var callerMembership = await _guildMemberships.FindByPlayerAsync(playerId, ct);
        Guid? callerGuildId  = callerMembership?.GuildId;

        var acceptedFriends = await _friendships.ListForPlayerAsync(playerId, FriendshipStatus.Accepted, ct);
        var acceptedFriendIds = new HashSet<Guid>(acceptedFriends.Select(f => f.OtherSide(playerId)));

        // System 16 Slice 7 — Gauntlet ladder stages (GauntletEventId != null) are EXCLUDED from the
        // regular list: they are Personal + caller-owned (so the own-raids branch would otherwise
        // surface them) but are accessed exclusively via GET /api/gauntlet/ladder. Excluding them
        // keeps the normal raid screen free of ladder clutter. (Join-by-id is unaffected — a Gauntlet
        // stage is solo + summoner-gated there too.) Guild raids likewise live on the guild screen.
        var activeRaids = allRaids
            .Where(r => r.GauntletEventId is null
                        && r.GuildId is null
                        && r.LifecycleState == RaidLifecycleState.Active
                        && (r.SummonedByPlayerId == playerId   // own raids always visible (any tier)
                            || (r.Size != RaidSize.Personal
                                && (r.Visibility == RaidVisibility.Public
                                    || (r.Visibility == RaidVisibility.GuildOnly
                                        && callerGuildId is not null
                                        && r.SummonedByPlayer?.GuildId == callerGuildId)
                                    || (r.Visibility == RaidVisibility.FriendsOnly
                                        && acceptedFriendIds.Contains(r.SummonedByPlayerId))))))
            .ToList();
        var result = new List<ActiveRaidResponse>(activeRaids.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var raid in activeRaids)
            result.Add(await MapToResponseAsync(raid, playerId, activeRaids.Count, now, ct));

        // T57 — also surface the caller's defeated raids with UNCLAIMED deferred rewards (Lootable), so
        // they can return and loot after leaving without claiming. Disjoint from the Active list above
        // (different lifecycle). Gauntlet/guild raids are excluded — those are claimed on their own screens.
        var lootable = await _raids.GetLootableUnclaimedForPlayerAsync(playerId, ct);
        if (lootable is not null)
            foreach (var raid in lootable)
                if (raid.GauntletEventId is null && raid.GuildId is null)
                    result.Add(await MapToResponseAsync(raid, playerId, raid.ParticipantCount, now, ct));

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
            // Ticket 50 — visibility tier + lifecycle state on the wire; IsPublic kept as a derived
            // convenience (= Visibility == Public) so the currently-shipped client keeps working.
            Visibility            = raid.Visibility.ToString(),
            LifecycleState        = raid.LifecycleState.ToString(),
            IsPublic              = raid.Visibility == RaidVisibility.Public,
        };
    }

    // System 21 Slice 3b — the caller's guild's active raids (guild screen). Scoped to the caller's
    // guild_id; reuses the shared MapToResponseAsync projection. Returns empty when the caller is
    // guild-less (the guild is resolved server-side, never from client input).
    public async Task<IReadOnlyList<ActiveRaidResponse>> GetGuildRaidsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var membership = await _guildMemberships.FindByPlayerAsync(playerId, ct);
        if (membership is null)
            return Array.Empty<ActiveRaidResponse>();

        var allRaids = await _raids.GetAllActiveAsync(ct);
        var guildRaids = allRaids.Where(r => r.GuildId == membership.GuildId).ToList();
        var result = new List<ActiveRaidResponse>(guildRaids.Count);
        var now = DateTimeOffset.UtcNow;
        foreach (var raid in guildRaids)
            result.Add(await MapToResponseAsync(raid, playerId, guildRaids.Count, now, ct));
        return result;
    }

    // System 21 Slice 3b — officer-gated guild-raid summon. Consumes 1 sigil from the guild pool
    // (atomic + balance-guarded — no overspend under concurrent summons) then creates a Large guild
    // raid stamped with guild_id. All members can then hit it (spending GuildStamina); rewards flow
    // through the existing contribution-tier engine + GuildMembership.ContributionTotal accrual.
    // KNOWN DEBT (accepted Phase-2 multi-step pattern): the pool debit and raid insert are not in one
    // DB transaction. The debit is atomic (no overspend); a crash between the two would, at worst, lose
    // one pooled sigil with no raid (favors the house, not exploitable) — never a free raid.
    public async Task<SummonGuildRaidResult> SummonGuildRaidAsync(
        Guid playerId, string raidDefinitionId, RaidDifficulty difficulty, CancellationToken ct = default)
    {
        var membership = await _guildMemberships.FindByPlayerAsync(playerId, ct);
        if (membership is null)
            return SummonGuildRaidResult.Fail(GuildRaidFailureCode.NotInGuild, "You are not a member of a guild.");
        if (membership.Rank < GuildRank.Officer)
            return SummonGuildRaidResult.Fail(GuildRaidFailureCode.PermissionDenied,
                "Only officers or the leader may summon a guild raid.");

        var definition = _raidDefinitions.GetById(raidDefinitionId);
        if (definition is null || !string.Equals(definition.Tier, "Guild", StringComparison.OrdinalIgnoreCase))
            return SummonGuildRaidResult.Fail(GuildRaidFailureCode.DefinitionNotFound,
                $"Guild raid '{raidDefinitionId}' not found.");

        long finalHp = (long)(definition.BaseHp * HpMultipliers[difficulty]);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(definition.TimerHours);
        // Large = participant cap 50 = the guild member cap, so every member can join the fight.
        var raid = ActiveRaid.Create(raidDefinitionId, playerId, finalHp, expiresAt, difficulty, RaidSize.Large);
        raid.LinkGuild(membership.GuildId);

        // Consume 1 pooled sigil — atomic + balance-guarded. referenceId ties the debit to this raid id.
        var poolRef = $"guildsummon:{raid.Id}";
        var spend = await _guildEconomy.TrySpendPoolAsync(membership.GuildId, 1, poolRef, ct);
        if (spend == GuildPoolSpendOutcome.Insufficient)
            return SummonGuildRaidResult.Fail(GuildRaidFailureCode.InsufficientPool,
                "The guild sigil pool is empty — donate sigils before summoning.");
        if (spend == GuildPoolSpendOutcome.AlreadyCharged)
            // Fresh raid id ⇒ this can't normally happen; treat defensively as a conflict.
            return SummonGuildRaidResult.Fail(GuildRaidFailureCode.Conflict, "Summon conflicted; retry.");

        await _raids.CreateAsync(raid, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "GuildRaidSummon", null,
            $"guild={membership.GuildId} raid={raid.Id} def={raidDefinitionId} [{difficulty}] hp={finalHp} sigilRef={poolRef}",
            null), ct);

        var poolBalance = await _guildEconomy.GetPoolBalanceAsync(membership.GuildId, ct);
        return SummonGuildRaidResult.Ok(new SummonRaidResponse
        {
            ActiveRaidId          = raid.Id,
            Name                  = definition.Name,
            MaxHp                 = raid.MaxHp,
            ExpiresAt             = raid.ExpiresAt,
            TimerRemainingSeconds = (long)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds,
            Difficulty            = difficulty.ToString(),
            DifficultyColor       = DifficultyColors[difficulty],
            Size                  = RaidSize.Large.ToString(),
        }, poolBalance);
    }

    public async Task<IReadOnlyList<CompletedRaidResponse>> GetCompletedRaidsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        const int Limit = 50;
        var rows = await _participants.GetCompletedForPlayerAsync(playerId, Limit, since: null, ct);
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
        // Join-by-UID: the GUID is the access token, so the visibility TIER is NOT checked here.
        var raid = await _raids.FindByIdWithSummonerAsync(activeRaidId, ct);

        // Not resolvable at all: missing / deleted (repo already filters IsDeleted) / expired.
        if (raid is null || raid.IsDeleted || raid.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        // Ticket 50 — a defeated raid is normally not joinable, BUT the summoner may resolve a Lootable
        // raid so the client can show the loot/dismiss screen. Looted raids resolve for no one (they've
        // been dismissed). Non-summoners still get null on any defeated raid.
        // TICKET-3-061126 — once the summoner has CLAIMED, the raid is gone for them too: a claimed
        // Lootable raid must not stay reachable by id (the list already drops it via RewardedAt).
        if (raid.IsDefeated)
        {
            bool summonerViewingLootable =
                raid.LifecycleState == RaidLifecycleState.Lootable && raid.SummonedByPlayerId == callerId;
            if (!summonerViewingLootable)
                return null;

            var callerParticipant = await _participants.FindByRaidAndPlayerAsync(activeRaidId, callerId, ct);
            if (callerParticipant is not null && callerParticipant.RewardsClaimed)
                return null;
        }

        // Don't leak someone else's Personal (solo) raid — only the summoner may resolve it.
        if (raid.Size == RaidSize.Personal && raid.SummonedByPlayerId != callerId)
            return null;

        return await MapToResponseAsync(raid, callerId, totalParticipants: 1, DateTimeOffset.UtcNow, ct);
    }

    public async Task<ShareRaidResult> ShareRaidAsync(
        Guid callerId, Guid activeRaidId, RaidVisibility visibility = RaidVisibility.Public,
        CancellationToken ct = default)
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

        // Ticket 50 — there is no un-share path: sharing only moves between Public/GuildOnly/FriendsOnly.
        // A Private target is meaningless here (the raid is already effectively private when unshared);
        // coerce it to Public for back-compat with the no-body share call.
        if (visibility == RaidVisibility.Private)
            visibility = RaidVisibility.Public;

        // GuildOnly requires the summoner to actually be in a guild (resolved server-side, never trusted
        // from the client). Friends/Public have no such precondition.
        if (visibility == RaidVisibility.GuildOnly)
        {
            var membership = await _guildMemberships.FindByPlayerAsync(callerId, ct);
            if (membership is null)
                return new ShareRaidResult
                {
                    FailureCode   = ShareRaidFailureCode.NotInGuild,
                    FailureReason = "You must be in a guild to share a raid guild-only.",
                };
        }

        raid.ShareTo(visibility);
        await _raids.UpdateAsync(raid, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            callerId, "RaidShared", null,
            $"Shared raid {raid.Id} ({raid.RaidDefinitionId}) [{raid.Size}] to the [{visibility}] list.",
            null), ct);

        var response = await MapToResponseAsync(raid, callerId, totalParticipants: 1, DateTimeOffset.UtcNow, ct);
        return new ShareRaidResult { Success = true, Raid = response };
    }

    // Ticket 50 + T57 — per-PARTICIPANT loot CLAIM. T57 reverses T50's "rewards already granted on the
    // killing hit, Loot is a pure dismiss": rewards (gold/gems/stat-points/items) are now COMPUTED on the
    // killing hit but GRANTED here, when each participant presses Loot. XP/level-ups stay immediate at
    // kill. Each participant claims exactly once (RewardedAt latches); a re-press is idempotent (the
    // summary is returned, nothing re-granted). The raid stays Lootable while ANY participant still has
    // an unclaimed reward; once the LAST participant claims, it flips Lootable→Looted (Loot()) so it
    // drops out of every "lootable" index instead of lingering forever.
    public async Task<LootRaidResult> LootRaidAsync(
        Guid callerId, Guid activeRaidId, CancellationToken ct = default)
    {
        var raid = await _raids.FindByIdWithSummonerAsync(activeRaidId, ct);
        // Missing / deleted / fully dismissed (legacy Looted) → gone from the player's perspective.
        if (raid is null || raid.IsDeleted || raid.LifecycleState == RaidLifecycleState.Looted)
            return new LootRaidResult
            {
                FailureCode   = LootRaidFailureCode.NotFound,
                FailureReason = "Raid not found.",
            };

        // Must be defeated (Lootable) to claim — a still-Active raid can't be looted.
        if (raid.LifecycleState != RaidLifecycleState.Lootable)
            return new LootRaidResult
            {
                FailureCode   = LootRaidFailureCode.NotLootable,
                FailureReason = "This raid is still active — defeat it before looting.",
            };

        // Only a participant has rewards to claim.
        var participant = await _participants.FindByRaidAndPlayerAsync(activeRaidId, callerId, ct);
        if (participant is null)
            return new LootRaidResult
            {
                FailureCode   = LootRaidFailureCode.NotFound,
                FailureReason = "You did not take part in this raid.",
            };

        // Grant the deferred rewards exactly once. T57: gold + XP are NOT here — they were granted
        // on-hit (the killing hit). Loot grants EVERYTHING else: gems, stat-points, inventory items,
        // and the magic/unit/legion/gear collection drops.
        //
        // The claim is race- and crash-safe: the latch is a conditional UPDATE (rewarded_at IS NULL)
        // and every grant rides the SAME advisory-lock transaction (keyed on the participant row), so
        // two concurrent Loot presses serialize — the loser latches zero rows and grants nothing —
        // and a crash mid-grant rolls the latch back together with the rewards (nothing lost,
        // nothing duplicated). A re-press after a committed claim skips here via RewardsClaimed.
        if (!participant.RewardsClaimed)
        {
            var claimed = await _raids.AtomicWithAdvisoryLockAsync(participant.Id, async () =>
            {
                if (!await _participants.TryClaimRewardsAsync(participant.Id, DateTimeOffset.UtcNow, ct))
                    return false; // a concurrent claim won the latch — grant nothing, summary still returned

                if (participant.GemsEarned > 0)
                    await _gems.GrantGemsAsync(callerId, participant.GemsEarned,
                        GemTransactionType.RaidReward, $"raid:{raid.Id}:{callerId}", ct);
                if (participant.StatPointsEarned > 0)
                    await _stats.AddUnassignedPointsAsync(callerId, participant.StatPointsEarned, ct);
                if (!string.IsNullOrEmpty(participant.ItemsEarnedJson))
                {
                    var pendingItems = JsonSerializer.Deserialize<List<ItemGrantDTO>>(participant.ItemsEarnedJson)
                                       ?? new List<ItemGrantDTO>();
                    var throwaway = new List<ItemGrantDTO>();
                    foreach (var it in pendingItems)
                        await GrantInventoryItemAsync(callerId, it.ItemId, it.Quantity, throwaway, ct);
                }
                // T57 — deferred collection drops (idempotent grants).
                if (!string.IsNullOrEmpty(participant.PendingDropsJson))
                {
                    var drops = JsonSerializer.Deserialize<List<PendingDrop>>(participant.PendingDropsJson)
                                ?? new List<PendingDrop>();
                    foreach (var d in drops)
                    {
                        switch (d.Kind)
                        {
                            case "Magic":  await _magicService.GrantMagicAsync(callerId, d.Id, ct); break;
                            case "Unit":   await _legionService.GrantUnitAsync(callerId, d.Id, ct); break;
                            case "Legion": await _legionService.GrantLegionAsync(callerId, d.Id, ct); break;
                            case "Gear":   await _equipment.GrantGearAsync(callerId, d.Id, d.Quantity, ct); break;
                        }
                    }
                }

                await _auditLog.AppendAsync(AuditLog.Create(
                    callerId, "RaidLootClaimed", null,
                    $"Claimed deferred rewards for raid {raid.Id} ({raid.RaidDefinitionId}): " +
                    $"gems +{participant.GemsEarned}, SP +{participant.StatPointsEarned}.", null), ct);
                return true;
            }, ct);

            // In-memory only, for the response below — the durable latch was the conditional UPDATE.
            participant.MarkRewardsClaimed(DateTimeOffset.UtcNow);

            // Fully-claimed expiry: once no participant has an unclaimed reward, dismiss the raid
            // (Lootable→Looted) so it no longer lingers in the lootable indexes forever. Loot() is
            // guarded Lootable-only and does NOT soft-delete (FK/history intact). Only the caller that
            // actually WON the latch checks this — the just-committed claim is then visible to the query.
            if (claimed)
            {
                var allParticipants = await _participants.GetAllForRaidAsync(activeRaidId, ct);
                var stillUnclaimed  = allParticipants.Any(p => p.RewardedAt == null);
                if (!stillUnclaimed && raid.LifecycleState == RaidLifecycleState.Lootable)
                {
                    raid.Loot();
                    await _raids.UpdateAsync(raid, ct);
                }
            }
        }

        var rewards  = BuildClaimedRewards(participant);
        var response = await MapToResponseAsync(raid, callerId, totalParticipants: raid.ParticipantCount, DateTimeOffset.UtcNow, ct);
        return new LootRaidResult { Success = true, Raid = response, Rewards = rewards };
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

        // 3a-guild. Guild raid access gate (System 21 Slice 3b) — only members of the owning guild may
        //           strike. Guild raids are non-Personal (Large), so the Personal gate above never applies.
        if (raid.GuildId is not null)
        {
            var striker = await _guildMemberships.FindByGuildAndPlayerAsync(raid.GuildId.Value, playerId, ct);
            if (striker is null)
                return HitFail(RaidHitFailureCode.AccessDenied,
                    "Only members of this guild may strike its raid.");
        }

        // 3b. Participant cap enforcement (pre-spend — no stamina cost on rejection).
        //     Personal raids are already gated above (access gate = effective cap of 1).
        //     A small over-cap race is acceptable — not security-critical.
        if (raid.Size != RaidSize.Personal)
        {
            var existingEntry = await _participants.FindByRaidAndPlayerAsync(activeRaidId, playerId, ct);
            if (existingEntry is null && raid.ParticipantCount >= ParticipantCaps[raid.Size])
                return HitFail(RaidHitFailureCode.RaidFull, "This raid is at its participant cap.");
        }

        // 4. Validate hit size BEFORE reserving the idempotency slot — a rejected request must not
        //    burn the client's key for 24h (the placeholder would answer every retry with
        //    "already in progress" until the TTL expired).
        // SECURITY (exploit audit 2026-06-14, finding B): the Gauntlet fork is a FLAT single strike
        // with hitSize-independent damage, so a multi-hit size would multiply the XP/gold reward basis
        // (staminaCost) for one strike. Force hitSize to 1 on the Gauntlet path.
        bool isGauntlet = raid.GauntletEventId is not null;
        if (isGauntlet && hitSize != 1)
            return HitFail(RaidHitFailureCode.InvalidHitSize, "Gauntlet strikes are always a single hit.");
        if (hitSize != 1 && hitSize != 5 && hitSize != 20)
            return HitFail(RaidHitFailureCode.InvalidHitSize, "Hit size must be 1, 5, or 20.");

        // 5. Atomic idempotency: reserve the slot (SET NX) or return cached response.
        //    Closes the check-then-set race from the previous GetAsync/SetAsync pattern.
        //    Audit fix: scope the cache key to THIS player + raid. The client-supplied key is a bare
        //    string (e.g. a per-session counter), so an unscoped "raidhit:{key}" let player B's "1"
        //    collide with player A's "1" — B's hit would silently return A's cached response. An empty
        //    key (no idempotency requested) gets a fresh GUID so successive hits never self-collide;
        //    an oversized key is hashed (deterministic, so retries still dedupe) to bound Redis keys.
        var clientKey = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey;
        if (clientKey.Length > 100)
            clientKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(clientKey)));
        var scopedKey = $"{playerId:N}:{activeRaidId:N}:{clientKey}";
        var (slotAcquired, existingResponse) = await _hitCache.TryAcquireSlotAsync(scopedKey, ct);
        if (!slotAcquired)
        {
            if (existingResponse is not null)
                return new RaidHitResult { Success = true, Response = existingResponse };
            // Concurrent in-flight duplicate — treat as duplicate-in-progress.
            return HitFail(RaidHitFailureCode.RaidNotFound, "Request already in progress.");
        }

        var definition = _raidDefinitions.GetById(raid.RaidDefinitionId)
            ?? throw new InvalidOperationException($"Raid definition '{raid.RaidDefinitionId}' not found.");

        // 6. Stamina cost — computed here so it's available for audit log and on-hit calcs
        //    even though the actual deduction happens inside the advisory-lock transaction.
        int staminaCost = hitSize * definition.StaminaCostPerHit;

        // 6a. Gauntlet fork (System 16 Slice 4 / D6) — Gauntlet raids spend a FLAT 1 STRIKE per hit
        //     (hitSize forced to 1 at step 4) instead of Stamina; non-Gauntlet raids keep the stamina
        //     path. The debit happens inside the advisory-lock tx (atomic with the hit). isGauntlet is
        //     resolved at step 4.
        // System 21 Slice 3b — guild raids spend GuildStamina (= hit size) instead of Stamina/Strikes.
        bool isGuildRaid = raid.GuildId is not null;
        // D6 (System 24) — Gauntlet strikes are a FLAT 1 ticket: hit sizes don't apply on the
        // Gauntlet fork, and strikeCost is only ever read on the Gauntlet (strike-spend) path.
        int strikeCost = 1;
        // SECURITY (exploit audit 2026-06-14, finding A — CRITICAL): key the strike-spend reference off
        // the SAME normalized scopedKey the Redis slot uses (empty→fresh GUID, oversized→SHA256), NOT the
        // raw client key. Otherwise a blank/constant IdempotencyKey produced a CONSTANT strikeRef while
        // the Redis layer (fresh GUID per blank key) let every hit proceed → SpendAsync returned
        // AlreadyCharged → full battalion damage for ZERO strikes (free ladder climb). scopedKey already
        // dedupes a genuine client retry at the Redis slot before SpendAsync is reached.
        string strikeRef = $"strikespend:{scopedKey}";

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
        bool strikesInsufficient = false;   // System 16 Slice 4 — Gauntlet strike spend failed
        bool guildStaminaInsufficient = false; // System 21 Slice 3b — guild-raid GuildStamina spend failed
        long offCapBonus         = 0;       // System 16 Slice 4 — off-cap Wrath/Blessing aura bonus
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
        long wrathLegionBonus       = 0;   // System 22 — Wrath mastery's marginal legion power (display)
        long bulwarkBonus           = 0;   // System 22 — Bulwark mastery's marginal guild-raid damage (display)
        long unitProcBonus          = 0;   // capped total unit-ability proc bonus
        var  unitProcs              = new List<MagicProcDTO>();
        bool commanderProcFired     = false;
        long commanderProcBonus     = 0;   // damage bonus from commander gear proc
        RaidParticipant? participantFinal = null;
        RaidRewards? rewards = null;
        long xpGained        = 0;   // int32-overflow-audit Unit 2 — widened (RaidHitResponse.XpGained is long)
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

            // Spend the action currency inside the advisory-lock transaction.
            // Gauntlet raids (System 16 Slice 4) spend STRIKES via the tx-safe StrikeRepository.SpendAsync
            // (it participates in this ambient tx, so the debit commits/rolls back atomically with the
            // hit and never touches the change tracker). Non-Gauntlet raids spend Stamina exactly as
            // before — AtomicUpdateAsync detects the ambient tx and participates in it, so a failed or
            // rolled-back hit also rolls back the spend. Only ONE currency is ever spent per hit.
            if (isGauntlet)
            {
                var strikeOutcome = await _strikes.SpendAsync(playerId, strikeCost, strikeRef, ct);
                if (strikeOutcome == StrikeSpendOutcome.Insufficient)
                {
                    strikesInsufficient = true;
                    return false;   // no stamina is spent on the Gauntlet path
                }
                // Charged (debited now) or AlreadyCharged (idempotent replay within the tx) → proceed.
            }
            else if (isGuildRaid)
            {
                // System 21 Slice 3b — guild raids spend GuildStamina (cost = hit size, since
                // guild_raids.json sets StaminaCostPerHit=1). Same tx-safe AtomicUpdateAsync path as
                // Stamina, so the debit commits/rolls back atomically with the hit. Strikes never apply.
                var guildStaminaSpent = await _energy.SpendEnergyAsync(
                    playerId, ResourceType.GuildStamina, staminaCost, ct);
                if (!guildStaminaSpent)
                {
                    guildStaminaInsufficient = true;
                    return false;
                }
            }
            else
            {
                var staminaSpent = await _energy.SpendEnergyAsync(
                    playerId, ResourceType.Stamina, staminaCost, ct);
                if (!staminaSpent)
                {
                    staminaInsufficient = true;
                    return false;
                }
            }

            // Load player stats for the damage formula (after successful spend — skipped on failure).
            var player = await _players.FindByIdWithStatsAsync(playerId, ct)
                ?? throw new InvalidOperationException($"Player {playerId} not found after stamina spend.");

            // Compute effective stats — base + gear bonuses + conditional bonuses.
            var combat = await _equipment.GetEffectiveCombatDataAsync(
                playerId, player.Stats!.BaseAttack, player.Stats.BaseDefense, ct);

            // T56 — health cost for this hit: flat per difficulty for ordinary/guild raids; a Defense-
            // scaled mob-damage curve for the Gauntlet (ramps past ~stage 200). Best-effort drain inside
            // the hit tx, clamped at 0 — it never blocks the hit (PHASE-2: optional 0-health gate).
            int healthCost = ComputeHealthCost(isGauntlet, lockedRaid, combat.EffectiveDefense);
            if (healthCost > 0)
                await _energy.DrainAsync(playerId, ResourceType.Health, healthCost, ct);

            // System 22 Phase A — mastery modifiers (combat: Wrath +% legion power, Bulwark +% guild-raid
            // damage; loot: Hoard +% gold). ONE mastery-state read for the whole hit; a mastery-less player
            // gets all-neutral → a byte-for-byte unchanged hit.
            var masteryMods = await _mastery.GetModifiersAsync(playerId, ct);

            var multiplier = 0.85 + _random.NextDouble() * 0.30; // uniform [0.85, 1.15]

            // System 24 (D8) — Gauntlet FULL-REPLACE: the strike-damage base IS battalion power × the
            // RNG band (owner decision — NO character base, legion, procs, trophy, auras, or PowerScaling).
            // Crit still applies below; every additive block further down is gated off for the Gauntlet.
            long battalionPower = isGauntlet
                ? await _battalion.ComputePowerAsync(playerId, combat.EffectiveAttack, combat.EffectiveDefense, ct)
                : 0L;

            long baseValue = (combat.EffectiveAttack * 4L) + combat.EffectiveDefense;
            long charBase  = isGauntlet
                ? Math.Max(1L, (long)(battalionPower * multiplier))
                : Math.Max(1, (long)(baseValue * hitSize * multiplier));

            // Legion power — uses the SAME RNG multiplier and hitSize as charBase (no second roll).
            // Computed inline from the directly-injected repos; does NOT call
            // LegionService.ComputeLegionPowerAsync (that method returns raw power without
            // hitSize/multiplier/PowerScaling, so reusing it would give the wrong result).
            // Gauntlet (D8) skips the legion entirely — battalion power already IS the base, so the
            // legion block, its trophy/PowerScaling stages, and the unit-proc block below all no-op.
            var activeLegion = isGauntlet ? null : await _playerLegions.GetActiveAsync(playerId, ct);
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

                // (Wrath, System 22 Phase A) — adds its percent into the legion bonus sum, exactly like a
                // General's LegionBonus, so it flows once through bonusFraction (additive). LOCKED RULE:
                // Wrath NEVER applies in the Gauntlet (GetModifiersAsync has no raid context, so the gate
                // lives here). Applied BEFORE the trophy stage + PowerScaling so it touches legion power once.
                double wrathPercent = isGauntlet ? 0.0 : masteryMods.Combat.WrathLegionPercent;
                totalLegionBonus += wrathPercent;

                double bonusFraction  = totalLegionBonus / 100.0;
                double rawLegionPower = unitSum * (1.0 + bonusFraction);

                // (A) Gauntlet Trophy multiplier (System 16 Slice 4) — highest-only, applies to EVERY
                // raid (DotD: trophies "boost the power of all your legions"). Loaded here, inside the
                // active-legion block, so legion-less players never run the query (and get zero change).
                // Own several → only the best fraction applies (NOT additive); no trophies → ×1.0 → a
                // byte-for-byte unchanged hit. Inserted BEFORE PowerScaling, per the locked spec.
                var ownedTrophies = await _trophyRepo.GetForPlayerAsync(playerId, ct);
                double maxTrophyFraction = 0.0;
                foreach (var owned in ownedTrophies)
                {
                    var trophyDef = _gauntletContent.GetTrophyById(owned.GauntletTrophyId);
                    if (trophyDef is not null && trophyDef.LegionPowerBonusFraction > maxTrophyFraction)
                        maxTrophyFraction = trophyDef.LegionPowerBonusFraction;
                }
                rawLegionPower *= 1.0 + maxTrophyFraction;

                // Apply PowerScaling (combat-only dial); multiply by same hitSize and multiplier.
                legionPowerTerm = Math.Max(0,
                    (long)(rawLegionPower * _legionConfig.PowerScaling * hitSize * multiplier));

                // Wrath marginal (display only): rawLegionPower is linear in bonusFraction, so Wrath's
                // contribution is unitSum × (wrathPercent/100) carried through the same trophy + scaling stages.
                wrathLegionBonus = Math.Max(0, (long)(unitSum * (wrathPercent / 100.0)
                    * (1.0 + maxTrophyFraction) * _legionConfig.PowerScaling * hitSize * multiplier));
            }

            // preProc = charBase + legionPower. Mount proc, magic procs, and unit procs all
            // scale off this combined base so mounts stay significant (as in DotD).
            long preProc = charBase + legionPowerTerm;
            damageFinal  = preProc;

            // Mount proc — once per hit, adds procPercent × pre-proc base damage as a bonus.
            if (!isGauntlet && combat.MountProc is not null && _random.NextDouble() < combat.MountProc.ProcChance)
            {
                procBonus   = Math.Max(0, (long)(preProc * combat.MountProc.ProcPercent));
                damageFinal += procBonus;
                procFired   = true;
            }

            // Magic DamageProcs — each applied magic with effectType=DamageProc rolls
            // independently; all bonuses accumulate against preProc then are capped.
            // Loaded inside the advisory lock so we see the current applied-magic list.
            // Gauntlet (D8) applies no magics — an empty list zeroes BOTH the magic damage procs here
            // and the CritChanceFlat magic-crit loop below, so the Gauntlet crit is pure Discernment.
            var appliedMagics = isGauntlet
                ? (IReadOnlyList<RaidMagic>)System.Array.Empty<RaidMagic>()
                : await _raidMagics.GetForRaidAsync(activeRaidId, ct);
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

            // (B) Off-cap Wrath/Blessing auras — REMOVED from the Gauntlet (System 24 D8 full-replace).
            // They were Gauntlet-ONLY, so there is no longer any off-cap aura path at all; offCapBonus
            // stays 0. (Former owners' honor still gates the rank-magic hand-off elsewhere — not here.)

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
            var commanderRow = isGauntlet ? null : await _commanderGear.FindAsync(playerId, ct);
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
            // int32-overflow-audit Unit 2 — DiscernmentInvestment is long; crit math is bounded (clamped
            // chance/multiplier), so the (int) narrowing for GetCritProfile is safe.
            var crit = _stats.GetCritProfile((int)player.Stats.DiscernmentInvestment);
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

            // Conditional flat damage percent — applied last, after crit. (Bulwark, System 22 Phase A)
            // stacks additively here on GUILD RAIDS ONLY, already hard-capped in GetCombatModifiersAsync.
            double bulwarkFraction = lockedRaid.GuildId is not null ? masteryMods.Combat.BulwarkGuildDamageFraction : 0.0;
            // Gauntlet (D8) full-replace: no gear FlatDamagePercent / Bulwark on the Gauntlet path.
            double flatDamageFraction = isGauntlet ? 0.0 : combat.FlatDamagePercent + bulwarkFraction;
            if (flatDamageFraction > 0)
            {
                long beforeFlat = damageFinal;
                damageFinal = Math.Max(1, (long)(damageFinal * (1.0 + flatDamageFraction)));
                // Bulwark marginal (display only): the flat bonuses are additive in the multiplier.
                bulwarkBonus = bulwarkFraction > 0 ? (long)(beforeFlat * bulwarkFraction) : 0;
            }

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

            // System 22 Phase A — mastery challenge counters (RaidHit + RaidDamageDealt), enlisted in
            // this advisory-lock tx exactly like the leaderboard hook. Replay-safe for free: the Redis
            // cached-replay early-return fires before AtomicApplyHitAsync is entered.
            await _mastery.RecordActivityAsync(playerId, MasteryActivityType.RaidHit, 1, ct: ct);
            await _mastery.RecordActivityAsync(playerId, MasteryActivityType.RaidDamageDealt, damageFinal, ct: ct);

            // (D) Gauntlet score update (System 16 Slice 4) — GAUNTLET RAIDS ONLY. Rides this ambient
            // advisory-lock tx (atomic with the hit/RecordHit). Reads nothing extra: damageFinal is the
            // authoritative value already accumulated into TotalDamageDealt above. No-op if the player
            // has no GauntletEntry (hasn't joined the event) — correct: you must join to be scored.
            // Non-Gauntlet hits never call it.
            if (lockedRaid.GauntletEventId is not null)
                await _gauntletScoring.UpdateScoreAsync(
                    playerId, lockedRaid.GauntletEventId.Value, damageFinal, DateTimeOffset.UtcNow, ct);

            if (isNew)
                await _participants.CreateAsync(participantFinal, ct);
            else
                await _participants.UpdateAsync(participantFinal, ct);

            // System 21 Slice 3b — accrue this hit's damage to the member's lifetime guild contribution
            // (the "damage dealt" metric), inside the advisory-lock tx (atomic with the hit/RecordHit).
            // Guild raids only; the access gate already required current membership, so this is a no-op
            // only in the rare race where the striker left the guild mid-hit.
            if (lockedRaid.GuildId is not null)
            {
                var contributor = await _guildMemberships.FindByGuildAndPlayerAsync(
                    lockedRaid.GuildId.Value, playerId, ct);
                if (contributor is not null)
                {
                    contributor.AddContribution(damageFinal);
                    await _guildMemberships.UpdateAsync(contributor, ct);
                    // System 22 Phase A — guild-raid contribution mastery counter (enlisted in this tx).
                    await _mastery.RecordActivityAsync(
                        playerId, MasteryActivityType.GuildRaidContribution, damageFinal, ct: ct);
                }
            }

            // On-hit XP and gold — granted every hit, inside the advisory lock.
            // XP scales with STAMINA SPENT, not player level (level only raises XpToNextLevel). Owner
            // 2026-06-14: SUMMED per-stamina roll — each point of stamina independently rolls Uniform
            // [min, max] and the rolls are summed (batching-invariant, tight spread ⇒ ~50 on a 20-stamina
            // hit). Defaults [1.0, 4.0]. Quests run the same model over energy (QuestService.RollEnergyXp).
            xpGained = Math.Max(1L, ResourceReward.RollSummed(
                _random, staminaCost, _combatConfig.XpPerStaminaRollMin, _combatConfig.XpPerStaminaRollMax));
            // Gold mirrors the XP roll — staminaCost × Uniform[min,max] (default 3-8/stamina).
            double goldMin  = _combatConfig.GoldPerStaminaRollMin;
            double goldMax  = _combatConfig.GoldPerStaminaRollMax;
            double goldRoll = goldMin + _random.NextDouble() * (goldMax - goldMin);
            // System 22 Phase A — Hoard +% gold (global). Applied to the on-hit gold; the GoldEarned
            // mastery counter below then reflects the boosted amount. Neutral (×1.0) for non-Hoard players.
            goldGained = Math.Max(1, (long)Math.Round(staminaCost * goldRoll * masteryMods.Loot.HoardGoldMultiplier));

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
                        xpGained    = Math.Max(1L, (long)Math.Round(xpGained * mDef.ProcAmount));
                        xpProcFired = true;
                    }
                }
            }

            // T59 — xmin-retry chokepoint: a simultaneous quest completion writing the same players
            // row no longer loses this hit's gold/XP (or vice versa). Same tracked instance as
            // `player`, so the response totals below reflect the committed values.
            var hitLevelUps = await _players.MutateWithRetryAsync(playerId, pl =>
            {
                var ups = pl.AddExperience(xpGained, lvl => _stats.XpToNextLevel(lvl));
                pl.AddGold(goldGained);
                return ups;
            }, ct);

            // System 22 Phase A — gold-earned mastery counter (on-hit gold, enlisted in this tx).
            await _mastery.RecordActivityAsync(playerId, MasteryActivityType.GoldEarned, goldGained, ct: ct);

            // Fire level-up side effects for each level gained (mirrors DistributeKillRewardsAsync)
            foreach (var newLevel in hitLevelUps)
                await _stats.GrantLevelUpPointsAsync(playerId, newLevel, ct);

            // Kill detection and reward distribution — fully inside the advisory lock.
            // On a killing hit, these kill rewards stack on top of the on-hit grant above.
            bool isKill = lockedRaid.CurrentHp == 0;
            if (isKill)
            {
                lockedRaid.MarkDefeated();

                // System 22 Phase A — RaidKill mastery counter for the killer (caller). Idempotent via a
                // per-(raid,player) referenceId so a re-processed kill never double-counts; enlisted in this tx.
                await _mastery.RecordActivityAsync(
                    playerId, MasteryActivityType.RaidKill, 1, $"mastery:kill:{activeRaidId}:{playerId}", ct);

                // TICKET 46 — RaidCompletions achievement counter for the killer, mirroring the mastery
                // kill hook. Idempotent via the same per-(raid,player) referenceId; enlisted in this tx.
                await _achievements.RecordProgressAsync(
                    playerId, AchievementMetric.RaidCompletions, 1, $"ach:raidkill:{activeRaidId}:{playerId}", ct);

                // System 16 Slice 5 — per-Gauntlet-raid-defeat reward. GAUNTLET RAIDS ONLY (gated on
                // GauntletEventId). Gauntlet raids are Personal/solo, so the killer is the lone
                // contributor — credit playerId. Both grants ride THIS advisory-lock tx (atomic with
                // the kill / MarkDefeated) and are plain append CreateAsync (EF Add+SaveChanges, no
                // ChangeTracker.Clear — tx-safe inside the lock). Each is guarded by a ReferenceExists
                // pre-check against a per-(raid,player) referenceId so a re-processed kill (e.g. a
                // crashed-then-retried hit that re-enters this block) never double-credits — the same
                // money-bug discipline as the settlement payout.
                if (lockedRaid.GauntletEventId is not null)
                {
                    var strikeDefeatRef = $"gauntletdefeat:{activeRaidId}:{playerId}:strikes";
                    if (!await _strikes.ReferenceExistsAsync(
                            playerId, StrikeTransactionType.RaidDefeat, strikeDefeatRef, ct))
                    {
                        await _strikes.CreateAsync(StrikeTransaction.Create(
                            playerId, _gauntletConfig.StrikesPerDefeat,
                            StrikeTransactionType.RaidDefeat, strikeDefeatRef), ct);
                    }

                    var tokenDefeatRef = $"gauntletdefeat:{activeRaidId}:{playerId}:token";
                    if (!await _gauntletCurrency.ReferenceExistsAsync(
                            playerId, GauntletCurrency.Token,
                            GauntletCurrencyTransactionType.RaidDefeatReward, tokenDefeatRef, ct))
                    {
                        await _gauntletCurrency.CreateAsync(GauntletCurrencyTransaction.Create(
                            playerId, GauntletCurrency.Token, 1,
                            GauntletCurrencyTransactionType.RaidDefeatReward, tokenDefeatRef), ct);
                    }

                    // T76 — record the ladder-stage defeat (the PRIMARY ranking metric: highest
                    // stage completed). Atomic GREATEST in SQL, rides this advisory-lock tx;
                    // naturally idempotent (a re-processed kill can never raise the peak twice).
                    var stageDef = _gauntletContent.GetGauntletRaidByDefinitionId(lockedRaid.RaidDefinitionId);
                    if (stageDef is not null)
                        await _gauntletScoring.RecordStageDefeatAsync(
                            playerId, lockedRaid.GauntletEventId.Value, stageDef.LadderStage, ct);
                }

                // GetAllForRaidAsync sees the participant saved above (same tx).
                var allParticipants = await _participants.GetAllForRaidAsync(activeRaidId, ct);
                // System 22 Phase A follow-up — Hoard scales the KILLER's chance-based threshold drops
                // (item/magic/unit/legion). masteryMods is the killer's, already loaded once above inside
                // this lock, so no extra per-hit read. The Gauntlet never drops threshold loot, but it
                // also gets no Hoard scaling here (neutral 1.0) for safety.
                double killerHoardDrop = isGauntlet ? 1.0 : masteryMods.Loot.HoardDropMultiplier;
                rewards = await DistributeKillRewardsAsync(
                    playerId, player, lockedRaid, definition, allParticipants, killerHoardDrop, ct);
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

            // System 16 Slice 4 — Gauntlet strike spend rolled back with the tx; no damage, no score.
            if (strikesInsufficient)
                return HitFail(RaidHitFailureCode.InsufficientStrikes, "Insufficient strikes.");

            // System 21 Slice 3b — guild-raid GuildStamina spend rolled back with the tx; no damage.
            if (guildStaminaInsufficient)
                return HitFail(RaidHitFailureCode.InsufficientGuildStamina, "Insufficient guild stamina.");

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
        // System 16 Slice 4 — Gauntlet off-cap aura + strike-spend audit suffixes (Gauntlet raids only).
        string offCapSuffix    = offCapBonus > 0 ? $" OFFCAP +{offCapBonus}" : string.Empty;
        string strikeSuffix    = isGauntlet ? $" STRIKES -{strikeCost}" : string.Empty;
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "RaidHit", null,
            $"Hit raid {activeRaidId} ({definition.Name}) [{raid.Difficulty}] for {damageFinal} dmg (x{hitSize}){critSuffix}{procSuffix}{magicSuffix}{legionSuffix}{unitSuffix}{commanderSuffix}{offCapSuffix}{strikeSuffix}. " +
            $"HP: {finalHp}/{raid.MaxHp}. Kill: {finalDefeated}",
            null), ct);

        // Guild raids spend GuildStamina (not Stamina), so the hit response's stamina fields must
        // report the pool that actually decremented — otherwise the "Guild Stamina" label shows the
        // untouched regular Stamina and the bar appears frozen until a profile re-fetch.
        var staminaResourceType = isGuildRaid ? ResourceType.GuildStamina : ResourceType.Stamina;
        var newStaminaValue = await _energy.GetCurrentEnergyAsync(playerId, staminaResourceType, ct);
        var staminaResource = await _resources.GetAsync(playerId, staminaResourceType, ct);
        int newStaminaMax   = staminaResource?.MaxValue ?? 0;
        // T56 — Health is drained per hit; surface the live value/max so the client can patch the
        // health bar without a profile re-fetch (otherwise it freezes after the first hit).
        var newHealthValue  = await _energy.GetCurrentEnergyAsync(playerId, ResourceType.Health, ct);
        var healthResource  = await _resources.GetAsync(playerId, ResourceType.Health, ct);
        int newHealthMax    = healthResource?.MaxValue ?? 0;
        string callerTier   = rewards?.ContributionTier ?? "Participant";
        // System 16 Slice 4 — post-spend Strike balance (0 for non-Gauntlet raids; they spend Stamina).
        long newStrikeBalance = isGauntlet ? await _strikes.GetBalanceAsync(playerId, ct) : 0;

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
            NewHealthValue  = newHealthValue,
            NewHealthMax    = newHealthMax,
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
            // System 16 Slice 4 — Gauntlet amplifier surfacing (0 on non-Gauntlet raids).
            OffCapAuraBonus      = offCapBonus,
            NewStrikeBalance     = newStrikeBalance,
            // System 22 Phase A — mastery combat surfacing (0 when no Wrath legion bonus / non-guild raid).
            WrathLegionBonus     = wrathLegionBonus,
            BulwarkBonus         = bulwarkBonus,
        };

        // 10. Store the completed response — replaces the "pending" placeholder. Uses the same
        //     player+raid-scoped key reserved in step 4.
        await _hitCache.StoreResultAsync(scopedKey, response, ct);

        return new RaidHitResult { Success = true, Response = response };
    }

    // KILL REWARD DISTRIBUTION

    // T56 — per-hit health cost. Ordinary/guild raids pay a flat cost by difficulty; the Gauntlet pays a
    // Defense-scaled mob-damage curve that ramps past the configured stage (~200). Always returns ≥ 1 for
    // a Gauntlet hit so the mob is always noticeable; ordinary raids use the configured per-difficulty cost.
    private int ComputeHealthCost(bool isGauntlet, ActiveRaid raid, long effectiveDefense)
    {
        if (isGauntlet)
        {
            int stage = ParseGauntletStage(raid.RaidDefinitionId);
            double raw = _combatConfig.GauntletHealthBaseDamage
                       + stage * _combatConfig.GauntletHealthPerStage
                       + Math.Max(0, stage - _combatConfig.GauntletHealthRampStage) * _combatConfig.GauntletHealthRampPerStage;
            double reduction = Math.Min(_combatConfig.GauntletHealthDefenseReductionMax,
                                        effectiveDefense * _combatConfig.GauntletHealthDefenseReductionPerPoint);
            return (int)Math.Max(1, Math.Round(raw * (1.0 - reduction)));
        }
        return _combatConfig.RaidHealthCostByDifficulty.TryGetValue(raid.Difficulty.ToString(), out var c)
            ? c
            : _combatConfig.RaidHealthCostDefault;
    }

    // Parse N from a "gauntlet_stage_N" definition id; 0 when it isn't a Gauntlet stage.
    private static int ParseGauntletStage(string raidDefinitionId)
    {
        const string prefix = "gauntlet_stage_";
        if (!string.IsNullOrEmpty(raidDefinitionId)
            && raidDefinitionId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(raidDefinitionId.Substring(prefix.Length), out var n))
            return n;
        return 0;
    }

    private async Task<RaidRewards> DistributeKillRewardsAsync(
        Guid callerPlayerId,
        Player callerPlayer,
        ActiveRaid raid,
        RaidDefinition definition,
        IReadOnlyList<RaidParticipant> allParticipants,
        double callerHoardDropMultiplier,
        CancellationToken ct)
    {
        // System 22 Phase A follow-up — Hoard scales CHANCE-based threshold drops, mirroring
        // ProcessQuestLootAsync.Scale: chance × Hoard, clamped at MaxThresholdDropChance, where the
        // clamp never *lowers* an already-higher base (a base ≥ the cap, e.g. 1.0, is unchanged).
        // MINIMAL SAFE SLICE (per the ticket): only the KILLER's drops are Hoard-scaled — their mastery
        // multiplier is already in hand from the hit's single GetModifiersAsync read. Scaling every
        // participant's drops would need a mastery read per participant INSIDE the advisory-lock tx
        // (the exact per-participant kill-loop cost System 22 deferred), so non-killer participants keep
        // their base chance (HoardDropMultiplier 1.0 → no-op).
        double ScaleDropChance(double baseChance, double hoardMultiplier)
        {
            double boosted = baseChance * hoardMultiplier;
            double cap = Math.Max(baseChance, _combatConfig.MaxThresholdDropChance);
            return Math.Min(boosted, cap);
        }
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

        // Unified boss-gem chance scales by the raid's chapter, parsed from its zone-boss id
        // ("raid_c3z1b" → 3). Non-chapter raids (World/Event/Guild) → the full-goal chapter (max chance).
        var chMatch = System.Text.RegularExpressions.Regex.Match(definition.Id ?? "", @"c(\d+)z\d+");
        int raidChapter = chMatch.Success ? int.Parse(chMatch.Groups[1].Value) : _questConfig.GemChanceFullChapter;

        foreach (var p in allParticipants)
        {
            var (tier, multiplier) = tierAssignments.GetValueOrDefault(p.PlayerId, ("Participant", 0.25m));
            string displayTier = tier.StartsWith("Legendary") ? "Legendary" : tier;

            Player? participantPlayer = p.PlayerId == callerPlayerId
                ? callerPlayer
                : await _players.FindByIdAsync(p.PlayerId, ct);
            if (participantPlayer is null) continue;

            // Gold scaled by difficulty (same multiplier as HP) then by tier.
            double diffMult = HpMultipliers[raid.Difficulty];
            long gold = (long)Math.Round(definition.BaseGoldReward * diffMult * (double)multiplier);
            // triage boss-raid-kill-xp-inflated: NO on-kill XP bonus. The killing hit already earns its
            // normal per-hit (stamina-spent) XP in HitRaidAsync; the kill itself grants ZERO extra XP.
            int xp = 0;

            // T57 — XP + GOLD are IMMEDIATE on-hit rewards (the killing hit grants both, alongside the
            // per-hit on-hit gold/XP). EVERYTHING ELSE — gems, stat-points, items, and the magic/unit/
            // legion/gear drops — is DEFERRED: computed + stored on the participant row now, GRANTED when
            // that participant presses Loot.
            // T59 — xmin-retry chokepoint: each participant may be mid-quest/mid-hit elsewhere; a
            // stale full-column save here silently lost their gold/XP (kill-reward last-write-wins).
            var levelUps = await _players.MutateWithRetryAsync(p.PlayerId, pl =>
            {
                pl.AddGold(gold);
                return pl.AddExperience(xp, lvl => _stats.XpToNextLevel(lvl));
            }, ct);

            // Fire level-up side effects for each level gained
            foreach (var newLevel in levelUps)
                await _stats.GrantLevelUpPointsAsync(p.PlayerId, newLevel, ct);

            // Gems — UNIFIED boss-gem model (owner 2026-06-23): a flat BossGemRewardAmount on a
            // per-chapter-scaled CHANCE (by the raid's chapter + difficulty), identical to quest bosses.
            // Replaces the old BaseGemReward × contribution-tier grant (that JSON field is now vestigial).
            // Rare+ contributors only. T57: COMPUTED now, GRANTED at Loot (idempotent ref reused there).
            int participantGemsGranted = 0;
            if (displayTier is not "Participant"
                && _random.NextDouble() < _questConfig.ResolveBossGemChance(raidChapter, raid.Difficulty.ToString()))
            {
                participantGemsGranted = _questConfig.BossGemRewardAmount;
                if (p.PlayerId == callerPlayerId)
                    callerGemsGranted = participantGemsGranted;
            }

            // Loot table — stat points, items, and collection drops (cumulative thresholds). T57: ALL of
            // these are DEFERRED to Loot — rolled now, granted on the participant's claim.
            int unassignedSP = 0;
            var items = new List<ItemGrantDTO>();
            var pendingDrops = new List<PendingDrop>();
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

                    // System 22 Phase A follow-up — only the killer's chance drops are Hoard-scaled
                    // (see ScaleDropChance). Every other participant keeps their base chance.
                    double hoardForThisPlayer = p.PlayerId == callerPlayerId ? callerHoardDropMultiplier : 1.0;

                    // Cumulative: collect all threshold tiers the player qualifies for
                    foreach (var threshold in diffLoot.ThresholdRewards
                        .OrderBy(t => t.ContributionPercent)
                        .Where(t => contribPct >= t.ContributionPercent))
                    {
                        unassignedSP += (int)Math.Round(threshold.UnassignedStatPoints * (double)multiplier);

                        foreach (var drop in threshold.ItemDrops)
                        {
                            if (_random.NextDouble() < ScaleDropChance(drop.Chance, hoardForThisPlayer))
                            {
                                int qty = (int)Math.Max(1, Math.Round(drop.Quantity * (double)multiplier));
                                // T57 — roll only; the item is GRANTED to inventory at Loot.
                                BuildItemGrantDTO(drop.ItemId, qty, items);
                            }
                        }

                        // T57 — magic/unit/legion/gear drops are DEFERRED: rolled now, stored as PendingDrop,
                        // granted (idempotently) at Loot.
                        foreach (var drop in threshold.MagicDrops)
                            if (_random.NextDouble() < ScaleDropChance(drop.Chance, hoardForThisPlayer))
                                pendingDrops.Add(new PendingDrop { Kind = "Magic", Id = drop.MagicId, Quantity = 1 });

                        foreach (var drop in threshold.UnitDrops)
                            if (_random.NextDouble() < ScaleDropChance(drop.Chance, hoardForThisPlayer))
                                pendingDrops.Add(new PendingDrop { Kind = "Unit", Id = drop.UnitId, Quantity = 1 });

                        foreach (var drop in threshold.LegionDrops)
                            if (_random.NextDouble() < ScaleDropChance(drop.Chance, hoardForThisPlayer))
                                pendingDrops.Add(new PendingDrop { Kind = "Legion", Id = drop.LegionId, Quantity = 1 });

                        // Gear drops are unconditional at a qualifying threshold (no chance roll) — a
                        // GUARANTEED drop, so it is intentionally NOT Hoard-scaled.
                        foreach (var drop in threshold.GearDrops)
                            pendingDrops.Add(new PendingDrop { Kind = "Gear", Id = drop.GearDefinitionId, Quantity = drop.Quantity });
                    }
                }
            }

            // T57 — stat points are DEFERRED (granted at Loot), not added here.

            // Persist the COMPUTED (pending) reward summary onto the participant row — same advisory-lock
            // transaction. RewardedAt stays null until the participant claims via Loot. Gold/XP were just
            // granted (on-hit), but GoldEarned/XpEarned are still stored for the completed-raid history.
            var itemsJson = items.Count > 0
                ? JsonSerializer.Serialize(items)
                : string.Empty;
            var pendingDropsJson = pendingDrops.Count > 0
                ? JsonSerializer.Serialize(pendingDrops)
                : string.Empty;
            p.RecordPendingRewards(
                tier:             displayTier,
                gold:             gold,
                xp:               xp,
                gems:             participantGemsGranted,
                statPoints:       unassignedSP,
                itemsJson:        itemsJson,
                pendingDropsJson: pendingDropsJson);
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
            ExperienceGranted        = 0,   // triage boss-raid-kill-xp-inflated: no on-kill XP bonus — per-hit (stamina-spent) XP only

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

    // HELPERS

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

    // T57 — build the loot DTO WITHOUT granting to inventory. The roll OUTCOME is fixed + stored at kill
    // (in items_earned_json); the actual inventory grant happens at Loot. Mirrors the DTO tail of
    // GrantInventoryItemAsync.
    private void BuildItemGrantDTO(string itemDefId, int quantity, List<ItemGrantDTO> into)
    {
        var def = _itemDefs.GetById(itemDefId);
        if (def is not null)
            into.Add(new ItemGrantDTO
            {
                ItemId   = itemDefId,
                ItemName = def.Name,
                Quantity = quantity,
                Rarity   = def.Rarity.ToString(),
                ArtKey   = def.ArtKey,
            });
    }

    // T57 — the Loot CLAIM summary: only what Loot actually grants (gems / stat-points / items). Gold +
    // XP are 0 here because they were on-hit rewards (granted on the killing hit), not claimed at Loot.
    private RaidRewards BuildClaimedRewards(RaidParticipant p)
    {
        var items = string.IsNullOrEmpty(p.ItemsEarnedJson)
            ? new List<ItemGrantDTO>()
            : (JsonSerializer.Deserialize<List<ItemGrantDTO>>(p.ItemsEarnedJson) ?? new List<ItemGrantDTO>());
        return new RaidRewards
        {
            GoldGranted                 = 0,   // on-hit reward, not claimed at Loot
            ExperienceGranted           = 0,   // on-hit reward, not claimed at Loot
            GemsGranted                 = p.GemsEarned,
            ContributionTier            = p.ContributionTier,
            // RaidRewards.UnassignedStatPointsGranted stays int (not in Unit-2 scope); a per-claim SP
            // award is bounded well within int32, so the narrowing is safe.
            UnassignedStatPointsGranted = (int)p.StatPointsEarned,
            ItemsGranted                = items,
        };
    }

    private static RaidHitResult HitFail(RaidHitFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
