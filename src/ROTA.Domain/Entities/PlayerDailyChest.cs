using System.Text.Json;
using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

/// <summary>
/// System 29 — one player's daily chest for one UTC day. All six chests are rolled when the day's chest
/// is first read and stored here BEFORE the player picks, so the reveal shows what was really behind the
/// other five and re-opening the panel can never re-roll them.
///
/// The pick is claimed by a conditional UPDATE in the repository (the BetaKey latch pattern), not by a
/// method here: two concurrent picks must race in the database, where only one can match
/// <c>picked_index IS NULL</c>.
/// </summary>
public class PlayerDailyChest
{
    public const int ChestCount = 6;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Required by EF Core
    private PlayerDailyChest() { }

    public static PlayerDailyChest Create(Guid playerId, DateOnly chestDay, IReadOnlyList<DailyChestSlot> contents)
    {
        if (contents.Count != ChestCount)
            throw new ArgumentException($"A daily chest holds exactly {ChestCount} slots, got {contents.Count}.", nameof(contents));
        if (contents.Any(c => string.IsNullOrWhiteSpace(c.ItemDefinitionId) || c.Quantity <= 0))
            throw new ArgumentException("Every slot needs an item and a positive quantity.", nameof(contents));

        var now = DateTimeOffset.UtcNow;
        return new PlayerDailyChest
        {
            Id           = Guid.NewGuid(),
            PlayerId     = playerId,
            ChestDay     = chestDay,
            ContentsJson = JsonSerializer.Serialize(contents, JsonOptions),
            CreatedAt    = now,
            UpdatedAt    = now,
        };
    }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public DateOnly ChestDay { get; private set; }

    /// <summary>The six rolled slots, in chest order, serialized. Read through <see cref="GetContents"/>.</summary>
    public string ContentsJson { get; private set; } = "[]";

    /// <summary>Null until the player picks; then 0–5.</summary>
    public short? PickedIndex { get; private set; }
    public DateTimeOffset? PickedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public bool IsPicked => PickedIndex.HasValue;

    public IReadOnlyList<DailyChestSlot> GetContents()
        => JsonSerializer.Deserialize<List<DailyChestSlot>>(ContentsJson, JsonOptions) ?? new List<DailyChestSlot>();
}

/// <summary>
/// One chest's contents. Rarity is snapshotted at roll time so the reveal stays stable even if
/// items.json re-tiers the item later the same day.
/// </summary>
public sealed record DailyChestSlot(string ItemDefinitionId, int Quantity, ItemRarity Rarity);
