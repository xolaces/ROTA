using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IRaidService
{
    Task<IReadOnlyList<ActiveRaidResponse>> GetActiveRaidsAsync(Guid playerId, CancellationToken ct = default);

    Task<SummonRaidResult> SummonRaidAsync(
        Guid playerId, string raidDefinitionId, RaidDifficulty difficulty,
        RaidSize size = RaidSize.Large, CancellationToken ct = default);

    Task<RaidHitResult> HitRaidAsync(
        Guid playerId, Guid activeRaidId, int hitSize, string idempotencyKey, CancellationToken ct = default);

    // System 21 Slice 3b — the caller's guild's active raids (empty when guild-less; guild resolved
    // server-side). Hits go through the existing HitRaidAsync (gated on guild_id).
    Task<IReadOnlyList<ActiveRaidResponse>> GetGuildRaidsAsync(Guid playerId, CancellationToken ct = default);

    // System 21 Slice 3b — officer-gated guild-raid summon: consumes 1 pooled sigil, creates a Large
    // raid stamped with the caller's guild_id.
    Task<SummonGuildRaidResult> SummonGuildRaidAsync(
        Guid playerId, string raidDefinitionId, RaidDifficulty difficulty, CancellationToken ct = default);

    // Join-by-UID lookup. Returns the raid mapped to ActiveRaidResponse regardless of IsPublic
    // (the GUID is the invite token). Returns null when not found / deleted / defeated / expired,
    // or when it's a Personal raid the caller did not summon (avoids leaking others' solo raids).
    Task<ActiveRaidResponse?> GetRaidByIdAsync(Guid activeRaidId, Guid callerId, CancellationToken ct = default);

    // Summoner-only publish to a visibility tier (Ticket 50). Sets Visibility, writes audit_log, returns
    // the updated raid. Fails NotFound (missing/expired), NotSummoner, CannotSharePersonal, or NotInGuild
    // (GuildOnly target while guild-less). The currently-shipped client passes RaidVisibility.Public.
    Task<ShareRaidResult> ShareRaidAsync(
        Guid callerId, Guid activeRaidId, RaidVisibility visibility = RaidVisibility.Public, CancellationToken ct = default);

    // Per-participant reward CLAIM on a defeated raid (T57 — this superseded Ticket 50's summoner-only
    // dismiss, and the old description survived here long enough to mislead the function reference).
    // ANY participant may call it; the claim is latched by a conditional UPDATE inside a per-participant
    // advisory-lock transaction, so it is race- and crash-safe and a re-press grants nothing further.
    // Grants the DEFERRED rewards only — gems, stat points, inventory items and collection drops; gold
    // and XP were already granted on the hit. A claimed participation is deleted with the grants, and
    // the last claimant deletes the raid: nothing about a looted raid is kept.
    // Fails NotFound (missing / already-looted / caller not a participant) or NotLootable (still Active).
    // NOTE: LootRaidFailureCode.NotSummoner is now dead — still mapped to 403, never returned.
    Task<LootRaidResult> LootRaidAsync(Guid callerId, Guid activeRaidId, CancellationToken ct = default);

    // World-raid expiry settlement. Finds timer-only raids (MaxHp == 0) whose clock has run out and which
    // are still Active, pays each one's banked damage ladder using the same compute-and-stash the kill path
    // uses, and flips the raid to Lootable so participants claim through the ordinary Loot flow.
    // Driven by RaidExpirySettlementService. Idempotent and safe to run concurrently: the Active → Lootable
    // transition is latched under the raid's advisory lock. Returns how many raids this call settled.
    Task<int> SettleExpiredRaidsAsync(int maxRaids = 50, CancellationToken ct = default);

    // Removes raids with nothing left to do: Lootable past the claim window (RaidConfig.LootClaimDays —
    // unclaimed loot is forfeited), ordinary raids that ran out of time with health left (failed), and
    // legacy Looted rows. Driven by RaidExpirySettlementService. Returns how many were removed.
    Task<int> PurgeSpentRaidsAsync(int maxRaids = 50, CancellationToken ct = default);

    Task<IReadOnlyList<RaidParticipantRankDto>> GetParticipantsAsync(Guid activeRaidId, int top, CancellationToken ct = default);
}
