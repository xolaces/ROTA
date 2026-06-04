using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public enum PlayerUpdateStatus { Success, NotFound, UsernameTaken }

public record UpdateUsernameResult(PlayerUpdateStatus Status, string? NewUsername = null, DateTimeOffset UpdatedAt = default);

public enum DisplayNameUpdateStatus { Success, NotFound }

public record UpdateDisplayNameResult(DisplayNameUpdateStatus Status, string? NewDisplayName = null, DateTimeOffset UpdatedAt = default);

public interface IPlayerService
{
    Task<PlayerProfileResponse?> GetProfileAsync(Guid playerId, CancellationToken ct = default);

    Task<UpdateUsernameResult> UpdateUsernameAsync(Guid playerId, UpdateUsernameRequest request, CancellationToken ct = default);

    Task<UpdateDisplayNameResult> UpdateDisplayNameAsync(Guid playerId, UpdateDisplayNameRequest request, CancellationToken ct = default);
}
