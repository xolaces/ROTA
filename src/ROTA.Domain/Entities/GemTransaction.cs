using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

public class GemTransaction
{
    // Required by EF Core
    private GemTransaction() { }

    public static GemTransaction Create(
        Guid playerId,
        int amount,
        GemTransactionType transactionType,
        string? referenceId)
        => new GemTransaction
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Amount = amount,
            TransactionType = transactionType,
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }

    public int Amount { get; private set; }

    public GemTransactionType TransactionType { get; private set; }

    public string? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
