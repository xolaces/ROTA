using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IMasteryRespecRepository
{
    /// <summary>True if a re-spec row with this (player, referenceId) already exists (period/scope check).</summary>
    Task<bool> ReferenceExistsAsync(Guid playerId, string referenceId, CancellationToken ct = default);

    /// <summary>Inserts the ledger row. Returns false if (player_id, reference_id) already exists (concurrent dup).</summary>
    Task<bool> CreateAsync(MasteryRespecTransaction tx, CancellationToken ct = default);
}
