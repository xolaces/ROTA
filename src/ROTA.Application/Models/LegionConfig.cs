namespace ROTA.Application.Models;

public class LegionConfig
{
    // Master dominance dial: tune down by playtest to keep legion below ~90% of a hit.
    // Applied in combat only (RaidService); ComputeLegionPowerAsync returns raw power.
    public double PowerScaling { get; set; } = 1.0;

    // Unit-type coefficients: General {Atk:2.0, Def:0.4}, Troop {Atk:1.44, Def:0.36}
    // These mirror DotD's per-unit-type coefficients.
    public Dictionary<string, UnitCoefficients> UnitCoefficients { get; set; } = new()
    {
        ["General"] = new UnitCoefficients { Atk = 2.00, Def = 0.40 },
        ["Troop"]   = new UnitCoefficients { Atk = 1.44, Def = 0.36 },
    };

    // Aggregate cap on unit-ability proc bonuses: cap = MaxUnitProcBonus × preProc.
    // Separate from MagicConfig.MaxAggregateProcBonus (each has its own cap).
    public double MaxUnitProcBonus { get; set; } = 5.0;
}

public class UnitCoefficients
{
    public double Atk { get; set; }
    public double Def { get; set; }
}
