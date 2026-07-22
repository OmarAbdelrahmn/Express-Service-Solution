using Application.Contracts.Common;
using Application.Contracts.Organization;
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
    public async Task LegalEntityCrud_ListsUpdatesAndRetiresWithoutErasingHistory()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" });
        await db.SaveChangesAsync();
        var service = new OrganizationSettingsService(db);

        var created = await service.CreateLegalEntityAsync(new CreateLegalEntityRequest(1, "north", "North Express", "sar", "1000000000"));

        Assert.True(created.IsSuccess);
        Assert.Equal("NORTH", created.Value.Code);
        var listed = await service.GetLegalEntitiesAsync(new PaginationRequest { PageSize = 100 }, new LegalEntityListFilter { TenantId = 1 });
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.Value.Items);

        var updated = await service.UpdateLegalEntityAsync(created.Value.Id, new UpdateLegalEntityRequest("north-ops", "North Operations", "SAR", null, true));
        Assert.True(updated.IsSuccess);
        Assert.Equal("NORTH-OPS", updated.Value.Code);
        Assert.Equal("North Operations", updated.Value.LegalName);

        var deleted = await service.DeleteLegalEntityAsync(created.Value.Id);
        Assert.True(deleted.IsSuccess);
        Assert.False(deleted.Value.IsActive);
        var inactive = await service.GetLegalEntitiesAsync(new PaginationRequest(), new LegalEntityListFilter { TenantId = 1, Active = false });
        Assert.Single(inactive.Value.Items);
    }

    [Fact]
    public async Task PlatformAccountCrud_ListsUpdatesAndRetires()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "ENTITY", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        await db.SaveChangesAsync();
        var service = new OrganizationSettingsService(db);

        var created = await service.CreatePlatformAccountAsync(new CreatePlatformAccountRequest(1, "hunger", "HungerStation", "contract-1"));

        Assert.True(created.IsSuccess);
        Assert.Equal("HUNGER", created.Value.Code);
        var listed = await service.GetPlatformAccountsAsync(new PaginationRequest { PageSize = 100 }, new PlatformAccountListFilter { LegalEntityId = 1 });
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.Value.Items);

        var updated = await service.UpdatePlatformAccountAsync(created.Value.Id, new UpdatePlatformAccountRequest("hunger-main", "HungerStation Main", null, true));
        Assert.True(updated.IsSuccess);
        Assert.Equal("HUNGER-MAIN", updated.Value.Code);

        var deleted = await service.DeletePlatformAccountAsync(created.Value.Id);
        Assert.True(deleted.IsSuccess);
        Assert.False(deleted.Value.IsActive);
        var inactive = await service.GetPlatformAccountsAsync(new PaginationRequest(), new PlatformAccountListFilter { LegalEntityId = 1, Active = false });
        Assert.Single(inactive.Value.Items);
    }

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
