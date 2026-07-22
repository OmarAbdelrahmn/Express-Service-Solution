using Domain.Entities.AccountingCore;
using FluentValidation;

namespace Application.Contracts.FinancialAccess;

public record GrantFinancialUserAccessRequest(string UserId, int LegalEntityId, FinancialPermission Permissions);
public record FinancialUserAccessResponse(int Id, string UserId, string UserName, int LegalEntityId, FinancialPermission Permissions, string GrantedBy, DateTime GrantedAt, DateTime? UpdatedAt);

public class GrantFinancialUserAccessRequestValidator : AbstractValidator<GrantFinancialUserAccessRequest>
{
    public GrantFinancialUserAccessRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.Permissions).NotEqual(FinancialPermission.None);
        RuleFor(x => x.Permissions).Must(x => (x & ~FinancialPermission.All) == FinancialPermission.None)
            .WithMessage("Permissions contain an unsupported value.");
    }
}
