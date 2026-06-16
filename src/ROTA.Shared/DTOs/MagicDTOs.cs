namespace ROTA.Shared.DTOs;

public enum MagicApplyFailureCode
{
    RaidNotFound,
    RaidNotActive,
    WorldGateBlocked,     // non-admin tried to apply magic on a World raid
    MagicNotOwned,
    NotAParticipant,      // non-world: caller is not summoner or participant
    AlreadyAppliedByPlayer, // one-per-player (non-world)
    DuplicateMagic,       // same magic already on this raid
    SlotsFull,
    MagicNotFound,        // magic not on this raid (remove)
    RemoveNotAllowed,     // non-world: only summoner may remove; world: only Admin
}

public class ApplyMagicRequest
{
    public string MagicDefinitionId { get; set; } = string.Empty;
}

public class MagicApplyResult
{
    public bool                   Success     { get; set; }
    public MagicApplyFailureCode  FailureCode { get; set; }
    public string?                FailureReason { get; set; }
}

public class AppliedMagicResponse
{
    public string          MagicDefinitionId { get; set; } = string.Empty;
    public string          Name              { get; set; } = string.Empty;
    public string          EffectType        { get; set; } = string.Empty;
    public double          ProcChance        { get; set; }
    public double          ProcAmount        { get; set; }
    public string          AppliedByUsername { get; set; } = string.Empty;
    public DateTimeOffset  AppliedAt         { get; set; }
}

public class BuyMagicRequest
{
    public string MagicDefinitionId { get; set; } = string.Empty;
}

public enum BuyMagicFailureCode
{
    MagicNotFound,
    NotForSale,          // GemPrice == 0 (magic not in gem shop)
    InsufficientBalance,
    AlreadyOwned,        // player already owns this magic — no charge made
}

public class BuyMagicResult
{
    public bool               Success       { get; set; }
    public BuyMagicFailureCode FailureCode  { get; set; }
    public string?            FailureReason { get; set; }
    public int                GemPrice      { get; set; }
}

public class OwnedMagicResponse
{
    public string MagicDefinitionId { get; set; } = string.Empty;
    public string Name              { get; set; } = string.Empty;
    public string Description       { get; set; } = string.Empty;
    public string Rarity            { get; set; } = string.Empty;
    public string Category          { get; set; } = string.Empty;
    public string EffectType        { get; set; } = string.Empty;
    public double ProcChance        { get; set; }
    public double ProcAmount        { get; set; }
    public bool   Stacks            { get; set; }
    public string IconPath          { get; set; } = string.Empty;
    public string Acquisition       { get; set; } = string.Empty;
    public DateTimeOffset AcquiredAt { get; set; }
}

// One catalogue entry: the full magic definition (from content/magics.json) plus the
// caller's owned-state and the gem price. The Bazaar renders the complete catalogue and
// uses ForSale/IsOwned to decide which buy/owned affordance to show.
public class MagicCatalogueEntry
{
    public string MagicDefinitionId { get; set; } = string.Empty;
    public string Name              { get; set; } = string.Empty;
    public string Description       { get; set; } = string.Empty;
    public string Rarity            { get; set; } = string.Empty;
    public string Category          { get; set; } = string.Empty;
    public string EffectType        { get; set; } = string.Empty;
    public double ProcChance        { get; set; }
    public double ProcAmount        { get; set; }
    public bool   Stacks            { get; set; }
    public string IconPath          { get; set; } = string.Empty;
    public string Acquisition       { get; set; } = string.Empty;
    public int    GemPrice          { get; set; }
    public bool   ForSale           { get; set; }  // GemPrice > 0 — purchasable in the gem shop
    public bool   IsOwned           { get; set; }  // caller already owns this magic
}

public class MagicCatalogueResponse
{
    public IReadOnlyList<MagicCatalogueEntry> Entries { get; set; } = new List<MagicCatalogueEntry>();
}
