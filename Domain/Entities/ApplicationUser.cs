using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public override string? Email { get; set; }
    public override string? NormalizedEmail { get; set; }
    public string? FullName { get; set; } = string.Empty;

    public string? Address { get; set; } = string.Empty;

    public DateTime? LastLogin { get; set; }

    public bool IsDisable { get; set; }

    //public List<RefreshToken> RefreshTokens { get; set; } = [];

}
