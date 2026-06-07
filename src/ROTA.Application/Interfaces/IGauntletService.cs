using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — player-facing Gauntlet operations: read the current event, join
// (league locked, idempotent), and buy Strikes with gems (uncapped, idempotent).
public interface IGauntletService
{
    /// <summary>The Active event, or null if none is open.</summary>
    Task<GauntletEventResponse?> GetCurrentEventAsync(CancellationToken ct = default);

    /// <summary>
    /// Joins the player to the Active event, locking their league by convergence tier
    /// (ResolveLeague(player.Level)). Rejects when there is no active event, level &lt; MinEntryLevel,
    /// or the player is banned/soft-deleted. Idempotent: an existing entry is returned unchanged
    /// (league never re-evaluated).
    /// </summary>
    Task<JoinGauntletResult> JoinEventAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>The caller's entry in the given event, or null.</summary>
    Task<GauntletEntryResponse?> GetMyEntryAsync(
        Guid playerId, Guid gauntletEventId, CancellationToken ct = default);

    /// <summary>
    /// The player's current Gauntlet ladder target for the active event (System 16 Slice 7). The
    /// ladder auto-advances via lazy spawn: if an active (not defeated, not expired) stage exists it
    /// is returned; otherwise the next stage above the highest the player has defeated is spawned
    /// (Personal, GauntletEventId-stamped, MaxHp = that stage's baseHp, no difficulty multiplier) and
    /// returned. Past the final stage → Complete. Requires a joined GauntletEntry; no active event →
    /// NoActiveEvent. No new entity / no migration — ladder progress is derived from the player's
    /// gauntlet ActiveRaids (stage number parsed from RaidDefinitionId "gauntlet_stage_N").
    /// </summary>
    Task<GauntletLadderResponse> GetLadderAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Buys <paramref name="strikes"/> Strikes for the player using gems
    /// (cost = strikes × StrikeGemPrice). Idempotent on <paramref name="idempotencyKey"/>:
    /// a retry re-credits the same strikes without re-charging gems or double-crediting.
    /// </summary>
    Task<BuyStrikesResult> BuyStrikesAsync(
        Guid playerId, int strikes, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// The token-shop catalogue (each entry hydrated with a caller-specific AlreadyOwned flag for
    /// own-once kinds) plus the caller's live Token and Pitchfork balances.
    /// </summary>
    Task<GauntletShopResponse> GetShopAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Buys a token-shop entry, mirroring the BuyUnit/BuyMagic discipline. Own-once kinds are
    /// ownership-pre-checked (AlreadyOwned without charging); the spend is idempotent on
    /// referenceId <c>gauntletshop:{playerId}:{shopEntryId}</c> from the entry's currency, and the
    /// grant is idempotent so an AlreadyCharged replay re-grants without re-charging. An unknown
    /// entry id fails NotFound-style. Token vs Pitchfork are never conflated — a Pitchfork-priced
    /// entry attempted with only a Token balance returns InsufficientTokens.
    /// </summary>
    Task<BuyShopResult> BuyFromShopAsync(Guid playerId, string shopEntryId, CancellationToken ct = default);
}
