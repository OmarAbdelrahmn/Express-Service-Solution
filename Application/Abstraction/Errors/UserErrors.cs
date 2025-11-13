using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class UserErrors
{
    public static readonly Error InvalidCredentials = new("User.InvalidCredentials", "Invalid UserName/Password", StatusCodes.Status401Unauthorized);
    public static readonly Error EmailAlreadyExist = new("User.UserNameAlreadyExist", "User with this Name is already exists ", StatusCodes.Status409Conflict);
    public static readonly Error DuplicatedConfermation = new("User.EmailAlreadyConfirmed", "Email already confirmed", StatusCodes.Status400BadRequest);
    public static readonly Error EmailNotConfirmed = new("User.EmailNotConfirmed", "this email is not confirmed", StatusCodes.Status401Unauthorized);
    public static readonly Error UserNotFound = new("User.UserNotFound", "User not found", StatusCodes.Status401Unauthorized);
    public static readonly Error Disableuser = new("User.UserDisable", "User is disable , contact the administrator", StatusCodes.Status401Unauthorized);
    public static readonly Error userLockedout = new("User.LockedUser", "User is locked , contact the administrator", StatusCodes.Status401Unauthorized);
}
