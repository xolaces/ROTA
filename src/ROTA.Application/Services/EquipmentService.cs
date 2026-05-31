using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA
public sealed class EquipmentService : IEquipmentService
{
    private readonly IPlayerEquipmentRepository _repo;
    private readonly IGearDefinitionProvider    _gearDefs;
    private readonly IAuditLogRepository        _auditLog;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IItemDefinitionProvider    _itemDefs;

    public EquipmentService(
        IPlayerEquipmentRepository repo,
        IGearDefinitionProvider    gearDefs,
        IAuditLogRepository        auditLog,
        IPlayerInventoryRepository inventory,
        IItemDefinitionProvider    itemDefs)
    {
        _repo      = repo;
        _gearDefs  = gearDefs;
        _auditLog  = auditLog;
        _inventory = inventory;
        _itemDefs  = itemDefs;
    }

    public async Task<EquipResult> EquipAsync(
        Guid playerId, string slotName, string gearDefinitionId, CancellationToken ct = default)
    {
        if (!Enum.TryParse<EquipmentSlot>(slotName, ignoreCase: true, out var slot))
            return new EquipResult { FailureReason = $"Unknown slot '{slotName}'." };

        var def = _gearDefs.GetById(gearDefinitionId);
        if (def is null)
            return new EquipResult { FailureReason = $"Gear definition '{gearDefinitionId}' not found." };

        if (!string.Equals(def.Slot, slotName, StringComparison.OrdinalIgnoreCase))
            return new EquipResult
            {
                FailureReason = $"Item '{def.Name}' belongs to slot '{def.Slot}', not '{slotName}'.",
            };

        // Upsert — one row per (player, slot) due to DB unique constraint.
        var existing = await _repo.FindBySlotAsync(playerId, slot, ct);
        if (existing is not null)
        {
            existing.Equip(gearDefinitionId);
            await _repo.UpdateAsync(existing, ct);
        }
        else
        {
            var newRow = PlayerEquipment.Create(playerId, slot, gearDefinitionId);
            await _repo.CreateAsync(newRow, ct);
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "GearEquip", null,
            $"Equipped '{def.Name}' ({gearDefinitionId}) in slot {slot}.", null), ct);

        return new EquipResult
        {
            Success = true,
            Item    = MapResponse(slot, def, DateTimeOffset.UtcNow),
        };
    }

    public async Task<UnequipResult> UnequipAsync(
        Guid playerId, string slotName, CancellationToken ct = default)
    {
        if (!Enum.TryParse<EquipmentSlot>(slotName, ignoreCase: true, out var slot))
            return new UnequipResult { FailureReason = $"Unknown slot '{slotName}'." };

        var existing = await _repo.FindBySlotAsync(playerId, slot, ct);
        if (existing is null || existing.IsDeleted)
            return new UnequipResult { FailureReason = $"No item equipped in slot '{slotName}'." };

        existing.Unequip();
        await _repo.UpdateAsync(existing, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "GearUnequip", null,
            $"Unequipped slot {slot} (was '{existing.GearDefinitionId}').", null), ct);

        return new UnequipResult { Success = true };
    }

    public async Task<IReadOnlyList<EquippedItemResponse>> GetEquipmentAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var rows = await _repo.GetEquippedAsync(playerId, ct);
        var result = new List<EquippedItemResponse>(rows.Count);
        foreach (var row in rows)
        {
            var def = _gearDefs.GetById(row.GearDefinitionId);
            if (def is null) continue;
            result.Add(MapResponse(row.Slot, def, row.EquippedAt));
        }
        return result;
    }

    public async Task<EffectiveCombatData> GetEffectiveCombatDataAsync(
        Guid playerId, int baseAtk, int baseDef, CancellationToken ct = default)
    {
        var rows = await _repo.GetEquippedAsync(playerId, ct);

        // Base gear stats
        int bonusAtk = 0;
        int bonusDef = 0;
        GearProcData? mountProc = null;

        // Collect all conditional bonuses declared across equipped gear
        var allConditionalBonuses = new List<ConditionalBonus>();
        var equippedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var def = _gearDefs.GetById(row.GearDefinitionId);
            if (def is null) continue;

            bonusAtk += def.BonusAttack;
            bonusDef += def.BonusDefense;

            if (row.Slot == EquipmentSlot.Mount
                && def.ProcChance is not null
                && def.ProcPercent is not null)
            {
                mountProc = new GearProcData(def.ProcChance.Value, def.ProcPercent.Value);
            }

            allConditionalBonuses.AddRange(def.ConditionalBonuses);
            equippedSlots.Add(row.Slot.ToString());
        }

        // Evaluate conditional bonuses if any gear declares them
        var flatDmgPct = 0.0;
        if (allConditionalBonuses.Count > 0)
        {
            var inventoryItems = await _inventory.GetAllForPlayerAsync(playerId, ct);

            var ownedById = inventoryItems.ToDictionary(
                i => i.ItemDefinitionId, i => i.Quantity);

            // Accumulate owned quantities by tag
            var ownedByTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var inv in inventoryItems)
            {
                var itemDef = _itemDefs.GetById(inv.ItemDefinitionId);
                if (itemDef is null) continue;
                foreach (var tag in itemDef.Tags)
                {
                    ownedByTag.TryGetValue(tag, out int existing);
                    ownedByTag[tag] = existing + inv.Quantity;
                }
            }

            var evaluated = ConditionalBonusEvaluator.Evaluate(
                allConditionalBonuses, ownedById, ownedByTag,
                new HashSet<string>(equippedSlots.Select(s => s.ToLowerInvariant())));

            bonusAtk  += evaluated.FlatAttack;
            bonusDef  += evaluated.FlatDefense;
            flatDmgPct = evaluated.FlatDamagePercent;

            // Fold conditional proc adjustments into mount proc data
            if (mountProc is not null && (evaluated.ProcChanceFlat != 0 || evaluated.ProcAmountFlat != 0))
            {
                var adjustedChance = Math.Min(1.0, mountProc.ProcChance + evaluated.ProcChanceFlat);
                var adjustedAmount = mountProc.ProcPercent + evaluated.ProcAmountFlat;
                mountProc = new GearProcData(adjustedChance, adjustedAmount);
            }
        }

        return new EffectiveCombatData(baseAtk + bonusAtk, baseDef + bonusDef, mountProc, flatDmgPct);
    }

    private static EquippedItemResponse MapResponse(
        EquipmentSlot slot, GearDefinition def, DateTimeOffset equippedAt)
        => new()
        {
            Slot             = slot.ToString(),
            GearDefinitionId = def.Id,
            Name             = def.Name,
            Description      = def.Description,
            Rarity           = def.Rarity.ToString(),
            BonusAttack      = def.BonusAttack,
            BonusDefense     = def.BonusDefense,
            ProcChance       = def.ProcChance,
            ProcPercent      = def.ProcPercent,
            IconPath         = def.IconPath,
            EquippedAt       = equippedAt,
        };
}
