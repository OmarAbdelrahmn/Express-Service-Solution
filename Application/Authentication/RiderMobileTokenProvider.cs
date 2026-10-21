using Application.Service.RiderMobile;
using Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Application.Authentication;

public interface IRiderMobileTokenProvider
{
    string Generate(RiderMobileSession session);
}

public class RiderMobileTokenProvider(IOptions<JwtOptions> options) : IRiderMobileTokenProvider
{
    public const string Scheme = "RiderMobile";
    public const string RiderIdClaim = "rider_id";
    public const string SessionClaim = "sid";
    public const string LanguageClaim = "lang";
    public const string PurposeClaim = "purpose";

    // Domain-separated signing material prevents rider tokens authenticating as staff tokens.
    private static SymmetricSecurityKey Key(JwtOptions jwt) => new(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Key), Encoding.UTF8.GetBytes("express-service:rider-mobile:v1")));

    public static TokenValidationParameters ValidationParameters(JwtOptions jwt) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience + ":rider-mobile",
        IssuerSigningKey = Key(jwt),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.Zero,
        NameClaimType = JwtRegisteredClaimNames.Sub
    };

    public string Generate(RiderMobileSession session)
    {
        var jwt = options.Value;
        var riderId = session.RiderId.ToString(CultureInfo.InvariantCulture);
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience + ":rider-mobile",
            [new Claim(JwtRegisteredClaimNames.Sub, riderId),
             new Claim(RiderIdClaim, riderId), new Claim(SessionClaim, session.Id.ToString()),
             new Claim(LanguageClaim, session.Language), new Claim(PurposeClaim, Scheme),
             new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            notBefore: session.CreatedAtUtc, expires: session.ExpiresAtUtc,
            signingCredentials: new SigningCredentials(Key(jwt), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
