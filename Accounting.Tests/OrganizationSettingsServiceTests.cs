using Application.Service.Organization;
using Domain;
using Domain.Entities;
using Domain.Entities.AccountingCore;
using Domain.Entities.Organization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class OrganizationSettingsServiceTests
{
    [Fact]
    public async Task GetCurrent_ReturnsOrganizationForAccountantWithoutContextDto()
    {
        await using var db = CreateDbContext();
        var tenant = new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" };
        tenant.LegalEntities.Add(new LegalEntity
        {
            Id = 1,
            Code = "ENTITY",
            LegalName = "Legal entity",
            BaseCurrencyCode = "SAR",
            Branches = [new Branch { Id = 1, Code = "HQ", Name = "Head office" }],
            PlatformAccounts = [new PlatformAccount { Id = 1, Code = "KEETA", PlatformName = "Keeta" }]
        });
        db.Tenants.Add(tenant);
        db.ApplicationUsers.Add(new ApplicationUser { Id = "accountant", UserName = "accountant", NormalizedUserName = "ACCOUNTANT" });
        db.ApplicationRoles.Add(new ApplicationRole { Id = "accountant-role", Name = "Accountant", NormalizedName = "ACCOUNTANT" });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "accountant", RoleId = "accountant-role" });
        await db.SaveChangesAsync();

        var result = await new OrganizationSettingsService(db).GetCurrentAsync("accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal("TENANT", result.Value.Tenant.Code);
        var entity = Assert.Single(result.Value.LegalEntities);
        Assert.Single(entity.Branches);
        Assert.Single(entity.PlatformAccounts);
    }

    [Fact]
    public async Task GetCurrent_FiltersNonAccountingActorToGrantedLegalEntities()
    {
        await using var db = CreateDbContext();
        var tenant = new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" };
        tenant.LegalEntities.Add(new LegalEntity { Id = 1, Code = "ONE", LegalName = "One", BaseCurrencyCode = "SAR" });
        tenant.LegalEntities.Add(new LegalEntity { Id = 2, Code = "TWO", LegalName = "Two", BaseCurrencyCode = "SAR" });
        db.Tenants.Add(tenant);
        db.ApplicationUsers.Add(new ApplicationUser { Id = "viewer", UserName = "viewer", NormalizedUserName = "VIEWER" });
        db.FinancialUserAccesses.AddRange(
            new FinancialUserAccess
            {
                UserId = "viewer",
                LegalEntityId = 1,
                Permissions = FinancialPermission.None,
                GrantedBy = "master"
            },
            new FinancialUserAccess
            {
                UserId = "viewer",
                LegalEntityId = 2,
                Permissions = FinancialPermission.View,
                GrantedBy = "master"
            });
        await db.SaveChangesAsync();

        var accessibleIds = await db.FinancialUserAccesses
            .Where(x => x.UserId == "viewer" && (x.Permissions & FinancialPermission.View) == FinancialPermission.View)
            .Select(x => x.LegalEntityId)
            .ToArrayAsync();
        Assert.Equal(new[] { 2 }, accessibleIds);

        var result = await new OrganizationSettingsService(db).GetCurrentAsync("viewer");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, Assert.Single(result.Value.LegalEntities).Id);
    }

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
