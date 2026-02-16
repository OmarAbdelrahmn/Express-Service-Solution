using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SurveyBasket.Abstraction.Consts;

namespace Domain.EntitiesConfigrations;

public class UserConfigration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasData(new ApplicationUser
        {
            Id = DefaultUsers.AdminId,
            UserName = DefaultUsers.AdminName,
            NormalizedUserName = DefaultUsers.AdminName.ToUpper(),
            EmailConfirmed = true,
            PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(null!, "P@ssword1234"),
            SecurityStamp = DefaultUsers.AdminSecurityStamp,
            ConcurrencyStamp = DefaultUsers.AdminConcurrencyStamp
        });

        builder.HasData(new ApplicationUser
        {
            Id = DefaultUsers.MasterId,
            UserName = DefaultUsers.MasterName,
            NormalizedUserName = DefaultUsers.MasterName.ToUpper(),
            EmailConfirmed = true,
            PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(null!, "P@ssword1234"),
            SecurityStamp = DefaultUsers.MasterSecurityStamp,
            ConcurrencyStamp = DefaultUsers.MasterConcurrencyStamp
        });

    }
}

