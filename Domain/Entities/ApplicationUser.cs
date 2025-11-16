using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public override string? Email { get; set; }
    public override string? NormalizedEmail { get; set; }
    public string? FullName { get; set; } = string.Empty;

    public string? Address { get; set; } = string.Empty;

    public bool IsDisable { get; set; }

    //public List<RefreshToken> RefreshTokens { get; set; } = [];

}
