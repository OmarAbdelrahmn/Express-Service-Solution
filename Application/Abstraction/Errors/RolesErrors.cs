using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class RolesErrors
{
    public static readonly Error InvalidPermissions = new("Invalid Permission", "Invalid Role permission", StatusCodes.Status400BadRequest);
    public static readonly Error InvalidRoles = new("Invalid Role", "Invalid user Role", StatusCodes.Status400BadRequest);
    public static readonly Error NotFound = new("Invalid credentials", "NotFound", StatusCodes.Status404NotFound);
    public static readonly Error DaplicatedRole = new("Invalid credentials", "Daplicated Role", StatusCodes.Status409Conflict);

}
