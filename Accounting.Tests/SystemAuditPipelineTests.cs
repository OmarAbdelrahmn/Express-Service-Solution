using Domain;
using Domain.Auditing;
using Domain.Entities;
using Domain.Entities.AccountingCore;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class SystemAuditPipelineTests
{
    [Fact]
    public async Task TrackedCreateUpdateAndDelete_WriteAtomicPerEntityAuditEvents()
    {
        await using var db = CreateDbContext();

        var part = new SparePart { Name = "Helmet", Quantity = 4, Price = 20m, Location = "Riyadh" };
        db.SpareParts.Add(part);
        await db.SaveChangesAsync();

        var created = await db.SystemAuditEvents.SingleAsync();
        Assert.Equal(SystemAuditAction.Create, created.Action);
        Assert.Equal("user-1", created.ActorUserId);
        Assert.Equal("Id=" + part.Id, created.EntityKey);
        Assert.Contains("Quantity", created.NewValuesJson);

        part.Quantity = 6;
        await db.SaveChangesAsync();

        var updated = await db.SystemAuditEvents.OrderBy(x => x.Id).LastAsync();
        Assert.Equal(SystemAuditAction.Update, updated.Action);
        Assert.Contains("Quantity", updated.ChangedFieldsJson);
        Assert.Contains("4", updated.OldValuesJson);
        Assert.Contains("6", updated.NewValuesJson);
        Assert.Equal("Location", updated.ScopeType);
        Assert.Equal("Riyadh", updated.ScopeBefore);
        Assert.Equal("Riyadh", updated.ScopeAfter);

        await db.SaveChangesAsync();
        Assert.Equal(2, await db.SystemAuditEvents.CountAsync());

        db.SpareParts.Remove(part);
        await db.SaveChangesAsync();

        var deleted = await db.SystemAuditEvents.OrderBy(x => x.Id).LastAsync();
        Assert.Equal(SystemAuditAction.Delete, deleted.Action);
        Assert.Contains("Helmet", deleted.OldValuesJson);
        Assert.Null(deleted.NewValuesJson);
    }

    [Fact]
    public async Task SensitiveValuesAreRedacted_AndAccountingModelsAreExcluded()
    {
        await using var db = CreateDbContext();

        db.ApplicationUsers.Add(new ApplicationUser
        {
            Id = "user-2",
            UserName = "audited-user",
            NormalizedUserName = "AUDITED-USER",
            PasswordHash = "must-not-be-stored"
        });
        await db.SaveChangesAsync();

        var userAudit = await db.SystemAuditEvents.SingleAsync();
        Assert.DoesNotContain("must-not-be-stored", userAudit.NewValuesJson);
        Assert.Contains("[REDACTED]", userAudit.NewValuesJson);

        db.AccountingAccounts.Add(new AccountingAccount { LegalEntityId = 1, Code = "1000", Name = "Cash" });
        db.TempEmployeeUpdates.Add(new TempEmployeeUpdate { IqamaNo = 1001, UploadedBy = "user-1" });
        await db.SaveChangesAsync();

        Assert.Single(await db.SystemAuditEvents.ToListAsync());
    }

    private static ApplicationDbcontext CreateDbContext()
    {
        var accessor = new AuditContextAccessor();
        accessor.Set(new AuditContext(
            Guid.Parse("81ec7b3a-25ed-4e30-a824-1579f46bea1c"),
            AuditActorType.User,
            "user-1",
            "Audit User",
            "Test",
            "SystemAuditPipelineTests"));

        return new ApplicationDbcontext(
            new DbContextOptionsBuilder<ApplicationDbcontext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            accessor);
    }
}
