using Microsoft.Extensions.Options;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA
public sealed class MagicService : IMagicService
{
    private readonly IPlayerMagicRepository   _magicRepo;
    private readonly IRaidMagicRepository     _raidMagics;
    private readonly IActiveRaidRepository    _raids;
    private readonly IRaidDefinitionProvider  _raidDefs;
    private readonly IRaidParticipantRepository _participants;
    private readonly IMagicDefinitionProvider _defs;
    private readonly IAuditLogRepository      _auditLog;
    private readonly MagicConfig              _config;

    public MagicService(
        IPlayerMagicRepository    magicRepo,
        IRaidMagicRepository      raidMagics,
        IActiveRaidRepository     raids,
        IRaidDefinitionProvider   raidDefs,
        IRaidParticipantRepository participants,
        IMagicDefinitionProvider  defs,
        IAuditLogRepository       auditLog,
        IOptions<MagicConfig>     config)
    {
        _magicRepo    = magicRepo;
        _raidMagics   = raidMagics;
        _raids        = raids;
        _raidDefs     = raidDefs;
        _participants = participants;
        _defs         = defs;
        _auditLog     = auditLog;
        _config       = config.Value;
    }

    public async Task<IReadOnlyList<OwnedMagicResponse>> GetOwnedMagicsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var rows = await _magicRepo.GetOwnedAsync(playerId, ct);
        var result = new List<OwnedMagicResponse>(rows.Count);
        foreach (var row in rows)
        {
            var def = _defs.GetById(row.MagicDefinitionId);
            if (def is null) continue;
            result.Add(new OwnedMagicResponse
            {
                MagicDefinitionId = def.Id,
                Name              = def.Name,
                Description       = def.Description,
                Rarity            = def.Rarity.ToString(),
                Category          = def.Category.ToString(),
                EffectType        = def.EffectType.ToString(),
                ProcChance        = def.ProcChance,
                ProcAmount        = def.ProcAmount,
                Stacks            = def.Stacks,
                IconPath          = def.IconPath,
                Acquisition       = def.Acquisition,
                AcquiredAt        = row.CreatedAt,
            });
        }
        return result;
    }

    public async Task<MagicApplyResult> ApplyMagicAsync(
        Guid playerId, Guid raidId, string magicDefinitionId, bool isAdmin,
        CancellationToken ct = default)
    {
        // 1. Raid exists and is active.
        var raid = await _raids.FindByIdAsync(raidId, ct);
        if (raid is null)
            return Fail(MagicApplyFailureCode.RaidNotFound, "Raid not found.");
        if (raid.IsDefeated || raid.ExpiresAt < DateTimeOffset.UtcNow)
            return Fail(MagicApplyFailureCode.RaidNotActive, "Raid is no longer active.");

        // 2. World gate: World raids are Admin-only for magic.
        var raidDef    = _raidDefs.GetById(raid.RaidDefinitionId);
        bool isWorld   = raidDef is not null
                      && _config.DevOnlyTiers.Contains(raidDef.Tier, StringComparer.OrdinalIgnoreCase);
        if (isWorld && !isAdmin)
            return Fail(MagicApplyFailureCode.WorldGateBlocked,
                "Only Admins may apply magic to World raids.");

        // 3. Player owns the magic.
        var owned = await _magicRepo.FindAsync(playerId, magicDefinitionId, ct);
        if (owned is null || owned.IsDeleted)
            return Fail(MagicApplyFailureCode.MagicNotOwned, "You do not own this magic.");

        // 4. Player is summoner or participant (non-world only; Admins on world are exempt).
        if (!isWorld)
        {
            bool isSummoner     = raid.SummonedByPlayerId == playerId;
            bool isParticipant  = isSummoner
                               || await _participants.FindByRaidAndPlayerAsync(raidId, playerId, ct) is not null;
            if (!isParticipant)
                return Fail(MagicApplyFailureCode.NotAParticipant,
                    "You must be the summoner or a participant to apply magic.");
        }

        // 5–7. Advisory-lock: one-per-player + duplicate check + slot cap must all be atomic.
        //      The one-per-player check is inside the lock (not before it) so two concurrent
        //      applies by the same player with two different magics cannot both pass the check
        //      and both insert. Ordering: player-check → dup-check → slot-cap → insert.
        bool alreadyByPlayer = false;
        bool duplicateMagic  = false;
        bool slotsFull       = false;

        var applied = await _raids.AtomicWithAdvisoryLockAsync(raidId, async () =>
        {
            // One-per-player (non-world): inside lock to close the same-player concurrency race.
            // FindByPlayerAsync filters IsDeleted=false, so a previously removed magic lets the
            // player reapply.
            if (!isWorld)
            {
                var byPlayer = await _raidMagics.FindByPlayerAsync(raidId, playerId, ct);
                if (byPlayer is not null)
                {
                    alreadyByPlayer = true;
                    return false;
                }
            }

            // Duplicate magic check (inside lock so no TOCTOU).
            var dup = await _raidMagics.FindAsync(raidId, magicDefinitionId, ct);
            if (dup is not null)
            {
                duplicateMagic = true;
                return false;
            }

            // Slot cap check.
            int slotCount = _config.GetSlotCount(raid.Size.ToString());
            int current   = await _raidMagics.CountForRaidAsync(raidId, ct);
            if (current >= slotCount)
            {
                slotsFull = true;
                return false;
            }

            // Insert within the transaction (shares DbContext with the lock tx).
            await _raidMagics.CreateAsync(RaidMagic.Create(raidId, magicDefinitionId, playerId), ct);

            return true;
        }, ct);

        if (!applied)
        {
            if (alreadyByPlayer)
                return Fail(MagicApplyFailureCode.AlreadyAppliedByPlayer,
                    "You already have a magic applied to this raid.");
            if (duplicateMagic)
                return Fail(MagicApplyFailureCode.DuplicateMagic,
                    "This magic is already applied to the raid.");
            if (slotsFull)
                return Fail(MagicApplyFailureCode.SlotsFull,
                    "All magic slots for this raid are occupied.");
            return Fail(MagicApplyFailureCode.RaidNotFound, "Could not acquire lock.");
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "MagicApply", null,
            $"Applied magic '{magicDefinitionId}' to raid {raidId}.", null), ct);

        return new MagicApplyResult { Success = true };
    }

    public async Task<MagicApplyResult> RemoveMagicAsync(
        Guid playerId, Guid raidId, string magicDefinitionId, bool isAdmin,
        CancellationToken ct = default)
    {
        // 1. Raid exists.
        var raid = await _raids.FindByIdAsync(raidId, ct);
        if (raid is null)
            return Fail(MagicApplyFailureCode.RaidNotFound, "Raid not found.");

        // 2. Find magic on raid.
        var raidMagic = await _raidMagics.FindAsync(raidId, magicDefinitionId, ct);
        if (raidMagic is null)
            return Fail(MagicApplyFailureCode.MagicNotFound, "Magic is not applied to this raid.");

        // 3. World gate: only Admin may remove.
        var raidDef  = _raidDefs.GetById(raid.RaidDefinitionId);
        bool isWorld = raidDef is not null
                    && _config.DevOnlyTiers.Contains(raidDef.Tier, StringComparer.OrdinalIgnoreCase);
        if (isWorld && !isAdmin)
            return Fail(MagicApplyFailureCode.WorldGateBlocked,
                "Only Admins may remove magic from World raids.");

        // 4. Non-world: only the summoner may remove.
        if (!isWorld && raid.SummonedByPlayerId != playerId)
            return Fail(MagicApplyFailureCode.RemoveNotAllowed,
                "Only the raid summoner may remove magic.");

        await _raidMagics.SoftDeleteAsync(raidMagic, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "MagicRemove", null,
            $"Removed magic '{magicDefinitionId}' from raid {raidId}.", null), ct);

        return new MagicApplyResult { Success = true };
    }

    private static MagicApplyResult Fail(MagicApplyFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
