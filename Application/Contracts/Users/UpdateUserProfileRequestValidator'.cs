using FluentValidation;

namespace Application.Contracts.Users;

public class UpdateUserProfileRequestValidator_ : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator_()
    {

        RuleFor(i => i.FirstName)
             .NotEmpty()
             .Length(3, 100);


        RuleFor(i => i.LastName)
            .NotEmpty()
            .Length(3, 100);

    }
}
