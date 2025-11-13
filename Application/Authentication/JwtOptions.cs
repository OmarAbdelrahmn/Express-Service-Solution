using System.ComponentModel.DataAnnotations;

namespace Application.Authentication;

public class JwtOptions
{
    [Required]
    public string Issuer { get; init; } = string.Empty;


    [Required]
    public string Audience { get; init; } = string.Empty;


    [Required]
    public string Key { get; init; } = string.Empty;


    [Required]
    [Range(1, int.MaxValue)]
    public int ExpiryIn { get; init; }
}
