using Application.Abstraction;
using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class OrganizationErrors
{
    public static readonly Error TenantNotFound = new("Organization.TenantNotFound", "The tenant was not found.", StatusCodes.Status404NotFound);
    public static readonly Error LegalEntityNotFound = new("Organization.LegalEntityNotFound", "The legal entity was not found.", StatusCodes.Status404NotFound);
    public static readonly Error PlatformAccountNotFound = new("Organization.PlatformAccountNotFound", "The platform account was not found.", StatusCodes.Status404NotFound);
    public static readonly Error LegacyCompanyNotFound = new("Organization.LegacyCompanyNotFound", "The legacy company was not found.", StatusCodes.Status404NotFound);
    public static readonly Error DuplicateCode = new("Organization.DuplicateCode", "The code is already in use within this organization scope.", StatusCodes.Status409Conflict);
    public static readonly Error LegacyCompanyAlreadyMapped = new("Organization.LegacyCompanyAlreadyMapped", "The legacy company already has a platform-account mapping.", StatusCodes.Status409Conflict);
}
