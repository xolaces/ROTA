using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

// BETA (System 16 Slice 6) — singleton; loads content/gauntlet_shop.json at construction and
// validates it against the spec (referential integrity on payloadId, price > 0, currency valid,
// no duplicate ids, GemBundle/StrikeRefill amount > 0). Throws InvalidOperationException at
// startup on any invalid data — mirrors GauntletContentProvider (Slice 1). Eagerly constructed
// in Program.cs so a bad catalogue fails the boot, not the first purchase.
public sealed class GauntletShopProvider : IGauntletShopProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<GauntletShopEntry> _entries;        // insertion order
    private readonly IReadOnlyDictionary<string, GauntletShopEntry> _byId;

    public GauntletShopProvider(
        string contentRootPath,
        IUnitDefinitionProvider unitDefs,
        ILegionDefinitionProvider legionDefs,
        IGearDefinitionProvider gearDefs)
    {
        var entries = Load(contentRootPath);
        Validate(entries, unitDefs, legionDefs, gearDefs);

        _entries = entries;
        _byId = entries.ToDictionary(e => e.Id, e => e, StringComparer.Ordinal);
    }

    // ── Accessors ────────────────────────────────────────────────────────────

    public IReadOnlyList<GauntletShopEntry> GetAll() => _entries;

    public GauntletShopEntry? GetById(string id)
        => _byId.TryGetValue(id, out var e) ? e : null;

    // ── Loading ──────────────────────────────────────────────────────────────

    private static List<GauntletShopEntry> Load(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "gauntlet_shop.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<GauntletShopEntry>>(json, JsonOptions)
            ?? throw new InvalidOperationException("gauntlet_shop.json deserialized to null.");
    }

    // ── Validation ─────────────────────────────────────────────────────────────

    private static void Validate(
        List<GauntletShopEntry> entries,
        IUnitDefinitionProvider unitDefs,
        ILegionDefinitionProvider legionDefs,
        IGearDefinitionProvider gearDefs)
    {
        if (entries.Count == 0)
            throw new InvalidOperationException("gauntlet_shop.json: no shop entries defined.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Id))
                throw new InvalidOperationException("gauntlet_shop.json: a shop entry has an empty id.");
            if (!seen.Add(e.Id))
                throw new InvalidOperationException(
                    $"gauntlet_shop.json: duplicate id '{e.Id}'.");

            if (!Enum.IsDefined(e.Currency))
                throw new InvalidOperationException(
                    $"gauntlet_shop.json: entry '{e.Id}' has an invalid currency.");
            if (!Enum.IsDefined(e.RewardKind))
                throw new InvalidOperationException(
                    $"gauntlet_shop.json: entry '{e.Id}' has an invalid rewardKind.");

            if (e.Price <= 0)
                throw new InvalidOperationException(
                    $"gauntlet_shop.json: entry '{e.Id}' has price {e.Price}; must be > 0.");

            switch (e.RewardKind)
            {
                case GauntletShopRewardKind.Unit:
                    RequirePayload(e);
                    if (unitDefs.GetById(e.PayloadId) is null)
                        throw new InvalidOperationException(
                            $"gauntlet_shop.json: entry '{e.Id}' Unit payloadId '{e.PayloadId}' " +
                            "is not defined in units.json.");
                    RequireOwnOnce(e);
                    break;

                case GauntletShopRewardKind.Legion:
                    RequirePayload(e);
                    if (legionDefs.GetById(e.PayloadId) is null)
                        throw new InvalidOperationException(
                            $"gauntlet_shop.json: entry '{e.Id}' Legion payloadId '{e.PayloadId}' " +
                            "is not defined in legions.json.");
                    RequireOwnOnce(e);
                    break;

                case GauntletShopRewardKind.Gear:
                    RequirePayload(e);
                    if (gearDefs.GetById(e.PayloadId) is null)
                        throw new InvalidOperationException(
                            $"gauntlet_shop.json: entry '{e.Id}' Gear payloadId '{e.PayloadId}' " +
                            "is not defined in gear.json.");
                    RequireOwnOnce(e);
                    break;

                case GauntletShopRewardKind.GemBundle:
                case GauntletShopRewardKind.StrikeRefill:
                    if (e.Amount <= 0)
                        throw new InvalidOperationException(
                            $"gauntlet_shop.json: entry '{e.Id}' ({e.RewardKind}) has amount " +
                            $"{e.Amount}; must be > 0.");
                    break;
            }
        }
    }

    private static void RequirePayload(GauntletShopEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.PayloadId))
            throw new InvalidOperationException(
                $"gauntlet_shop.json: entry '{e.Id}' ({e.RewardKind}) requires a non-empty payloadId.");
    }

    // Own-once kinds must be maxOwned:1 so the ownership pre-check (BuyFromShopAsync step 2) gates
    // them; a missing/other maxOwned would let the shop re-sell an owned item.
    private static void RequireOwnOnce(GauntletShopEntry e)
    {
        if (e.MaxOwned != 1)
            throw new InvalidOperationException(
                $"gauntlet_shop.json: entry '{e.Id}' ({e.RewardKind}) must have maxOwned = 1 " +
                $"(own-once); got {(e.MaxOwned.HasValue ? e.MaxOwned.Value.ToString() : "null")}.");
    }
}
