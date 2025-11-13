using Domain.Entities;

namespace Application.Authentication;

public interface IJwtProvider
{
    (string Token, int Expiry) GenerateToken(ApplicationUser user, IEnumerable<string> Roles);

    string? ValidateToken(string token);
}
