using Application.Contracts.Common;
using Application.Abstraction;
using Application.Contracts.Organization;

namespace Application.Service.Organization;

public interface IOrganizationSettingsService
{
    Task<Result<OrganizationResponse>> GetCurrentAsync(string actorId, CancellationToken cancellationToken = default);
    Task<Result<OrganizationResponse>> GetTenantAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<Result<TenantResponse>> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<LegalEntityResponse>>> GetLegalEntitiesAsync(PaginationRequest pagination, LegalEntityListFilter filter, CancellationToken cancellationToken = default);
    Task<Result<LegalEntityResponse>> GetLegalEntityAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<LegalEntityResponse>> CreateLegalEntityAsync(CreateLegalEntityRequest request, CancellationToken cancellationToken = default);
    Task<Result<LegalEntityResponse>> UpdateLegalEntityAsync(int id, UpdateLegalEntityRequest request, CancellationToken cancellationToken = default);
    Task<Result<LegalEntityResponse>> DeleteLegalEntityAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<BranchResponse>> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<PlatformAccountResponse>>> GetPlatformAccountsAsync(PaginationRequest pagination, PlatformAccountListFilter filter, CancellationToken cancellationToken = default);
    Task<Result<PlatformAccountResponse>> GetPlatformAccountAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<PlatformAccountResponse>> CreatePlatformAccountAsync(CreatePlatformAccountRequest request, CancellationToken cancellationToken = default);
    Task<Result<PlatformAccountResponse>> UpdatePlatformAccountAsync(int id, UpdatePlatformAccountRequest request, CancellationToken cancellationToken = default);
    Task<Result<PlatformAccountResponse>> DeletePlatformAccountAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<LegacyCompanyPlatformMappingResponse>> CreateLegacyCompanyPlatformMappingAsync(CreateLegacyCompanyPlatformMappingRequest request, CancellationToken cancellationToken = default);
}
