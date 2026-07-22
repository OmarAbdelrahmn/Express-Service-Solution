using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Organization;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Organization;

public class OrganizationSettingsService(ApplicationDbcontext dbcontext) : IOrganizationSettingsService
{
    public async Task<Result<OrganizationResponse>> GetCurrentAsync(string actorId, CancellationToken cancellationToken = default)
    {
        var hasFullAccountingRole = await dbcontext.UserRoles
            .AsNoTracking()
            .Where(x => x.UserId == actorId)
            .Join(
                dbcontext.ApplicationRoles.AsNoTracking(),
                userRole => userRole.RoleId,
                role => role.Id,
                (_, role) => role.Name)
            .AnyAsync(roleName => roleName == "Master" || roleName == "Accountant", cancellationToken);

        HashSet<int>? accessibleLegalEntityIds = null;
        if (!hasFullAccountingRole)
        {
            accessibleLegalEntityIds = (await dbcontext.FinancialUserAccesses
                .AsNoTracking()
                .Where(x => x.UserId == actorId && (x.Permissions & FinancialPermission.View) == FinancialPermission.View)
                .OrderBy(x => x.LegalEntityId)
                .Select(x => x.LegalEntityId)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        var tenantQuery = dbcontext.Tenants
            .AsNoTracking()
            .Include(x => x.LegalEntities)
                .ThenInclude(x => x.Branches)
            .Include(x => x.LegalEntities)
                .ThenInclude(x => x.PlatformAccounts)
            .AsQueryable();

        if (accessibleLegalEntityIds is not null)
            tenantQuery = tenantQuery.Where(x => x.LegalEntities.Any(entity => accessibleLegalEntityIds.Contains(entity.Id)));

        var tenant = await tenantQuery
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return tenant is null
            ? Result.Failure<OrganizationResponse>(OrganizationErrors.TenantNotFound)
            : Result.Success(ToResponse(tenant, accessibleLegalEntityIds));
    }

    public async Task<Result<OrganizationResponse>> GetTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await dbcontext.Tenants
            .AsNoTracking()
            .Include(x => x.LegalEntities)
                .ThenInclude(x => x.Branches)
            .Include(x => x.LegalEntities)
                .ThenInclude(x => x.PlatformAccounts)
            .SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);

        return tenant is null
            ? Result.Failure<OrganizationResponse>(OrganizationErrors.TenantNotFound)
            : Result.Success(ToResponse(tenant));
    }

    public async Task<Result<TenantResponse>> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(request.Code);
        if (await dbcontext.Tenants.AnyAsync(x => x.Code == code, cancellationToken))
            return Result.Failure<TenantResponse>(OrganizationErrors.DuplicateCode);

        var tenant = new Tenant { Code = code, Name = request.Name.Trim() };
        dbcontext.Tenants.Add(tenant);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantResponse(tenant.Id, tenant.Code, tenant.Name, tenant.IsActive));
    }

    public async Task<Result<LegalEntityResponse>> CreateLegalEntityAsync(CreateLegalEntityRequest request, CancellationToken cancellationToken = default)
    {
        if (!await dbcontext.Tenants.AnyAsync(x => x.Id == request.TenantId, cancellationToken))
            return Result.Failure<LegalEntityResponse>(OrganizationErrors.TenantNotFound);

        var code = NormalizeCode(request.Code);
        if (await dbcontext.LegalEntities.AnyAsync(x => x.TenantId == request.TenantId && x.Code == code, cancellationToken))
            return Result.Failure<LegalEntityResponse>(OrganizationErrors.DuplicateCode);

        var entity = new LegalEntity
        {
            TenantId = request.TenantId,
            Code = code,
            LegalName = request.LegalName.Trim(),
            BaseCurrencyCode = NormalizeCode(request.BaseCurrencyCode),
            TaxRegistrationNumber = TrimToNull(request.TaxRegistrationNumber)
        };
        dbcontext.LegalEntities.Add(entity);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(entity));
    }

    public async Task<Result<BranchResponse>> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken = default)
    {
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == request.LegalEntityId, cancellationToken))
            return Result.Failure<BranchResponse>(OrganizationErrors.LegalEntityNotFound);

        var code = NormalizeCode(request.Code);
        if (await dbcontext.Branches.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.Code == code, cancellationToken))
            return Result.Failure<BranchResponse>(OrganizationErrors.DuplicateCode);

        var branch = new Branch { LegalEntityId = request.LegalEntityId, Code = code, Name = request.Name.Trim() };
        dbcontext.Branches.Add(branch);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BranchResponse(branch.Id, branch.Code, branch.Name, branch.IsActive));
    }

    public async Task<Result<PlatformAccountResponse>> CreatePlatformAccountAsync(CreatePlatformAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == request.LegalEntityId, cancellationToken))
            return Result.Failure<PlatformAccountResponse>(OrganizationErrors.LegalEntityNotFound);

        var code = NormalizeCode(request.Code);
        if (await dbcontext.PlatformAccounts.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.Code == code, cancellationToken))
            return Result.Failure<PlatformAccountResponse>(OrganizationErrors.DuplicateCode);

        var platformAccount = new PlatformAccount
        {
            LegalEntityId = request.LegalEntityId,
            Code = code,
            PlatformName = request.PlatformName.Trim(),
            ExternalAccountReference = TrimToNull(request.ExternalAccountReference)
        };
        dbcontext.PlatformAccounts.Add(platformAccount);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(platformAccount));
    }

    public async Task<Result<LegacyCompanyPlatformMappingResponse>> CreateLegacyCompanyPlatformMappingAsync(CreateLegacyCompanyPlatformMappingRequest request, CancellationToken cancellationToken = default)
    {
        if (!await dbcontext.Companies.AnyAsync(x => x.Id == request.CompanyId, cancellationToken))
            return Result.Failure<LegacyCompanyPlatformMappingResponse>(OrganizationErrors.LegacyCompanyNotFound);

        if (!await dbcontext.PlatformAccounts.AnyAsync(x => x.Id == request.PlatformAccountId, cancellationToken))
            return Result.Failure<LegacyCompanyPlatformMappingResponse>(OrganizationErrors.PlatformAccountNotFound);

        if (await dbcontext.LegacyCompanyPlatformMappings.AnyAsync(x => x.CompanyId == request.CompanyId, cancellationToken))
            return Result.Failure<LegacyCompanyPlatformMappingResponse>(OrganizationErrors.LegacyCompanyAlreadyMapped);

        var mapping = new LegacyCompanyPlatformMapping
        {
            CompanyId = request.CompanyId,
            PlatformAccountId = request.PlatformAccountId,
            EffectiveFrom = request.EffectiveFrom.ToUniversalTime()
        };
        dbcontext.LegacyCompanyPlatformMappings.Add(mapping);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(new LegacyCompanyPlatformMappingResponse(mapping.Id, mapping.CompanyId, mapping.PlatformAccountId, mapping.EffectiveFrom));
    }

    private static OrganizationResponse ToResponse(Tenant tenant, IReadOnlySet<int>? accessibleLegalEntityIds = null) => new(
        new TenantResponse(tenant.Id, tenant.Code, tenant.Name, tenant.IsActive),
        tenant.LegalEntities
            .Where(x => accessibleLegalEntityIds is null || accessibleLegalEntityIds.Contains(x.Id))
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .Select(ToResponse)
            .ToArray());

    private static LegalEntityResponse ToResponse(LegalEntity entity) => new(
        entity.Id,
        entity.TenantId,
        entity.Code,
        entity.LegalName,
        entity.BaseCurrencyCode,
        entity.TaxRegistrationNumber,
        entity.IsActive,
        entity.Branches.OrderBy(x => x.Code).ThenBy(x => x.Id).Select(x => new BranchResponse(x.Id, x.Code, x.Name, x.IsActive)).ToArray(),
        entity.PlatformAccounts.OrderBy(x => x.Code).ThenBy(x => x.Id).Select(ToResponse).ToArray());

    private static PlatformAccountResponse ToResponse(PlatformAccount account) => new(
        account.Id,
        account.Code,
        account.PlatformName,
        account.ExternalAccountReference,
        account.IsActive);

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
