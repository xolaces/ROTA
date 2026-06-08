namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// Append-only record of one mastery re-spec / pledge change (System 22 Phase A). Idempotency +
/// the period caps are enforced by the unique (player_id, reference_id) index — the referenceId
/// encodes the period/scope: paid <c>respec:paid:{p}:{isoYear}-Www</c>, free-monthly
/// <c>respec:free:{p}:{yyyy-MM}</c>, unlock <c>respec:unlock:{p}:{ancient}</c>.
/// </summary>
public class MasteryRespecTransaction
{
    // Required by EF Core
    private MasteryRespecTransaction() { }

    public static MasteryRespecTransaction Create(
        Guid playerId,
        MasteryRespecKind kind,
        MasteryAncient? fromAncient,
        MasteryAncient toAncient,
        int gemCost,
        string referenceId)
        => new()
        {
            Id          = Guid.NewGuid(),
            PlayerId    = playerId,
            Kind        = kind,
            FromAncient = fromAncient,
            ToAncient   = toAncient,
            GemCost     = gemCost,
            ReferenceId = referenceId,
            CreatedAt   = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public MasteryRespecKind Kind { get; private set; }
    public MasteryAncient? FromAncient { get; private set; }
    public MasteryAncient ToAncient { get; private set; }
    public int GemCost { get; private set; }
    public string ReferenceId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
