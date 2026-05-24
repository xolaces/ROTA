using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IAuditLogRepository
{
    Task AppendAsync(AuditLog entry, CancellationToken ct = default);
}
