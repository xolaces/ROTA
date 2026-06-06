namespace ROTA.Domain.Enums;

/// <summary>Lifecycle of a Gauntlet event (System 16). Admin-triggered transitions.</summary>
public enum GauntletEventState
{
    /// <summary>Created but not yet open for play.</summary>
    Scheduled = 0,

    /// <summary>Open — players may join, hit Gauntlet raids, and accrue score.</summary>
    Active = 1,

    /// <summary>Closed to new score; awaiting settlement.</summary>
    Closed = 2,

    /// <summary>Prizes distributed; terminal state.</summary>
    Settled = 3,
}
