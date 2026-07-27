using Application.Contracts.Vacation;
using Application.Service.Vacation;
using Domain;
using Domain.Entities;
using Domain.Entities.Vacation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Accounting.Tests;

public class VacationServiceTests
{
    [Fact]
    public async Task SequentialApproval_AllowsOneMultiRoleUser_ThenSchedulesVacation()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        db.VacationUserRoleAssignments.AddRange(
            new VacationUserRoleAssignment { UserId = "reviewer", Role = VacationRole.Operation, GrantedBy = "master" },
            new VacationUserRoleAssignment { UserId = "reviewer", Role = VacationRole.Accountant, GrantedBy = "master" },
            new VacationUserRoleAssignment { UserId = "administrator", Role = VacationRole.Administration, GrantedBy = "master" });
        await db.SaveChangesAsync();
        var service = new VacationService(db);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(3);

        var created = await service.CreateForMemberAsync("member", 100, new CreateVacationRequest(1, start, start.AddDays(4)));
        var operation = await service.DecideAsync("reviewer", created.Value.Id, new VacationDecisionRequest(VacationDecision.Approved, "Operations approved"));
        var accountant = await service.DecideAsync("reviewer", created.Value.Id, new VacationDecisionRequest(VacationDecision.Approved, "Accounting approved"));
        var administration = await service.DecideAsync("administrator", created.Value.Id, new VacationDecisionRequest(VacationDecision.Approved, "Administration approved"));

        Assert.True(created.IsSuccess);
        Assert.Equal(VacationRequestStatus.PendingAccountant, operation.Value.Status);
        Assert.Equal(VacationRequestStatus.PendingAdministration, accountant.Value.Status);
        Assert.Equal(VacationRequestStatus.Approved, administration.Value.Status);
        Assert.Equal(3, administration.Value.Decisions.Count);
    }

    [Fact]
    public async Task PendingCancellation_PausesWorkflow_AndMasterApprovalCancelsRequest()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        db.VacationUserRoleAssignments.Add(new VacationUserRoleAssignment { UserId = "reviewer", Role = VacationRole.Operation, GrantedBy = "master" });
        await db.SaveChangesAsync();
        var service = new VacationService(db);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(2);
        var created = await service.CreateForMemberAsync("member", 100, new CreateVacationRequest(1, start, start.AddDays(2)));

        var cancellation = await service.RequestCancellationAsync("member", 100, created.Value.Id, new CreateVacationCancellationRequest("Rider travel changed"));
        var blocked = await service.DecideAsync("reviewer", created.Value.Id, new VacationDecisionRequest(VacationDecision.Approved, "Approved"));
        var resolved = await service.ResolveCancellationAsync("master", cancellation.Value.Id, new ResolveVacationAmendmentRequest(VacationDecision.Approved, "Cancellation accepted"));

        Assert.True(cancellation.IsSuccess);
        Assert.True(blocked.IsFailure);
        Assert.Equal("Vacation.WorkflowPaused", blocked.Error.Code);
        Assert.Equal(VacationAmendmentStatus.Approved, resolved.Value.Status);
        Assert.Equal(VacationRequestStatus.Cancelled, (await db.VacationRequests.SingleAsync()).Status);
    }

    private static ApplicationDbcontext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static async Task SeedAsync(ApplicationDbcontext db)
    {
        db.ApplicationUsers.AddRange(
            new ApplicationUser { Id = "member", UserName = "100", FullName = "Housing Member" },
            new ApplicationUser { Id = "reviewer", UserName = "reviewer", FullName = "Reviewer" },
            new ApplicationUser { Id = "administrator", UserName = "administrator", FullName = "Administrator" },
            new ApplicationUser { Id = "master", UserName = "master", FullName = "Master" });
        db.Housings.Add(new Housing { Id = 1, Name = "Housing", Address = "Riyadh", Capacity = 10, ManagerIqamaNo = 100 });
        db.Companies.Add(new Company { Id = 1, Name = "Company" });
        db.Employees.Add(new Employees
        {
            IqamaNo = 200,
            IqamaEndM = new DateOnly(2030, 1, 1),
            IqamaEndH = new DateOnly(2030, 1, 1),
            Sponsor = "Sponsor",
            JobTitle = "Rider",
            NameAR = "Rider AR",
            NameEN = "Rider EN",
            Country = "SA",
            Phone = "0500000000",
            DateOfBirth = new DateOnly(2000, 1, 1),
            HousingId = 1,
            Status = "enable"
        });
        db.RiderDetails.Add(new RiderDetails { Id = 1, EmployeeIqamaNo = 200, CompanyId = 1, WorkingId = "R-1" });
        await db.SaveChangesAsync();
    }
}
