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
    /// Buys <paramref name="strikes"/> Strikes for the player using gems
    /// (cost = strikes × StrikeGemPrice). Idempotent on <paramref name="idempotencyKey"/>:
    /// a retry re-credits the same strikes without re-charging gems or double-crediting.
    /// </summary>
    Task<BuyStrikesResult> BuyStrikesAsync(
        Guid playerId, int strikes, string idempotencyKey, CancellationToken ct = default);
}
