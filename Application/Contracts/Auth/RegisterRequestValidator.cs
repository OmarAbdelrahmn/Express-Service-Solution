using Application.Abstraction.Consts;
using FluentValidation;

namespace Application.Contracts.Auth;



public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{

    public RegisterRequestValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("UserName is required")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required")
            .Length(12, 128)
            .WithMessage("Password must be between 12 and 128 characters")
            .Matches(RegexPatterns.Password)
            .WithMessage("Password should contains Lowercase,Uppercase,Number and Special character ");

    }
}

