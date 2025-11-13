using Application.Abstraction.Consts;
using FluentValidation;

namespace Application.Contracts.Users;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .Matches(RegexPatterns.Password)
            .WithMessage("Password should be 8 digits and should contains Lowercase,Uppercase,Number and Special character ");

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
