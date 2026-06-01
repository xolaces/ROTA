using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class SlotSpec
{
    public SlotConstraintType ConstraintType  { get; set; } = SlotConstraintType.None;
    // null when ConstraintType = None; otherwise the string name of the enum value (e.g. "Tank", "Strength")
    public string?            ConstraintValue { get; set; }
}
