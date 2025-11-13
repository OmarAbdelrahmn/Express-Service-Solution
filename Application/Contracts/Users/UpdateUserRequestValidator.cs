using FluentValidation;

namespace Application.Contracts.Users;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .WithMessage("LastName is required")
            .Length(3, 100);


        RuleFor(x => x.LastName)
            .NotEmpty()
            .WithMessage("LastName is required")
            .Length(3, 100);

        RuleFor(x => x.Roles)
            .NotEmpty()
            .NotNull();

        RuleFor(i => i.Roles)
            .Must(i => i.Distinct().Count() == i.Count)
            .WithMessage("you can't add duplicated permission for the role")
            .When(c => c.Roles != null);
    }

}
