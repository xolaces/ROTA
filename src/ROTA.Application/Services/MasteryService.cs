using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
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
    private readonly MasteryConfig _config;

    public MasteryService(
        IMasteryDefinitionProvider defs,
        IPlayerMasteryRepository masteryRepo,
        IPlayerMasteryActivityRepository activityRepo,
        IPlayerRepository players,
        ILeaderboardEntryRepository leaderboard,
        IOptions<MasteryConfig> config)
    {
        _defs         = defs;
        _masteryRepo  = masteryRepo;
        _activityRepo = activityRepo;
        _players      = players;
        _leaderboard  = leaderboard;
        _config       = config.Value;
    }

    // ── GetMasteriesAsync ─────────────────────────────────────────────────────

    public async Task<MasteryOverviewResponse> GetMasteriesAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        var pledge = player?.ActivePledgeAncient;

        var masteries = await _masteryRepo.EnsureAllAsync(playerId, ct);
        var levels = masteries.ToDictionary(m => m.Ancient, m => m.Level);

        var activities = await _activityRepo.GetForPlayerAsync(playerId, ct);
        var counters = activities.ToDictionary(a => a.ActivityType, a => a.Counter);

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
            // Re-spec availability is refined in Slice 3 (needs the respec ledger); GemCost is real now.
            RespecStatus = new MasteryRespecStatusDto
            {
                FreeMonthlyAvailable = true,
                PaidWeeklyAvailable  = true,
                GemCost              = _config.RespecGemCost,
            },
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
