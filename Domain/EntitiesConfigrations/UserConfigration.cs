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
            PasswordHash = "AQAAAAIAAYagAAAAEA/zZpuqFzbTSnicQa4Tooll0FGxeDLCE2M5TALeSVR6BGE45Era3fs5IhF5zU2ZyQ==",
            SecurityStamp = DefaultUsers.AdminSecurityStamp,
            ConcurrencyStamp = DefaultUsers.AdminConcurrencyStamp
        });

        builder.HasData(new ApplicationUser
        {
            Id = DefaultUsers.MasterId,
            UserName = DefaultUsers.MasterName,
            NormalizedUserName = DefaultUsers.MasterName.ToUpper(),
            EmailConfirmed = true,
            PasswordHash = "AQAAAAIAAYagAAAAEFpg1iN3qC51jcJrS5Ea9/Ab1Xi7kXnwjCrMOynu6YUpw7q1mrTe8yz+5Cx2W01t5A==",
            SecurityStamp = DefaultUsers.MasterSecurityStamp,
            ConcurrencyStamp = DefaultUsers.MasterConcurrencyStamp
        });

    }
}

