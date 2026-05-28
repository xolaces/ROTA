using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerRepository
{
    Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task<Player?> FindByEmailAsync(string email, CancellationToken ct = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

    Task<Player> CreateAsync(Player player, CancellationToken ct = default);

    Task<Player?> FindByIdWithResourcesAsync(Guid id, CancellationToken ct = default);

    Task<Player?> FindByIdWithStatsAsync(Guid id, CancellationToken ct = default);

    Task UpdateAsync(Player player, CancellationToken ct = default);

    Task UpdateStatsAsync(Domain.Entities.PlayerStats stats, CancellationToken ct = default);
}
