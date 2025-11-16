using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class RolesErrors
{
    public static readonly Error InvalidPermissions = new("Invalid Permission", "Invalid Role permission", StatusCodes.Status400BadRequest);
    public static readonly Error InvalidRoles = new("Invalid Role", "Invalid user Role", StatusCodes.Status400BadRequest);
    public static readonly Error NotFound = new("Invalid credentials", "NotFound", StatusCodes.Status404NotFound);
    public static readonly Error DaplicatedRole = new("Invalid credentials", "Role With this name is already exists", StatusCodes.Status409Conflict);
    public static readonly Error somethingwrong = new("some thing wrong", "This user doesn't have roles at all call the admin", StatusCodes.Status400BadRequest);
    public static readonly Error haveit = new("he have it already", "this user have this role already", StatusCodes.Status409Conflict);

}
