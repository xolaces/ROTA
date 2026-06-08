using System.Globalization;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// System 22 Phase A — Masteries read/compute service. Modifier values are plain numbers consumed at
/// the existing combat/loot hooks (no new combat path; not ConditionalBonus rows). Tier-up evaluation
/// and activity recording arrive in Slice 4; re-spec in Slice 3.
/// </summary>
public sealed class MasteryService : IMasteryService
{
    private static readonly MasteryAncient[] AllAncients = Enum.GetValues<MasteryAncient>();
    private const string LiveKey = "live";

    private readonly IMasteryDefinitionProvider _defs;
    private readonly IPlayerMasteryRepository _masteryRepo;
    private readonly IPlayerMasteryActivityRepository _activityRepo;
    private readonly IPlayerRepository _players;
    private readonly ILeaderboardEntryRepository _leaderboard;
    private readonly IMasteryRespecRepository _respec;
    private readonly IGemService _gems;
    private readonly IMasteryRespecCapStore _capStore;
    private readonly IAuditLogRepository _auditLog;
    private readonly MasteryConfig _config;

    public MasteryService(
        IMasteryDefinitionProvider defs,
        IPlayerMasteryRepository masteryRepo,
        IPlayerMasteryActivityRepository activityRepo,
        IPlayerRepository players,
        ILeaderboardEntryRepository leaderboard,
        IMasteryRespecRepository respec,
        IGemService gems,
        IMasteryRespecCapStore capStore,
        IAuditLogRepository auditLog,
        IOptions<MasteryConfig> config)
    {
        _defs         = defs;
        _masteryRepo  = masteryRepo;
        _activityRepo = activityRepo;
        _players      = players;
        _leaderboard  = leaderboard;
        _respec       = respec;
        _gems         = gems;
        _capStore     = capStore;
        _auditLog     = auditLog;
        _config       = config.Value;
    }

    // ── GetMasteriesAsync ─────────────────────────────────────────────────────

    public async Task<MasteryOverviewResponse> GetMasteriesAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        var pledge = player?.ActivePledgeAncient;

        var counters = await LoadCountersAsync(playerId, ct);
        // Evaluate earned tier-ups off the hot path (this read is one of the two trigger points).
        var masteries = await EvaluateTierUpsCoreAsync(playerId, counters, ct);
        var levels = masteries.ToDictionary(m => m.Ancient, m => m.Level);

        var ancientDtos = new List<MasteryAncientDto>(AllAncients.Length);
        foreach (var ancient in AllAncients)
        {
            var def = _defs.Get(ancient);
            int level = LevelOf(levels, ancient);
            double global = _defs.GlobalPercent(ancient, level);
            bool pledged = pledge == ancient;
            double effective = ClampForDisplay(ancient, pledged ? global * _config.PledgeMultiplier : global);

            ancientDtos.Add(new MasteryAncientDto
            {
                Ancient          = ancient.ToString(),
                Name             = def?.Name ?? ancient.ToString(),
                Theme            = def?.Theme ?? string.Empty,
                Level            = level,
                IsPledged        = pledged,
                GlobalPercent    = global,
                EffectivePercent = effective,
                NextTier         = BuildTierProgress(ancient, level, counters),
            });
        }

        int rating = ComputeRating(levels);
        return new MasteryOverviewResponse
        {
            Ancients     = ancientDtos,
            ActivePledge = pledge?.ToString(),
            Rating       = new MasteryRatingDto { Active = rating, Lifetime = rating },
            Titles       = BuildTitles(levels),
            RespecStatus = await BuildRespecStatusAsync(playerId, DateTimeOffset.UtcNow, ct),
        };
    }

    private async Task<MasteryRespecStatusDto> BuildRespecStatusAsync(Guid playerId, DateTimeOffset now, CancellationToken ct)
    {
        bool freeMonthly = !await _respec.ReferenceExistsAsync(playerId, MonthlyRef(playerId, now), ct);
        bool paidWeekly  = !await _respec.ReferenceExistsAsync(playerId, PaidRef(playerId, now), ct);
        return new MasteryRespecStatusDto
        {
            FreeMonthlyAvailable = freeMonthly,
            PaidWeeklyAvailable  = paidWeekly,
            GemCost              = _config.RespecGemCost,
        };
    }

    // ── Modifier reads (consumed by Slices 5/6/7) ─────────────────────────────

    public async Task<MasteryCombatModifiers> GetCombatModifiersAsync(Guid playerId, CancellationToken ct = default)
    {
        var (levels, pledge) = await LoadLevelsAndPledgeAsync(playerId, ct);
        return ComputeCombatModifiers(levels, pledge);
    }

    public async Task<MasteryLootModifiers> GetLootModifiersAsync(Guid playerId, CancellationToken ct = default)
    {
        var (levels, pledge) = await LoadLevelsAndPledgeAsync(playerId, ct);
        return ComputeLootModifiers(levels, pledge);
    }

    public async Task<MasteryModifiers> GetModifiersAsync(Guid playerId, CancellationToken ct = default)
    {
        var (levels, pledge) = await LoadLevelsAndPledgeAsync(playerId, ct);
        return new MasteryModifiers(ComputeCombatModifiers(levels, pledge), ComputeLootModifiers(levels, pledge));
    }

    // ── Rating-board snapshot ─────────────────────────────────────────────────

    public async Task<int> SnapshotRatingBoardAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var ratings = await _masteryRepo.GetAllRatingsAsync(ct);
        foreach (var row in ratings)
        {
            int rating = ComputeRating(row.Levels);
            await _leaderboard.SetValueAsync(row.PlayerId, LeaderboardBoard.MasteryRatingActive,
                LeaderboardPeriod.Live, LiveKey, rating, now, ct);
            await _leaderboard.SetValueAsync(row.PlayerId, LeaderboardBoard.MasteryRatingLifetime,
                LeaderboardPeriod.Live, LiveKey, rating, now, ct);
        }
        return ratings.Count;
    }

    // ── Activity recording + tier-up leveling (Slice 4) ───────────────────────

    public async Task RecordActivityAsync(Guid playerId, MasteryActivityType activityType, long amount = 1,
        string? referenceId = null, CancellationToken ct = default)
    {
        if (amount <= 0) return;

        if (referenceId is not null)
        {
            // Exactly-once: pre-check the idempotency ledger (avoids a unique-violation that would
            // poison an ambient tx — e.g. the raid advisory-lock tx). Insert + increment ride that tx.
            if (await _activityRepo.HasEventAsync(playerId, activityType, referenceId, ct)) return;
            await _activityRepo.RecordEventAsync(playerId, activityType, referenceId, ct);
        }

        await _activityRepo.IncrementAsync(playerId, activityType, amount, ct);
    }

    public async Task EvaluateTierUpsAsync(Guid playerId, CancellationToken ct = default)
    {
        var counters = await LoadCountersAsync(playerId, ct);
        await EvaluateTierUpsCoreAsync(playerId, counters, ct);
    }

    private async Task<Dictionary<MasteryActivityType, long>> LoadCountersAsync(Guid playerId, CancellationToken ct)
        => (await _activityRepo.GetForPlayerAsync(playerId, ct)).ToDictionary(a => a.ActivityType, a => a.Counter);

    private async Task<IReadOnlyList<PlayerMastery>> EvaluateTierUpsCoreAsync(
        Guid playerId, IReadOnlyDictionary<MasteryActivityType, long> counters, CancellationToken ct)
    {
        var masteries = await _masteryRepo.EnsureAllAsync(playerId, ct);
        foreach (var m in masteries)
        {
            // Cumulative counters vs increasing per-tier thresholds — a veteran may jump several tiers.
            while (m.Level < PlayerMasteryMax)
            {
                var tier = _defs.GetTierChallenge(m.Ancient, m.Level);
                if (tier is null) break;
                bool met = tier.Checklist.All(c =>
                    (counters.TryGetValue(c.ActivityType, out var cur) ? cur : 0) >= c.Threshold);
                if (!met) break;

                m.LevelUp();
                await _masteryRepo.UpsertAsync(m, ct);
                await _auditLog.AppendAsync(AuditLog.Create(
                    playerId, "MasteryLevelUp", null, $"{m.Ancient} -> L{m.Level}", null), ct);
            }
        }
        return masteries;
    }

    // ── Re-spec (LOSSLESS pledge change) ──────────────────────────────────────

    public async Task<MasteryRespecResult> RespecAsync(Guid playerId, MasteryAncient toAncient, CancellationToken ct = default)
    {
        if (_defs.Get(toAncient) is null)
            return MasteryRespecResult.Fail(MasteryRespecFailureCode.AncientNotFound, $"Unknown Ancient '{toAncient}'.");

        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null)
            return MasteryRespecResult.Fail(MasteryRespecFailureCode.PlayerNotFound, "Player not found.");

        if (player.ActivePledgeAncient == toAncient)
            return MasteryRespecResult.Fail(MasteryRespecFailureCode.AlreadyPledged, $"Already pledged to {toAncient}.");

        var from = player.ActivePledgeAncient;
        var now  = DateTimeOffset.UtcNow;

        // 1. Free first pledge to this Ancient (one per Ancient, ever — also covers a future awakened Ancient).
        var unlockRef = $"respec:unlock:{playerId}:{toAncient}";
        if (!await _respec.ReferenceExistsAsync(playerId, unlockRef, ct))
            return await ApplySwapAsync(player, from, toAncient, MasteryRespecKind.NewAncientUnlock, 0, unlockRef, ct);

        // 2. Free monthly swap.
        var monthRef = MonthlyRef(playerId, now);
        if (!await _respec.ReferenceExistsAsync(playerId, monthRef, ct))
            return await ApplySwapAsync(player, from, toAncient, MasteryRespecKind.FreeMonthly, 0, monthRef, ct);

        // 3. Paid weekly swap. The cap store is the fast gate; the gem week-bucket refId is the hard backstop.
        if (await _capStore.IsPaidWeeklyUsedAsync(playerId, ct))
            return MasteryRespecResult.Fail(MasteryRespecFailureCode.WeeklyCapReached, "Paid re-spec already used this week.");

        var paidRef = PaidRef(playerId, now);
        var outcome = await _gems.SpendGemsAsync(playerId, _config.RespecGemCost, GemTransactionType.MasteryRespec, paidRef, ct);
        switch (outcome)
        {
            case GemSpendOutcome.InsufficientBalance:
                return MasteryRespecResult.Fail(MasteryRespecFailureCode.InsufficientGems,
                    $"Insufficient gems (need {_config.RespecGemCost}).");

            case GemSpendOutcome.AlreadyProcessed:
                // The gem ledger already holds this week's paid charge → cap reached. Sync the fast gate.
                // PHASE-2: a crash between the gem charge and the pledge commit leaves the charge banked
                // without the swap until next week (rare; the strict cap is the deliberate priority).
                await _capStore.MarkPaidWeeklyUsedAsync(playerId, ct);
                return MasteryRespecResult.Fail(MasteryRespecFailureCode.WeeklyCapReached, "Paid re-spec already used this week.");

            case GemSpendOutcome.Charged:
                await _capStore.MarkPaidWeeklyUsedAsync(playerId, ct);
                return await ApplySwapAsync(player, from, toAncient, MasteryRespecKind.Paid, _config.RespecGemCost, paidRef, ct);

            default:
                return MasteryRespecResult.Fail(MasteryRespecFailureCode.None, "Unexpected re-spec outcome.");
        }
    }

    private async Task<MasteryRespecResult> ApplySwapAsync(
        Player player, MasteryAncient? from, MasteryAncient to,
        MasteryRespecKind kind, int gemCost, string referenceId, CancellationToken ct)
    {
        // LOSSLESS: only the pledge pointer changes; the four mastery level tracks are untouched.
        await _respec.CreateAsync(MasteryRespecTransaction.Create(player.Id, kind, from, to, gemCost, referenceId), ct);
        player.SetPledge(to);
        await _players.UpdateAsync(player, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, $"MasteryRespec:{kind}", null,
            $"pledge {(from?.ToString() ?? "none")} -> {to} (cost={gemCost}, ref={referenceId})", null), ct);

        return MasteryRespecResult.Ok(kind.ToString(), gemCost, to.ToString());
    }

    private static string MonthlyRef(Guid playerId, DateTimeOffset now) => $"respec:free:{playerId}:{now.UtcDateTime:yyyy-MM}";
    private static string PaidRef(Guid playerId, DateTimeOffset now)
        => $"respec:paid:{playerId}:{ISOWeek.GetYear(now.UtcDateTime)}-W{ISOWeek.GetWeekOfYear(now.UtcDateTime):D2}";

    // ── Overall Mastery Rating — Formula B (pure) ─────────────────────────────

    public int ComputeRating(IReadOnlyDictionary<MasteryAncient, int> levels)
    {
        var all = AllAncients.Select(a => LevelOf(levels, a)).ToArray();
        int rating = all.Sum();
        if (all.All(x => x >= 2)) rating += 3;
        if (all.All(x => x >= 3)) rating += 5;
        if (all.All(x => x >= 4)) rating += 8;
        if (all.All(x => x >= 5)) rating += 12;
        rating += 2 * all.Count(x => x == PlayerMasteryMax);
        if (_config.IncludeWeakestPillarFloor) rating += 2 * all.Min();
        return rating;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const int PlayerMasteryMax = 5;

    private static int LevelOf(IReadOnlyDictionary<MasteryAncient, int> levels, MasteryAncient ancient)
        => levels.TryGetValue(ancient, out var v) ? v : 1;

    private async Task<(Dictionary<MasteryAncient, int> Levels, MasteryAncient? Pledge)> LoadLevelsAndPledgeAsync(
        Guid playerId, CancellationToken ct)
    {
        var rows = await _masteryRepo.GetForPlayerAsync(playerId, ct);
        var levels = rows.ToDictionary(r => r.Ancient, r => r.Level);
        var player = await _players.FindByIdAsync(playerId, ct);
        return (levels, player?.ActivePledgeAncient);
    }

    private double EffectivePercent(IReadOnlyDictionary<MasteryAncient, int> levels, MasteryAncient? pledge, MasteryAncient ancient)
    {
        int level = LevelOf(levels, ancient);
        double global = _defs.GlobalPercent(ancient, level);
        return pledge == ancient ? global * _config.PledgeMultiplier : global;
    }

    private double ClampForDisplay(MasteryAncient ancient, double percent)
        => ancient == MasteryAncient.Bulwark ? Math.Min(percent, _config.BulwarkMaxGuildDamagePercent) : percent;

    private MasteryCombatModifiers ComputeCombatModifiers(IReadOnlyDictionary<MasteryAncient, int> levels, MasteryAncient? pledge)
    {
        double wrathPercent = EffectivePercent(levels, pledge, MasteryAncient.Wrath);
        double bulwarkPercent = Math.Min(
            EffectivePercent(levels, pledge, MasteryAncient.Bulwark),
            _config.BulwarkMaxGuildDamagePercent);
        return new MasteryCombatModifiers(wrathPercent, bulwarkPercent / 100.0);
    }

    private MasteryLootModifiers ComputeLootModifiers(IReadOnlyDictionary<MasteryAncient, int> levels, MasteryAncient? pledge)
    {
        double hoardPercent = EffectivePercent(levels, pledge, MasteryAncient.Hoard);
        // Capped breadth micro-bonus, only once all four Ancients are ≥ 3 (off by default).
        if (AllAncients.All(a => LevelOf(levels, a) >= 3))
            hoardPercent += Math.Min(_config.BreadthMicroBonusPercent, _config.BreadthMicroBonusMaxPercent);

        double hoardFraction = hoardPercent / 100.0;
        double discFraction = EffectivePercent(levels, pledge, MasteryAncient.Discernment) / 100.0;

        return new MasteryLootModifiers(
            HoardDropMultiplier:             1.0 + hoardFraction,
            HoardGoldMultiplier:             1.0 + hoardFraction,
            DiscernmentSigilFindMultiplier:  1.0 + discFraction,
            DiscernmentQualityChance:        discFraction);
    }

    private MasteryTierProgressDto? BuildTierProgress(
        MasteryAncient ancient, int level, IReadOnlyDictionary<MasteryActivityType, long> counters)
    {
        if (level >= PlayerMasteryMax) return null;
        var tier = _defs.GetTierChallenge(ancient, level);
        if (tier is null) return null;

        var items = tier.Checklist.Select(c =>
        {
            long current = counters.TryGetValue(c.ActivityType, out var cur) ? cur : 0;
            return new MasteryChecklistItemDto
            {
                ActivityType = c.ActivityType.ToString(),
                Current      = Math.Min(current, c.Threshold),
                Threshold    = c.Threshold,
            };
        }).ToList();

        return new MasteryTierProgressDto
        {
            FromLevel = level,
            ToLevel   = level + 1,
            Items     = items,
            Complete  = items.All(i => i.Current >= i.Threshold),
        };
    }

    private static MasteryTitlesDto BuildTitles(IReadOnlyDictionary<MasteryAncient, int> levels)
    {
        int L(MasteryAncient a) => LevelOf(levels, a);

        string? breadth = null;
        if (AllAncients.All(a => L(a) >= 5)) breadth = "Ascendant of the Ancients";
        else if (AllAncients.All(a => L(a) >= 4)) breadth = "Paragon of the Ancients";
        else if (AllAncients.All(a => L(a) >= 3)) breadth = "Well-Rounded";
        else if (AllAncients.All(a => L(a) >= 2)) breadth = "Touched Everything";

        var masteries = AllAncients.Where(a => L(a) >= 5).Select(a => $"Master of {a}").ToList();
        return new MasteryTitlesDto { Breadth = breadth, Masteries = masteries };
    }
}
