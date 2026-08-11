using Application.Abstraction;
using Application.Contracts.SystemAudit;

namespace Application.Service.SystemAudit;

public interface ISystemAuditService
{
    Task<Result<SystemAuditPageResponse>> GetAllAsync(SystemAuditQuery query, CancellationToken cancellationToken = default);
    Task<Result<SystemAuditDetailResponse>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
