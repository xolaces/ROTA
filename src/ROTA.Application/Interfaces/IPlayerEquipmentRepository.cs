using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IPlayerEquipmentRepository
{
    // Returns the row for this slot regardless of IsDeleted — used for upsert on equip.
    Task<PlayerEquipment?> FindBySlotAsync(Guid playerId, EquipmentSlot slot, CancellationToken ct = default);
    // Returns only IsDeleted=false rows — used for stat computation and profile display.
    Task<IReadOnlyList<PlayerEquipment>> GetEquippedAsync(Guid playerId, CancellationToken ct = default);
    Task CreateAsync(PlayerEquipment equipment, CancellationToken ct = default);
    Task UpdateAsync(PlayerEquipment equipment, CancellationToken ct = default);
}
