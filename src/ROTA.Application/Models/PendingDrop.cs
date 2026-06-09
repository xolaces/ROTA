namespace ROTA.Application.Models;

// T57 — a deferred raid collection drop (magic / unit / legion / gear). Rolled on the killing hit and
// stored as JSON on the participant row, then GRANTED via the matching service when the participant
// presses Loot. Inventory items use ItemsEarnedJson; gold + XP are NOT deferred (on-hit rewards).
public sealed class PendingDrop
{
    public string Kind { get; set; } = string.Empty;   // "Magic" | "Unit" | "Legion" | "Gear"
    public string Id { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}
