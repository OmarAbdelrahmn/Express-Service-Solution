using FluentValidation;

namespace Application.Contracts.Users;

public class UpdateUserProfileRequestValidator_ : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator_()
    {

        RuleFor(i => i.FullName)
             .NotEmpty()
             .Length(3, 100);


        RuleFor(i => i.Address)
            .NotEmpty()
            .Length(3, 100);

    }
}
