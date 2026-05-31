namespace ROTA.Shared.DTOs;

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
