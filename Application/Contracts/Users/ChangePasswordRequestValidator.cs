
using Application.Abstraction.Consts;
using FluentValidation;

namespace Application.Contracts.Users;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(i => i.CurrentPassword)
            .NotEmpty();


        RuleFor(i => i.NewPassord)
            .NotEmpty()
            .Matches(RegexPatterns.Password)
            .WithMessage("Password should be 8 digits and should contains Lowercase,Uppercase,Number and Special character ")
            .NotEqual(c => c.CurrentPassword)
            .WithMessage("New password can't be same as current one");


    }
}
