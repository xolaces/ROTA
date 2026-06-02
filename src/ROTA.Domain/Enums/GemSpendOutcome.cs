namespace ROTA.Domain.Enums;

/// <summary>
/// Tri-state result of <see cref="ROTA.Application.Interfaces.IGemService.SpendGemsAsync"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>
///     <see cref="Charged"/> — the ledger row was written and balance was deducted.
///   </description></item>
///   <item><description>
///     <see cref="AlreadyProcessed"/> — the referenceId already exists in the ledger;
///     no second row was written.  This is an idempotent SUCCESS: the original charge
///     committed, so callers must proceed with the grant step.
///   </description></item>
///   <item><description>
///     <see cref="InsufficientBalance"/> — balance was below the requested amount; no
///     row was written.  Callers should return an error to the player.
///   </description></item>
/// </list>
/// </remarks>
public enum GemSpendOutcome
{
    Charged            = 1,
    AlreadyProcessed   = 2,
    InsufficientBalance = 3,
}
