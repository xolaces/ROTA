using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IActiveRaidRepository
{
    Task<ActiveRaid?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveRaid>> GetAllActiveAsync(CancellationToken ct = default);
    Task<ActiveRaid> CreateAsync(ActiveRaid raid, CancellationToken ct = default);
    Task UpdateAsync(ActiveRaid raid, CancellationToken ct = default);
}
