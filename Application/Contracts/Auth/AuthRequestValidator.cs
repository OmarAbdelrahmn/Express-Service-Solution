using FluentValidation;

namespace Application.Contracts.Auth;

public class AuthRequestValidator : AbstractValidator<AuthRequest>
{

    public AuthRequestValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("UserName is required")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required")
            .MaximumLength(128);

    }
}
