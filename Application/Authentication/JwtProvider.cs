using Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Application.Authentication;

public class JwtProvider(IOptions<JwtOptions> options) : IJwtProvider
{
    public const string SecurityStampClaimType = "security_stamp";

    private readonly JwtOptions options = options.Value;

    public (string Token, int Expiry) GenerateToken(ApplicationUser user, IEnumerable<string> roles)
    {
        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
            throw new InvalidOperationException("A security stamp is required before issuing a token.");

        Claim[] claims = [
            new (System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.Id),
            new (System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new (System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new (SecurityStampClaimType, user.SecurityStamp),
            new (nameof(roles),JsonSerializer.Serialize(roles),System.IdentityModel.Tokens.Jwt.JsonClaimValueTypes.JsonArray)
            ];

        var SymmetricSecuritykey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));

        var signingCredentials = new SigningCredentials(SymmetricSecuritykey, SecurityAlgorithms.HmacSha256);


        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.ExpiryIn),
            signingCredentials: signingCredentials
        );

        return (Token: new JwtSecurityTokenHandler().WriteToken(token), Expiry: options.ExpiryIn);
    }

    public string? ValidateToken(string token)
    {
        var tokenhandler = new JwtSecurityTokenHandler();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));

        try
        {
            tokenhandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKey = key
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;

            return jwtToken.Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value;
        }
        catch
        {
            return null;

        }
    }
}
