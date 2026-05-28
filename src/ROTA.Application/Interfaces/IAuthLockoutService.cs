namespace ROTA.Application.Interfaces;

public interface IAuthLockoutService
{
    Task<bool> IsLockedOutAsync(string email, CancellationToken ct = default);

    Task RecordFailedAttemptAsync(string email, CancellationToken ct = default);

    Task ClearAsync(string email, CancellationToken ct = default);
}
