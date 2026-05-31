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
