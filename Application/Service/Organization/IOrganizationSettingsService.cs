using Application.Abstraction;
using Application.Contracts.Organization;

namespace Application.Service.Organization;

public interface IOrganizationSettingsService
{
    Task<Result<OrganizationResponse>> GetCurrentAsync(string actorId, CancellationToken cancellationToken = default);
    Task<Result<OrganizationResponse>> GetTenantAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<Result<TenantResponse>> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);
    Task<Result<LegalEntityResponse>> CreateLegalEntityAsync(CreateLegalEntityRequest request, CancellationToken cancellationToken = default);
    Task<Result<BranchResponse>> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken = default);
    Task<Result<PlatformAccountResponse>> CreatePlatformAccountAsync(CreatePlatformAccountRequest request, CancellationToken cancellationToken = default);
    Task<Result<LegacyCompanyPlatformMappingResponse>> CreateLegacyCompanyPlatformMappingAsync(CreateLegacyCompanyPlatformMappingRequest request, CancellationToken cancellationToken = default);
}
