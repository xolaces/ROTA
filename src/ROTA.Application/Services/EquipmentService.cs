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
    private readonly IPlayerGearRepository      _gearRepo;
    private readonly IAchievementService        _achievements;

    public EquipmentService(
        IPlayerEquipmentRepository repo,
        IGearDefinitionProvider    gearDefs,
        IAuditLogRepository        auditLog,
        IPlayerInventoryRepository inventory,
        IItemDefinitionProvider    itemDefs,
        IPlayerGearRepository      gearRepo,
        IAchievementService        achievements)
    {
        _repo         = repo;
        _gearDefs     = gearDefs;
        _auditLog     = auditLog;
        _inventory    = inventory;
        _itemDefs     = itemDefs;
        _gearRepo     = gearRepo;
        _achievements = achievements;
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

        // Ownership gate — require a spare: owned_qty − currently_equipped_count ≥ 1.
        var owned = await _gearRepo.GetAsync(playerId, gearDefinitionId, ct);
        if (owned is null || owned.IsDeleted || owned.Quantity < 1)
            return new EquipResult { FailureReason = $"You do not own '{def.Name}'." };

        var equippedRows  = await _repo.GetEquippedAsync(playerId, ct);
        var equippedCount = equippedRows.Count(e =>
            string.Equals(e.GearDefinitionId, gearDefinitionId, StringComparison.OrdinalIgnoreCase));

        if (owned.Quantity - equippedCount < 1)
            return new EquipResult { FailureReason = $"No available copy of '{def.Name}' to equip (all copies already equipped)." };

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

    public async Task<IReadOnlyList<OwnedGearResponse>> GetOwnedGearAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var owned    = await _gearRepo.GetOwnedAsync(playerId, ct);
        var equipped = await _repo.GetEquippedAsync(playerId, ct);

        // How many copies of each def are currently equipped (across all slots).
        var equippedCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in equipped)
        {
            equippedCount.TryGetValue(e.GearDefinitionId, out var c);
            equippedCount[e.GearDefinitionId] = c + 1;
        }

        var result = new List<OwnedGearResponse>(owned.Count);
        foreach (var row in owned)
        {
            var def = _gearDefs.GetById(row.GearDefinitionId);
            if (def is null) continue;  // owned a def that no longer exists in content

            equippedCount.TryGetValue(row.GearDefinitionId, out var eq);
            result.Add(new OwnedGearResponse
            {
                GearDefinitionId  = def.Id,
                Name              = def.Name,
                Description       = def.Description,
                Rarity            = def.Rarity.ToString(),
                Slot              = def.Slot,
                BonusAttack       = def.BonusAttack,
                BonusDefense      = def.BonusDefense,
                ProcChance        = def.ProcChance,
                ProcPercent       = def.ProcPercent,
                IconPath          = def.IconPath,
                OwnedQuantity     = row.Quantity,
                EquippedQuantity  = eq,
                AvailableQuantity = Math.Max(0, row.Quantity - eq),
            });
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

            // ProcChanceFlat/ProcAmountFlat only apply when a mount is equipped.
            // Without a mount there is no GearProcData baseline to fold them into.
            // Magic DamageProcs (System 14) are the standalone proc layer for raids.
            if (mountProc is not null && (evaluated.ProcChanceFlat != 0 || evaluated.ProcAmountFlat != 0))
            {
                var adjustedChance = Math.Min(1.0, mountProc.ProcChance + evaluated.ProcChanceFlat);
                var adjustedAmount = mountProc.ProcPercent + evaluated.ProcAmountFlat;
                mountProc = new GearProcData(adjustedChance, adjustedAmount);
            }
        }

        return new EffectiveCombatData(baseAtk + bonusAtk, baseDef + bonusDef, mountProc, flatDmgPct);
    }

    public async Task GrantGearAsync(
        Guid playerId, string gearDefinitionId, int quantity, CancellationToken ct = default)
    {
        var existing = await _gearRepo.GetAsync(playerId, gearDefinitionId, ct);
        if (existing is not null)
        {
            existing.AddQuantity(quantity);
            await _gearRepo.UpdateAsync(existing, ct);
        }
        else
        {
            await _gearRepo.CreateAsync(PlayerGear.Create(playerId, gearDefinitionId, quantity), ct);
        }

        // TICKET 46 — EquipmentPiecesOwned achievement. RECOUNT the absolute owned total (SUM of
        // quantities across all owned gear) rather than applying a delta, so the counter can never
        // drift. Best-effort: a recount failure must never break the gear grant.
        try
        {
            var owned = await _gearRepo.GetOwnedAsync(playerId, ct);
            long total = owned.Sum(g => (long)g.Quantity);
            await _achievements.SetCounterAsync(
                playerId, Domain.Enums.AchievementMetric.EquipmentPiecesOwned, total, ct);
            // Evaluate promptly so an equipment-owned achievement is awarded at grant time (mirrors
            // QuestService), not only on the next profile/quest/raid read.
            await _achievements.EvaluateCompletionsAsync(playerId, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* best-effort — never break the grant */ }
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
