
using Application.Abstraction.Consts;
using FluentValidation;

namespace Application.Contracts.Users;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(i => i.CurrentPassword)
            .NotEmpty()
            .MaximumLength(128);


        RuleFor(i => i.NewPassord)
            .NotEmpty()
            .Length(12, 128)
            .Matches(RegexPatterns.Password)
            .WithMessage("Password must contain lowercase, uppercase, number and special characters")
            .NotEqual(c => c.CurrentPassword)
            .WithMessage("New password can't be same as current one");


    }
}
