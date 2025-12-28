using System.Security.Claims;

namespace Application.Extensions;

public static class UserExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public static string? GetUserName(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name);
    }
    public static long GetUserIqamaNo(this ClaimsPrincipal principal)
    {
        var userName = principal.GetUserName();

        if (string.IsNullOrEmpty(userName))
            return 0;

        if (long.TryParse(userName, out var iqamaNo))
            return iqamaNo;

        return 0;
    }
}
