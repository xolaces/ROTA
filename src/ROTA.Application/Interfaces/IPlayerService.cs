using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public enum PlayerUpdateStatus { Success, NotFound, UsernameTaken }

public record UpdateUsernameResult(PlayerUpdateStatus Status, string? NewUsername = null, DateTimeOffset UpdatedAt = default);

public interface IPlayerService
{
    /// <summary>
    /// Returns the player's full profile with live resource values.
    /// Returns null if the player is not found or has been soft-deleted.
    /// </summary>
    Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Updates the player's username after re-validating uniqueness.
    /// PlayerId always comes from the verified JWT — never from the request.
    /// </summary>
    Task<UpdateUsernameResult> UpdateUsernameAsync(Guid playerId, UpdateUsernameRequest request, CancellationToken ct = default);
}
