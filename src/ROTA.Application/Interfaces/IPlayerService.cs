using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public enum PlayerUpdateStatus { Success, NotFound, UsernameTaken }

public record UpdateUsernameResult(PlayerUpdateStatus Status, string? NewUsername = null, DateTimeOffset UpdatedAt = default);

public interface IPlayerService
{
    Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken ct = default);

    Task<UpdateUsernameResult> UpdateUsernameAsync(Guid playerId, UpdateUsernameRequest request, CancellationToken ct = default);
}
