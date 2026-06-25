using Application.Abstraction.Consts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class RolesConfigration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {

        builder.HasData(
            [
                new ApplicationRole
                {
                    Id = DefaultRoles.AdminRoleId,
                    Name = DefaultRoles.Admin,
                    ConcurrencyStamp = DefaultRoles.AdminRoleConcurrencyStamp,
                    NormalizedName = DefaultRoles.Admin.ToUpper(),
                    IsDefault = false,
                    IsDeleted = false
                },
                new ApplicationRole
                {
                    Id = DefaultRoles.MemberRoleId,
                    Name = DefaultRoles.Member,
                    ConcurrencyStamp = DefaultRoles.MemberRoleConcurrencyStamp,
                    NormalizedName = DefaultRoles.Member.ToUpper(),
                    IsDefault = true,
                    IsDeleted = false
                },
                new ApplicationRole
                {
                    Id = DefaultRoles.AccountantRoleId,
                    Name = DefaultRoles.Accountant,
                    ConcurrencyStamp = DefaultRoles.AccountantRoleConcurrencyStamp,
                    NormalizedName = DefaultRoles.Accountant.ToUpper(),
                    IsDefault = false,
                    IsDeleted = false
                },
                 new ApplicationRole
                {
                    Id = DefaultRoles.MasterRoleId,
                    Name = DefaultRoles.Master,
                    ConcurrencyStamp = DefaultRoles.MasterRoleConcurrencyStamp,
                    NormalizedName = DefaultRoles.Master.ToUpper(),
                    IsDefault = false,
                    IsDeleted = false
                }
            ]
        );

    }
}

