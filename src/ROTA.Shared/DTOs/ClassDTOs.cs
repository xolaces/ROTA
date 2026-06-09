namespace ROTA.Shared.DTOs;

public class ClassRegenRates
{
    public string CurrentClass { get; set; } = string.Empty;
    public double EnergyRegenMinutesPerPoint { get; set; }
    public double StaminaRegenMinutesPerPoint { get; set; }
    public double GuildStaminaRegenMinutesPerPoint { get; set; }
    public double HealthRegenMinutesPerPoint { get; set; }   // T56
    public List<string> AvailableChoices { get; set; } = new();
    // Per-choice regen preview so the unlock overlay can show each candidate class's benefit
    // (faster regen = lower minutes-per-point) without a round-trip per class. Empty when there
    // is no pending choice. Mirrors AvailableChoices one-to-one.
    public List<ClassChoicePreview> ChoicePreviews { get; set; } = new();
    public bool IsConverged { get; set; }
}

public class ClassChoicePreview
{
    public string ClassName { get; set; } = string.Empty;
    public double EnergyRegenMinutesPerPoint { get; set; }
    public double StaminaRegenMinutesPerPoint { get; set; }
    public double GuildStaminaRegenMinutesPerPoint { get; set; }
}

public class ChooseClassRequest
{
    public string PlayerClass { get; set; } = string.Empty;
}
