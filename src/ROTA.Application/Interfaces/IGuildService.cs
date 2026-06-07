using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Guild identity, membership, join flow, roles, and lifecycle (System 21 Slice 1). Server-authoritative:
/// every state change re-verifies the actor and writes audit_log. No guild chat (S2) or guild raids (S3).
/// </summary>
public interface IGuildService
{
    // ── Lifecycle ──────────────────────────────────────────────────────────
    Task<CreateGuildResult> CreateGuildAsync(
        Guid playerId, string name, string tag, string description, GuildJoinPolicy joinPolicy,
        CancellationToken ct = default);

    Task<GuildActionResult> DisbandGuildAsync(Guid playerId, Guid guildId, CancellationToken ct = default);

    Task<GuildActionResult> UpdateGuildAsync(
        Guid actorId, Guid guildId, string? name, string? tag, string? description, string? motd,
        GuildJoinPolicy? joinPolicy, CancellationToken ct = default);

    // ── Join flow ──────────────────────────────────────────────────────────
    Task<ApplyGuildResult> ApplyAsync(Guid playerId, Guid guildId, CancellationToken ct = default);
    Task<GuildActionResult> AcceptApplicationAsync(Guid actorId, Guid guildId, Guid requestId, CancellationToken ct = default);
    Task<GuildActionResult> RejectApplicationAsync(Guid actorId, Guid guildId, Guid requestId, CancellationToken ct = default);
    Task<GuildActionResult> InviteAsync(Guid actorId, Guid guildId, string targetUsernameOrId, CancellationToken ct = default);
    Task<GuildActionResult> AcceptInviteAsync(Guid playerId, Guid requestId, CancellationToken ct = default);

    // ── Membership ─────────────────────────────────────────────────────────
    Task<GuildActionResult> LeaveAsync(Guid playerId, Guid guildId, CancellationToken ct = default);
    Task<GuildActionResult> KickAsync(Guid actorId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default);
    Task<GuildActionResult> PromoteAsync(Guid actorId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default);
    Task<GuildActionResult> DemoteAsync(Guid actorId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default);
    Task<GuildActionResult> TransferLeadershipAsync(Guid leaderId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default);

    // ── Succession ─────────────────────────────────────────────────────────
    /// <summary>
    /// Promotes the most-active officer to Leader if the current leader has been inactive beyond
    /// the configured window. Triggerable now; a scheduled auto-driver is a documented follow-up.
    /// </summary>
    Task<GuildActionResult> RunInactivitySuccessionAsync(Guid guildId, CancellationToken ct = default);

    // ── Reads ──────────────────────────────────────────────────────────────
    Task<GuildDetailResponse?> GetGuildAsync(Guid guildId, Guid callerId, CancellationToken ct = default);
    Task<IReadOnlyList<GuildSummaryDto>> BrowseGuildsAsync(string? query, int page, CancellationToken ct = default);
}
