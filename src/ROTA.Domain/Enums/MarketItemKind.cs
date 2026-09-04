namespace ROTA.Domain.Enums;

/// <summary>
/// What a listing is carrying. Deliberately NOT the full ItemType surface: units, legions and magics
/// are own-once and would need a "buyer already owns it" refund path, and sigils summon raids, which
/// makes them closer to a service than an object. The prototype trades the two kinds that stack and
/// have no side effects on acquisition.
/// </summary>
public enum MarketItemKind
{
    Item = 0,
    Gear = 1,
}
