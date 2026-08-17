using Domain;
using Microsoft.EntityFrameworkCore;

namespace Application.Authentication;

public interface IJwtAccountValidator
{
    Task<bool> IsCurrentAsync(
        string userId,
        string securityStamp,
        CancellationToken cancellationToken = default);
}

public sealed class JwtAccountValidator(ApplicationDbcontext dbcontext) : IJwtAccountValidator
{
    public Task<bool> IsCurrentAsync(
        string userId,
        string securityStamp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(securityStamp))
            return Task.FromResult(false);

        return dbcontext.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId &&
                        !user.IsDisable &&
                        user.SecurityStamp == securityStamp,
                cancellationToken);
    }
}
