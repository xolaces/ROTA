namespace ROTA.Shared.DTOs;

public class ClassRegenRates
{
    public string CurrentClass { get; set; } = string.Empty;
    public double EnergyRegenMinutesPerPoint { get; set; }
    public double StaminaRegenMinutesPerPoint { get; set; }
    public double GuildStaminaRegenMinutesPerPoint { get; set; }
    public List<string> AvailableChoices { get; set; } = new();
    public bool IsConverged { get; set; }
}

public class ChooseClassRequest
{
    public string PlayerClass { get; set; } = string.Empty;
}
