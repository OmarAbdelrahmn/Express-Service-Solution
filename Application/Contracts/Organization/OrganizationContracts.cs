using FluentValidation;

namespace Application.Contracts.Organization;

public record CreateTenantRequest(string Code, string Name);
public record CreateLegalEntityRequest(int TenantId, string Code, string LegalName, string BaseCurrencyCode, string? TaxRegistrationNumber);
public record CreateBranchRequest(int LegalEntityId, string Code, string Name);
public record CreatePlatformAccountRequest(int LegalEntityId, string Code, string PlatformName, string? ExternalAccountReference);
public record CreateLegacyCompanyPlatformMappingRequest(int CompanyId, int PlatformAccountId, DateTime EffectiveFrom);

public record TenantResponse(int Id, string Code, string Name, bool IsActive);
public record BranchResponse(int Id, string Code, string Name, bool IsActive);
public record PlatformAccountResponse(int Id, string Code, string PlatformName, string? ExternalAccountReference, bool IsActive);
public record LegalEntityResponse(int Id, int TenantId, string Code, string LegalName, string BaseCurrencyCode, string? TaxRegistrationNumber, bool IsActive, IReadOnlyCollection<BranchResponse> Branches, IReadOnlyCollection<PlatformAccountResponse> PlatformAccounts);
public record OrganizationResponse(TenantResponse Tenant, IReadOnlyCollection<LegalEntityResponse> LegalEntities);
public record LegacyCompanyPlatformMappingResponse(int Id, int CompanyId, int PlatformAccountId, DateTime EffectiveFrom);

public class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateLegalEntityRequestValidator : AbstractValidator<CreateLegalEntityRequest>
{
    public CreateLegalEntityRequestValidator()
    {
        RuleFor(x => x.TenantId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BaseCurrencyCode).Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.TaxRegistrationNumber).MaximumLength(64).When(x => x.TaxRegistrationNumber is not null);
    }
}

public class CreateBranchRequestValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchRequestValidator()
    {
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreatePlatformAccountRequestValidator : AbstractValidator<CreatePlatformAccountRequest>
{
    public CreatePlatformAccountRequestValidator()
    {
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.PlatformName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalAccountReference).MaximumLength(128).When(x => x.ExternalAccountReference is not null);
    }
}

public class CreateLegacyCompanyPlatformMappingRequestValidator : AbstractValidator<CreateLegacyCompanyPlatformMappingRequest>
{
    public CreateLegacyCompanyPlatformMappingRequestValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.PlatformAccountId).GreaterThan(0);
    }
}
