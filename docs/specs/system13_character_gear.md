# System 13 — Character Gear (v0.2.4)

*Spec locked 2026-05-30. Build against this exactly.*

---

## What to build

8-slot equipment system. JSON-driven gear definitions (no code change for new gear). Effective ATK/DEF (base + gear) replaces `BaseAttack`/`BaseDefense` everywhere a stat matters. Mount slot = a once-per-hit proc bonus in the raid-hit pipeline. Equip/unequip endpoints. `player_equipment` DB table.

---

## New files to create

| Path | Purpose |
|------|---------|
| `src/ROTA.Domain/Enums/EquipmentSlot.cs` | Slot enum |
| `src/ROTA.Domain/Entities/PlayerEquipment.cs` | Domain entity |
| `src/ROTA.Application/Models/GearDefinition.cs` | Content model |
| `src/ROTA.Application/Interfaces/IGearDefinitionProvider.cs` | Content provider interface |
| `src/ROTA.Application/Interfaces/IPlayerEquipmentRepository.cs` | Repo interface |
| `src/ROTA.Application/Interfaces/IEquipmentService.cs` | Service interface + result records |
| `src/ROTA.Application/Services/EquipmentService.cs` | Service implementation |
| `src/ROTA.Infrastructure/Persistence/Configurations/PlayerEquipmentConfiguration.cs` | EF config |
| `src/ROTA.Infrastructure/Persistence/Repositories/PlayerEquipmentRepository.cs` | Repo impl |
| `src/ROTA.Infrastructure/Services/GearDefinitionProvider.cs` | Content provider impl |
| `src/ROTA.Api/Controllers/EquipmentController.cs` | Endpoints |
| `src/ROTA.Api/content/gear.json` | Starter gear definitions |
| `src/ROTA.Shared/DTOs/EquipmentDTOs.cs` | DTOs |
| `tests/ROTA.UnitTests/Services/EquipmentServiceTests.cs` | 8 unit tests |

---

## Files to modify

| Path | Change |
|------|--------|
| `src/ROTA.Application/Services/RaidService.cs` | Effective stats + mount proc in `HitRaidAsync` |
| `src/ROTA.Application/Services/StatService.cs` | Effective stats in `GetStatsAsync` |
| `src/ROTA.Application/Services/PlayerService.cs` | Effective stats in `GetProfileAsync` |
| `src/ROTA.Shared/DTOs/StatDTOs.cs` | Add `EffectiveAttack`, `EffectiveDefense` to `PlayerStatsResponse` |
| `src/ROTA.Shared/DTOs/PlayerDTOs.cs` | Add `EffectiveAttack`, `EffectiveDefense` to `PlayerProfileResponse` |
| `src/ROTA.Shared/DTOs/RaidDTOs.cs` | Add `ProcFired`, `ProcBonus` to `RaidHitResponse` |
| `src/ROTA.Infrastructure/ServiceCollectionExtensions.cs` | Register new services |
| `src/ROTA.Infrastructure/Persistence/RotaDbContext.cs` | Add `DbSet<PlayerEquipment>` |
| `tests/ROTA.UnitTests/Services/RaidServiceTests.cs` | Add `IEquipmentService` mock; 2 new mount-proc tests |
| `tests/ROTA.UnitTests/Services/StatServiceTests.cs` | Add `IEquipmentService` mock to constructor |
| `tests/ROTA.UnitTests/Services/PlayerServiceTests.cs` | Add `IEquipmentService` mock to constructor |

Generate the migration with:
```
dotnet ef migrations add AddEquipmentSystem --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
```

Do NOT run `database update` — the owner applies migrations.

---

## Domain: EquipmentSlot enum

File: `src/ROTA.Domain/Enums/EquipmentSlot.cs`

```csharp
namespace ROTA.Domain.Enums;

public enum EquipmentSlot
{
    Head   = 0,
    Neck   = 1,
    Torso  = 2,
    Ring1  = 3,
    Ring2  = 4,
    Mount  = 5,
    Boots  = 6,
    Gloves = 7,
}
```

---

## Domain: PlayerEquipment entity

File: `src/ROTA.Domain/Entities/PlayerEquipment.cs`

Rules: private setters, no EF attributes, changes via methods only.

```csharp
namespace ROTA.Domain.Entities;

using ROTA.Domain.Enums;

public class PlayerEquipment
{
    private PlayerEquipment() { }

    public static PlayerEquipment Create(Guid playerId, EquipmentSlot slot, string gearDefinitionId)
        => new()
        {
            Id               = Guid.NewGuid(),
            PlayerId         = playerId,
            Slot             = slot,
            GearDefinitionId = gearDefinitionId,
            EquippedAt       = DateTimeOffset.UtcNow,
            CreatedAt        = DateTimeOffset.UtcNow,
            UpdatedAt        = DateTimeOffset.UtcNow,
            IsDeleted        = false,
        };

    public Guid            Id               { get; private set; }
    public Guid            PlayerId         { get; private set; }
    public EquipmentSlot   Slot             { get; private set; }
    public string          GearDefinitionId { get; private set; } = string.Empty;
    public DateTimeOffset  EquippedAt       { get; private set; }
    public DateTimeOffset  CreatedAt        { get; private set; }
    public DateTimeOffset  UpdatedAt        { get; private set; }
    public bool            IsDeleted        { get; private set; }

    // Swap or restore gear in this slot.
    public void Equip(string gearDefinitionId)
    {
        GearDefinitionId = gearDefinitionId;
        IsDeleted        = false;
        EquippedAt       = DateTimeOffset.UtcNow;
        UpdatedAt        = DateTimeOffset.UtcNow;
    }

    public void Unequip()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

---

## Application: GearDefinition model

File: `src/ROTA.Application/Models/GearDefinition.cs`

```csharp
using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class GearDefinition
{
    public string      Id          { get; set; } = string.Empty;
    public string      Name        { get; set; } = string.Empty;
    public string      Description { get; set; } = string.Empty;
    public ItemRarity  Rarity      { get; set; }
    public string      Slot        { get; set; } = string.Empty; // stored as string, parsed to EquipmentSlot
    public int         BonusAttack  { get; set; }
    public int         BonusDefense { get; set; }
    public double?     ProcChance   { get; set; } // null = no proc
    public double?     ProcPercent  { get; set; } // bonus added = baseDamage × ProcPercent
    public string      IconPath     { get; set; } = string.Empty;
}
```

---

## Application: IGearDefinitionProvider

File: `src/ROTA.Application/Interfaces/IGearDefinitionProvider.cs`

```csharp
using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface IGearDefinitionProvider
{
    GearDefinition? GetById(string id);
    IReadOnlyList<GearDefinition> GetAll();
    IReadOnlyList<GearDefinition> GetBySlot(string slot);
}
```

---

## Application: IPlayerEquipmentRepository

File: `src/ROTA.Application/Interfaces/IPlayerEquipmentRepository.cs`

```csharp
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
```

---

## Application: IEquipmentService (+ result records)

File: `src/ROTA.Application/Interfaces/IEquipmentService.cs`

```csharp
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IEquipmentService
{
    Task<EquipResult>   EquipAsync(Guid playerId, string slotName, string gearDefinitionId, CancellationToken ct = default);
    Task<UnequipResult> UnequipAsync(Guid playerId, string slotName, CancellationToken ct = default);
    Task<IReadOnlyList<EquippedItemResponse>> GetEquipmentAsync(Guid playerId, CancellationToken ct = default);

    // Called by RaidService on every hit. baseAtk/baseDef are from PlayerStats.
    Task<EffectiveCombatData> GetEffectiveCombatDataAsync(Guid playerId, int baseAtk, int baseDef, CancellationToken ct = default);
}

// Lives in this file alongside the interface.
public sealed record EffectiveCombatData(
    int           EffectiveAttack,
    int           EffectiveDefense,
    GearProcData? MountProc);      // null when no mount is equipped

public sealed record GearProcData(double ProcChance, double ProcPercent);
```

---

## Application: EquipmentService

File: `src/ROTA.Application/Services/EquipmentService.cs`

```csharp
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

    public EquipmentService(
        IPlayerEquipmentRepository repo,
        IGearDefinitionProvider    gearDefs,
        IAuditLogRepository        auditLog)
    {
        _repo     = repo;
        _gearDefs = gearDefs;
        _auditLog = auditLog;
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
        int bonusAtk = 0;
        int bonusDef = 0;
        GearProcData? mountProc = null;

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
        }

        return new EffectiveCombatData(baseAtk + bonusAtk, baseDef + bonusDef, mountProc);
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
```

---

## Infrastructure: EF Configuration

File: `src/ROTA.Infrastructure/Persistence/Configurations/PlayerEquipmentConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerEquipmentConfiguration : IEntityTypeConfiguration<PlayerEquipment>
{
    public void Configure(EntityTypeBuilder<PlayerEquipment> builder)
    {
        builder.ToTable("player_equipment");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(e => e.Slot)
            .HasColumnName("slot")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.GearDefinitionId)
            .HasColumnName("gear_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EquippedAt)
            .HasColumnName("equipped_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // One row per (player, slot) — equip upserts into this row; unequip soft-deletes it.
        builder.HasIndex(e => new { e.PlayerId, e.Slot })
            .IsUnique()
            .HasDatabaseName("ix_player_equipment_player_slot");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_player_equipment_player_id");
    }
}
```

---

## Infrastructure: PlayerEquipmentRepository

File: `src/ROTA.Infrastructure/Persistence/Repositories/PlayerEquipmentRepository.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerEquipmentRepository : IPlayerEquipmentRepository
{
    private readonly RotaDbContext _db;

    public PlayerEquipmentRepository(RotaDbContext db) => _db = db;

    public Task<PlayerEquipment?> FindBySlotAsync(
        Guid playerId, EquipmentSlot slot, CancellationToken ct = default)
        => _db.PlayerEquipment
              .FirstOrDefaultAsync(e => e.PlayerId == playerId && e.Slot == slot, ct);

    public async Task<IReadOnlyList<PlayerEquipment>> GetEquippedAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var rows = await _db.PlayerEquipment
            .Where(e => e.PlayerId == playerId && !e.IsDeleted)
            .ToListAsync(ct);
        return rows;
    }

    public async Task CreateAsync(PlayerEquipment equipment, CancellationToken ct = default)
    {
        _db.PlayerEquipment.Add(equipment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PlayerEquipment equipment, CancellationToken ct = default)
    {
        _db.PlayerEquipment.Update(equipment);
        await _db.SaveChangesAsync(ct);
    }
}
```

---

## Infrastructure: GearDefinitionProvider

File: `src/ROTA.Infrastructure/Services/GearDefinitionProvider.cs`

Follows the same pattern as `ItemDefinitionProvider`. Throws at startup if gear.json is missing or malformed.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

public sealed class GearDefinitionProvider : IGearDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, GearDefinition> _gear;

    public GearDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "gear.json");
        if (!File.Exists(path))
            throw new InvalidOperationException("gear.json not found — required for startup.");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var list = JsonSerializer.Deserialize<List<GearDefinition>>(json, options)
            ?? throw new InvalidOperationException("gear.json deserialized to null.");

        // Startup validation: no duplicate IDs
        var seen = new HashSet<string>();
        foreach (var g in list)
        {
            if (!seen.Add(g.Id))
                throw new InvalidOperationException($"gear.json has duplicate id '{g.Id}'.");
        }

        _gear = list.ToDictionary(g => g.Id, g => g);
    }

    public GearDefinition? GetById(string id)
        => _gear.TryGetValue(id, out var g) ? g : null;

    public IReadOnlyList<GearDefinition> GetAll()
        => _gear.Values.ToList();

    public IReadOnlyList<GearDefinition> GetBySlot(string slot)
        => _gear.Values.Where(g => string.Equals(g.Slot, slot, StringComparison.OrdinalIgnoreCase)).ToList();
}
```

---

## Infrastructure: RotaDbContext addition

Add to `src/ROTA.Infrastructure/Persistence/RotaDbContext.cs`:

```csharp
public DbSet<PlayerEquipment> PlayerEquipment => Set<PlayerEquipment>();
```

---

## Infrastructure: ServiceCollectionExtensions additions

Add to `AddRotaServices` in `src/ROTA.Infrastructure/ServiceCollectionExtensions.cs`:

```csharp
services.AddScoped<IPlayerEquipmentRepository, PlayerEquipmentRepository>();
services.AddScoped<IEquipmentService, EquipmentService>();
services.AddSingleton<IGearDefinitionProvider>(
    _ => new GearDefinitionProvider(contentRootPath));
```

Place repo registration with the other repo registrations, service with other services, singleton with other singletons.

---

## Shared DTOs

### EquipmentDTOs.cs

File: `src/ROTA.Shared/DTOs/EquipmentDTOs.cs`

```csharp
namespace ROTA.Shared.DTOs;

public class EquippedItemResponse
{
    public string          Slot             { get; set; } = string.Empty;
    public string          GearDefinitionId { get; set; } = string.Empty;
    public string          Name             { get; set; } = string.Empty;
    public string          Description      { get; set; } = string.Empty;
    public string          Rarity           { get; set; } = string.Empty;
    public int             BonusAttack      { get; set; }
    public int             BonusDefense     { get; set; }
    public double?         ProcChance       { get; set; }
    public double?         ProcPercent      { get; set; }
    public string          IconPath         { get; set; } = string.Empty;
    public DateTimeOffset  EquippedAt       { get; set; }
}

public class EquipRequest
{
    public string GearDefinitionId { get; set; } = string.Empty;
}

public class EquipResult
{
    public bool                  Success       { get; set; }
    public string?               FailureReason { get; set; }
    public EquippedItemResponse? Item          { get; set; }
}

public class UnequipResult
{
    public bool    Success       { get; set; }
    public string? FailureReason { get; set; }
}
```

### StatDTOs.cs additions

Add to `PlayerStatsResponse`:
```csharp
public int EffectiveAttack  { get; set; }
public int EffectiveDefense { get; set; }
```

### PlayerDTOs.cs additions

Add to `PlayerProfileResponse`:
```csharp
public int EffectiveAttack  { get; set; }
public int EffectiveDefense { get; set; }
```

### RaidDTOs.cs additions

Add to `RaidHitResponse`:
```csharp
public bool   ProcFired  { get; set; }
public double ProcBonus  { get; set; } // raw bonus damage from proc (0 if no proc)
```

---

## API: EquipmentController

File: `src/ROTA.Api/Controllers/EquipmentController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Route("api/equipment")]
[Authorize]
public sealed class EquipmentController : ControllerBase
{
    private readonly IEquipmentService _equipment;

    public EquipmentController(IEquipmentService equipment) => _equipment = equipment;

    private Guid PlayerId => Guid.Parse(User.FindFirst("sub")!.Value);

    // GET /api/equipment
    [HttpGet]
    public async Task<IActionResult> GetEquipment(CancellationToken ct)
    {
        var items = await _equipment.GetEquipmentAsync(PlayerId, ct);
        return Ok(items);
    }

    // PUT /api/equipment/{slot}
    [HttpPut("{slot}")]
    public async Task<IActionResult> Equip(string slot, [FromBody] EquipRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GearDefinitionId))
            return BadRequest(new { error = "gearDefinitionId is required." });

        var result = await _equipment.EquipAsync(PlayerId, slot, request.GearDefinitionId, ct);
        if (!result.Success)
            return result.FailureReason!.Contains("not found")
                ? NotFound(new { error = result.FailureReason })
                : UnprocessableEntity(new { error = result.FailureReason });

        return Ok(result.Item);
    }

    // DELETE /api/equipment/{slot}
    [HttpDelete("{slot}")]
    public async Task<IActionResult> Unequip(string slot, CancellationToken ct)
    {
        var result = await _equipment.UnequipAsync(PlayerId, slot, ct);
        if (!result.Success)
            return NotFound(new { error = result.FailureReason });

        return NoContent();
    }
}
```

---

## RaidService changes

### Constructor

Add `IEquipmentService equipment` parameter (last before `Random? random`). Assign `_equipment = equipment`.

```csharp
private readonly IEquipmentService _equipment;
```

### HitRaidAsync — damage formula block

Replace the existing block that starts with:
```csharp
// Compute damage — server RNG, never client.
var multiplier = 0.85 + _random.NextDouble() * 0.30;
long baseValue = (player.Stats!.BaseAttack * 4L) + player.Stats.BaseDefense;
damageFinal = Math.Max(1, (long)(baseValue * hitSize * multiplier));
```

With:
```csharp
// Compute effective stats — base + gear bonuses.
var combat = await _equipment.GetEffectiveCombatDataAsync(
    playerId, player.Stats!.BaseAttack, player.Stats.BaseDefense, ct);

var multiplier = 0.85 + _random.NextDouble() * 0.30;
long baseValue = (combat.EffectiveAttack * 4L) + combat.EffectiveDefense;
damageFinal = Math.Max(1, (long)(baseValue * hitSize * multiplier));

// Mount proc — once per hit, adds procPercent × base damage as a bonus.
bool procFired = false;
long procBonus  = 0;
if (combat.MountProc is not null && _random.NextDouble() < combat.MountProc.ProcChance)
{
    procBonus   = Math.Max(0, (long)(damageFinal * combat.MountProc.ProcPercent));
    damageFinal += procBonus;
    procFired   = true;
}
```

The `procFired` and `procBonus` variables must be captured in outer scope (like `isCrit`/`appliedCritMult` are). Add them to the outer-scope declarations:

```csharp
bool procFired  = false;
long procBonus  = 0;
```

### HitRaidAsync — response construction

Add to `RaidHitResponse`:
```csharp
ProcFired  = procFired,
ProcBonus  = procBonus,
```

### Audit string (step 10)

Append proc info when it fired:
```csharp
string procSuffix = procFired ? $" PROC +{procBonus}" : string.Empty;
// combine with critSuffix already there:
$"...{critSuffix}{procSuffix}..."
```

---

## StatService changes

Add `IEquipmentService _equipment` to constructor. In `GetStatsAsync`:

```csharp
public async Task<PlayerStatsResponse?> GetStatsAsync(Guid playerId, CancellationToken ct = default)
{
    var player = await _players.FindByIdWithStatsAsync(playerId, ct);
    if (player is null || player.IsDeleted) return null;
    var stats = player.Stats!;

    // Effective stats include gear bonuses.
    var combat = await _equipment.GetEffectiveCombatDataAsync(
        playerId, stats.BaseAttack, stats.BaseDefense, ct);

    return new PlayerStatsResponse
    {
        SkillPoints            = stats.SkillPoints,
        EnergyInvestment       = stats.EnergyInvestment,
        StaminaInvestment      = stats.StaminaInvestment,
        DiscernmentInvestment  = stats.DiscernmentInvestment,
        CurrentLsi             = (decimal)stats.ComputeLSI(player.Level),
        MaxEnergy              = stats.ComputeMaxEnergy(),
        MaxStamina             = stats.ComputeMaxStamina(),
        MaxGuildStamina        = player.Level,
        BaseAttack             = stats.BaseAttack,
        BaseDefense            = stats.BaseDefense,
        BaseMaxHealth          = stats.BaseMaxHealth,
        CurrentHealth          = stats.CurrentHealth,
        EffectiveAttack        = combat.EffectiveAttack,
        EffectiveDefense       = combat.EffectiveDefense,
    };
}
```

---

## PlayerService changes

Add `IEquipmentService _equipment` to constructor. In `GetProfileAsync`, after loading the player:

```csharp
// Compute effective stats for profile display.
int effAtk = player.Stats?.BaseAttack ?? 0;
int effDef = player.Stats?.BaseDefense ?? 0;
if (player.Stats is not null)
{
    var combat = await _equipment.GetEffectiveCombatDataAsync(
        playerId, player.Stats.BaseAttack, player.Stats.BaseDefense, ct);
    effAtk = combat.EffectiveAttack;
    effDef = combat.EffectiveDefense;
}
```

Add to `PlayerProfileResponse`:
```csharp
EffectiveAttack  = effAtk,
EffectiveDefense = effDef,
```

---

## Content: gear.json

File: `src/ROTA.Api/content/gear.json`

Full starter set. All Grey rarity. Total full-set bonus: +1 ATK / +3 DEF. Mount has proc only (no stat bonus).

```json
[
  {
    "id": "gear_conscript_helm",
    "name": "Conscript Helm",
    "description": "A battered iron helm worn by new recruits.",
    "rarity": "Grey",
    "slot": "Head",
    "bonusAttack": 0,
    "bonusDefense": 1,
    "procChance": null,
    "procPercent": null,
    "iconPath": "icons/gear/conscript_helm.png"
  },
  {
    "id": "gear_conscript_collar",
    "name": "Conscript Collar",
    "description": "A crude neck guard offering minimal protection.",
    "rarity": "Grey",
    "slot": "Neck",
    "bonusAttack": 0,
    "bonusDefense": 1,
    "procChance": null,
    "procPercent": null,
    "iconPath": "icons/gear/conscript_collar.png"
  },
  {
    "id": "gear_conscript_chest",
    "name": "Conscript Chest",
    "description": "Padded leather chest armour. Stops the smallest of blows.",
    "rarity": "Grey",
    "slot": "Torso",
    "bonusAttack": 0,
    "bonusDefense": 1,
    "procChance": null,
    "procPercent": null,
    "iconPath": "icons/gear/conscript_chest.png"
  },
  {
    "id": "gear_iron_ring",
    "name": "Iron Ring",
    "description": "A plain iron ring. Somehow sharpens the wearer's strikes.",
    "rarity": "Grey",
    "slot": "Ring1",
    "bonusAttack": 1,
    "bonusDefense": 0,
    "procChance": null,
    "procPercent": null,
    "iconPath": "icons/gear/iron_ring.png"
  },
  {
    "id": "gear_worn_band",
    "name": "Worn Band",
    "description": "A scratched metal band. No discernible power remains.",
    "rarity": "Grey",
    "slot": "Ring2",
    "bonusAttack": 0,
    "bonusDefense": 0,
    "procChance": null,
    "procPercent": null,
    "iconPath": "icons/gear/worn_band.png"
  },
  {
    "id": "gear_draft_horse",
    "name": "Draft Horse",
    "description": "A sturdy workhorse. Occasionally charges into the fray with surprising force.",
    "rarity": "Grey",
    "slot": "Mount",
    "bonusAttack": 0,
    "bonusDefense": 0,
    "procChance": 0.05,
    "procPercent": 2.0,
    "iconPath": "icons/gear/draft_horse.png"
  },
  {
    "id": "gear_conscript_boots",
    "name": "Conscript Boots",
    "description": "Worn leather boots. Better than bare feet.",
    "rarity": "Grey",
    "slot": "Boots",
    "bonusAttack": 0,
    "bonusDefense": 0,
    "procChance": null,
    "procPercent": null,
    "iconPath": "icons/gear/conscript_boots.png"
  },
  {
    "id": "gear_conscript_gloves",
    "name": "Conscript Gloves",
    "description": "Rough cloth gloves. Barely break-in.",
    "rarity": "Grey",
    "slot": "Gloves",
    "bonusAttack": 0,
    "bonusDefense": 0,
    "procChance": null,
    "procPercent": null,
    "iconPath": "icons/gear/conscript_gloves.png"
  }
]
```

---

## Unit tests: EquipmentServiceTests

File: `tests/ROTA.UnitTests/Services/EquipmentServiceTests.cs`

Write 8 tests:

1. **`Equip_ValidItem_ReturnsSuccess`** — equip "gear_conscript_helm" in Head slot when no existing row; verifies Success=true, item.Slot="Head", item.BonusDefense=1.

2. **`Equip_UnknownSlot_ReturnsFailure`** — slot="Shoulder" → FailureReason contains "Unknown slot".

3. **`Equip_GearNotFound_ReturnsFailure`** — gearDefinitionId="nonexistent" → FailureReason contains "not found".

4. **`Equip_SlotMismatch_ReturnsFailure`** — gear_conscript_helm (Slot=Head) in "Neck" → FailureReason contains "belongs to slot".

5. **`Equip_ExistingSlot_SwapsGear`** — existing row found via FindBySlotAsync (IsDeleted=false); Equip() should call UpdateAsync, not CreateAsync; Success=true.

6. **`Unequip_EquippedSlot_ReturnsSuccess`** — row exists with IsDeleted=false; Unequip() soft-deletes; Success=true.

7. **`Unequip_EmptySlot_ReturnsFailure`** — FindBySlotAsync returns null → FailureReason contains "No item equipped".

8. **`GetEffectiveCombatData_WithGear_AddsStatBonuses`** — two equipped items (BonusAttack=1, BonusDefense=3 total); base ATK=10, DEF=10; effective = ATK=11, DEF=13; MountProc=null.

9. **`GetEffectiveCombatData_WithMount_ReturnsProcData`** — one equipped mount (procChance=0.05, procPercent=2.0); EffectiveCombatData.MountProc is not null, ProcChance=0.05.

Use `new Mock<IPlayerEquipmentRepository>()`, `new Mock<IGearDefinitionProvider>()`, `new Mock<IAuditLogRepository>()`.
Gear definitions returned by `GetById` should be simple `GearDefinition` instances — no mocking needed there, just return real objects.

---

## Unit tests: RaidServiceTests changes

The `RaidService` constructor now takes `IEquipmentService`. All existing tests must continue to pass — add `Mock<IEquipmentService>` to `ServiceBundle` and `BuildService`.

Default mock setup (add to the existing default setups in `BuildService`):

```csharp
var equipment = new Mock<IEquipmentService>();
equipment.Setup(e => e.GetEffectiveCombatDataAsync(
        It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((Guid _, int atk, int def, CancellationToken _) =>
        new EffectiveCombatData(atk, def, null)); // pass-through, no gear bonus, no proc
```

Add `equipment` to the `ServiceBundle` record and pass `equipment.Object` to `RaidService` constructor.

**New test 1: `HitRaid_MountProcFires_DamageIncludesBonus`**
- Use `Random` seeded so `_random.NextDouble()` first call returns 0.5 (RNG multiplier ~1.0), second call returns 0.0 (proc fires — 0.0 < 0.05), third call returns 0.0 (crit check with chance=0 → no crit).
- Equipment mock returns `new EffectiveCombatData(10, 10, new GearProcData(0.05, 2.0))`.
- Hit size = 1.
- Verify `result.Response!.ProcFired == true` and `result.Response.ProcBonus > 0`.

**New test 2: `HitRaid_MountProcDoesNotFire_NoBonusDamage`**
- Equipment mock returns same `EffectiveCombatData(10, 10, new GearProcData(0.05, 2.0))`.
- Seed Random so proc roll returns 0.9 (0.9 >= 0.05 → no proc).
- Verify `result.Response!.ProcFired == false` and `result.Response.ProcBonus == 0`.

---

## StatServiceTests and PlayerServiceTests changes

Both services now take `IEquipmentService`. Add `Mock<IEquipmentService>` to their respective `BuildService` helpers with the same pass-through default:

```csharp
equipment.Setup(e => e.GetEffectiveCombatDataAsync(
    It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((Guid _, int atk, int def, CancellationToken _) =>
        new EffectiveCombatData(atk, def, null));
```

No new test cases needed for those files — just ensure the mock wiring compiles and existing tests pass.

---

## Migration

After implementing all code, run:
```
dotnet ef migrations add AddEquipmentSystem --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
```

Verify the generated migration creates `player_equipment` with:
- `id uuid DEFAULT gen_random_uuid() PRIMARY KEY`
- `player_id uuid NOT NULL`
- `slot integer NOT NULL`
- `gear_definition_id varchar(100) NOT NULL`
- `equipped_at timestamptz NOT NULL DEFAULT NOW()`
- `created_at timestamptz NOT NULL DEFAULT NOW()`
- `updated_at timestamptz NOT NULL DEFAULT NOW()`
- `is_deleted boolean NOT NULL DEFAULT false`
- Unique index: `ix_player_equipment_player_slot` on `(player_id, slot)`
- Index: `ix_player_equipment_player_id` on `player_id`

---

## Build verification

```
dotnet build
dotnet test
```

Target: 0 errors, 0 warnings, all tests green (215 existing + 9 new = 224 minimum).

---

## Constraint reminders

- Domain entities: private setters only. No EF attributes.
- EF mapping: Fluent API only. snake_case for all DB names.
- Content providers: singleton. Throw at startup on invalid data.
- No business logic in the controller.
- All stat mutations (equip/unequip) write to audit_log.
- `PlayerId` comes from verified JWT `sub` claim only — never route/body.
- Do NOT run `dotnet ef database update`.
- Do NOT add or remove any other features beyond what is specified here.
