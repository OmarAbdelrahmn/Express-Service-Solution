using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
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
            : Result.Success(ToResponse(tenant, accessibleLegalEntityIds, includeInactive: false));
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

    public async Task<Result<PagedResponse<LegalEntityResponse>>> GetLegalEntitiesAsync(
        PaginationRequest pagination,
        LegalEntityListFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = dbcontext.LegalEntities
            .AsNoTracking()
            .Include(x => x.Branches)
            .Include(x => x.PlatformAccounts)
            .AsQueryable();

        if (filter.TenantId.HasValue)
            query = query.Where(x => x.TenantId == filter.TenantId.Value);
        if (filter.Active.HasValue)
            query = query.Where(x => x.IsActive == filter.Active.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.ToUpper().Contains(search) || x.LegalName.ToUpper().Contains(search) ||
                                     (x.TaxRegistrationNumber != null && x.TaxRegistrationNumber.ToUpper().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var ascending = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var ordered = (filter.SortBy?.Trim().ToLowerInvariant(), ascending) switch
        {
            ("legalname", true) => query.OrderBy(x => x.LegalName).ThenBy(x => x.Id),
            ("legalname", false) => query.OrderByDescending(x => x.LegalName).ThenByDescending(x => x.Id),
            ("createdat", true) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            ("createdat", false) => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
            ("id", false) => query.OrderByDescending(x => x.Id),
            ("id", true) => query.OrderBy(x => x.Id),
            ("code", false) => query.OrderByDescending(x => x.Code).ThenByDescending(x => x.Id),
            _ => query.OrderBy(x => x.Code).ThenBy(x => x.Id)
        };

        var items = (await ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken))
            .Select(x => ToResponse(x))
            .ToArray();
        return Result.Success(new PagedResponse<LegalEntityResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<LegalEntityResponse>> GetLegalEntityAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await dbcontext.LegalEntities
            .AsNoTracking()
            .Include(x => x.Branches)
            .Include(x => x.PlatformAccounts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity is null
            ? Result.Failure<LegalEntityResponse>(OrganizationErrors.LegalEntityNotFound)
            : Result.Success(ToResponse(entity));
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

    public async Task<Result<LegalEntityResponse>> UpdateLegalEntityAsync(int id, UpdateLegalEntityRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await dbcontext.LegalEntities
            .Include(x => x.Branches)
            .Include(x => x.PlatformAccounts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return Result.Failure<LegalEntityResponse>(OrganizationErrors.LegalEntityNotFound);

        var code = NormalizeCode(request.Code);
        if (await dbcontext.LegalEntities.AnyAsync(x => x.TenantId == entity.TenantId && x.Code == code && x.Id != id, cancellationToken))
            return Result.Failure<LegalEntityResponse>(OrganizationErrors.DuplicateCode);

        entity.Code = code;
        entity.LegalName = request.LegalName.Trim();
        entity.BaseCurrencyCode = NormalizeCode(request.BaseCurrencyCode);
        entity.TaxRegistrationNumber = TrimToNull(request.TaxRegistrationNumber);
        entity.IsActive = request.IsActive;
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(entity));
    }

    public async Task<Result<LegalEntityResponse>> DeleteLegalEntityAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await dbcontext.LegalEntities
            .Include(x => x.Branches)
            .Include(x => x.PlatformAccounts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return Result.Failure<LegalEntityResponse>(OrganizationErrors.LegalEntityNotFound);

        entity.IsActive = false;
        foreach (var platformAccount in entity.PlatformAccounts)
            platformAccount.IsActive = false;
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

    public async Task<Result<PagedResponse<PlatformAccountResponse>>> GetPlatformAccountsAsync(
        PaginationRequest pagination,
        PlatformAccountListFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (filter.LegalEntityId <= 0)
            return Result.Failure<PagedResponse<PlatformAccountResponse>>(OrganizationErrors.LegalEntityNotFound);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == filter.LegalEntityId, cancellationToken))
            return Result.Failure<PagedResponse<PlatformAccountResponse>>(OrganizationErrors.LegalEntityNotFound);

        var query = dbcontext.PlatformAccounts
            .AsNoTracking()
            .Where(x => x.LegalEntityId == filter.LegalEntityId);

        if (filter.Active.HasValue)
            query = query.Where(x => x.IsActive == filter.Active.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.ToUpper().Contains(search) || x.PlatformName.ToUpper().Contains(search) ||
                                     (x.ExternalAccountReference != null && x.ExternalAccountReference.ToUpper().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var ascending = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var ordered = (filter.SortBy?.Trim().ToLowerInvariant(), ascending) switch
        {
            ("platformname", true) => query.OrderBy(x => x.PlatformName).ThenBy(x => x.Id),
            ("platformname", false) => query.OrderByDescending(x => x.PlatformName).ThenByDescending(x => x.Id),
            ("createdat", true) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            ("createdat", false) => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
            ("id", false) => query.OrderByDescending(x => x.Id),
            ("id", true) => query.OrderBy(x => x.Id),
            ("code", false) => query.OrderByDescending(x => x.Code).ThenByDescending(x => x.Id),
            _ => query.OrderBy(x => x.Code).ThenBy(x => x.Id)
        };

        var items = (await ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken))
            .Select(x => ToResponse(x))
            .ToArray();
        return Result.Success(new PagedResponse<PlatformAccountResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<PlatformAccountResponse>> GetPlatformAccountAsync(int id, CancellationToken cancellationToken = default)
    {
        var account = await dbcontext.PlatformAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return account is null
            ? Result.Failure<PlatformAccountResponse>(OrganizationErrors.PlatformAccountNotFound)
            : Result.Success(ToResponse(account));
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

    public async Task<Result<PlatformAccountResponse>> UpdatePlatformAccountAsync(int id, UpdatePlatformAccountRequest request, CancellationToken cancellationToken = default)
    {
        var account = await dbcontext.PlatformAccounts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (account is null) return Result.Failure<PlatformAccountResponse>(OrganizationErrors.PlatformAccountNotFound);

        var code = NormalizeCode(request.Code);
        if (await dbcontext.PlatformAccounts.AnyAsync(x => x.LegalEntityId == account.LegalEntityId && x.Code == code && x.Id != id, cancellationToken))
            return Result.Failure<PlatformAccountResponse>(OrganizationErrors.DuplicateCode);

        account.Code = code;
        account.PlatformName = request.PlatformName.Trim();
        account.ExternalAccountReference = TrimToNull(request.ExternalAccountReference);
        account.IsActive = request.IsActive;
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(account));
    }

    public async Task<Result<PlatformAccountResponse>> DeletePlatformAccountAsync(int id, CancellationToken cancellationToken = default)
    {
        var account = await dbcontext.PlatformAccounts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (account is null) return Result.Failure<PlatformAccountResponse>(OrganizationErrors.PlatformAccountNotFound);

        account.IsActive = false;
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(account));
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

    private static OrganizationResponse ToResponse(Tenant tenant, IReadOnlySet<int>? accessibleLegalEntityIds = null, bool includeInactive = true) => new(
        new TenantResponse(tenant.Id, tenant.Code, tenant.Name, tenant.IsActive),
        tenant.LegalEntities
            .Where(x => includeInactive || x.IsActive)
            .Where(x => accessibleLegalEntityIds is null || accessibleLegalEntityIds.Contains(x.Id))
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .Select(x => ToResponse(x, includeInactive))
            .ToArray());

    private static LegalEntityResponse ToResponse(LegalEntity entity, bool includeInactive = true) => new(
        entity.Id,
        entity.TenantId,
        entity.Code,
        entity.LegalName,
        entity.BaseCurrencyCode,
        entity.TaxRegistrationNumber,
        entity.IsActive,
        entity.Branches.OrderBy(x => x.Code).ThenBy(x => x.Id).Select(x => new BranchResponse(x.Id, x.Code, x.Name, x.IsActive)).ToArray(),
        entity.PlatformAccounts.Where(x => includeInactive || x.IsActive).OrderBy(x => x.Code).ThenBy(x => x.Id).Select(ToResponse).ToArray());

    private static PlatformAccountResponse ToResponse(PlatformAccount account) => new(
        account.Id,
        account.Code,
        account.PlatformName,
        account.ExternalAccountReference,
        account.IsActive);

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
