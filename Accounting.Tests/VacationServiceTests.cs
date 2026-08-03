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
    public async Task CreateVacation_PersistsMemberNotes_AndReturnsThemInVacationResponses()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        var service = new VacationService(db);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(2);

        var created = await service.CreateForMemberAsync(
            "member", 100, new CreateVacationRequest(1, start, start.AddDays(3), "  Rider requested an early flight.  "));
        var memberRequests = await service.GetMemberRequestsAsync(100);

        Assert.True(created.IsSuccess);
        Assert.Equal("Rider requested an early flight.", created.Value.MemberNotes);
        Assert.Equal(200, created.Value.Rider.IqamaNo);
        Assert.Equal("P123456", created.Value.Rider.PassportNo);
        Assert.Equal(new DateOnly(2031, 1, 1), created.Value.Rider.PassportEnd);
        Assert.Equal(new DateOnly(2030, 1, 1), created.Value.Rider.IqamaEndM);
        Assert.Equal(created.Value.MemberNotes, memberRequests.Value.Single().MemberNotes);
        Assert.Equal(created.Value.MemberNotes, (await db.VacationRequests.SingleAsync()).MemberNotes);
    }

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
        Assert.Equal(VacationHrStatus.AwaitingTicket, administration.Value.Hr.Status);
        Assert.Equal(3, administration.Value.Decisions.Count);
    }

    [Fact]
    public async Task HrCompletesTicketThenVisa_AndMemberResponseContainsProtectedDocumentLinks()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        await AssignWorkflowRolesAsync(db);
        var storage = new MemoryVacationDocumentStorage();
        var service = new VacationService(db, storage);
        var vacation = await CreateAndFullyApproveAsync(service);

        var visaBeforeTicket = await service.UploadHrDocumentAsync(
            "hr", vacation.Id, VacationHrDocumentType.ExitReentryVisa, true,
            "visa.pdf", "application/pdf", 1, new MemoryStream([0]));
        var ticket = await service.UploadHrDocumentAsync(
            "hr", vacation.Id, VacationHrDocumentType.Ticket, true,
            "ticket.pdf", "application/pdf", 4, new MemoryStream([1, 2, 3, 4]));
        var visa = await service.UploadHrDocumentAsync(
            "hr", vacation.Id, VacationHrDocumentType.ExitReentryVisa, true,
            "visa.pdf", "application/pdf", 3, new MemoryStream([5, 6, 7]));
        var memberRequests = await service.GetMemberRequestsAsync(100);
        var memberFile = await service.OpenHrDocumentAsync(
            "member", 100, false, vacation.Id, ticket.Value.Document.Id);

        Assert.True(visaBeforeTicket.IsFailure);
        Assert.Equal("Vacation.TicketRequired", visaBeforeTicket.Error.Code);
        Assert.True(ticket.IsSuccess);
        Assert.Equal(VacationHrStatus.AwaitingExitReentryVisa, ticket.Value.Vacation.Hr.Status);
        Assert.True(visa.IsSuccess);
        Assert.Equal(VacationHrStatus.Completed, visa.Value.Vacation.Hr.Status);
        Assert.True(visa.Value.Vacation.Hr.TicketCompleted);
        Assert.True(visa.Value.Vacation.Hr.ExitReentryVisaCompleted);
        Assert.Equal(2, memberRequests.Value.Single().Hr.Documents.Count);
        Assert.True(memberFile.IsSuccess);
        Assert.Equal(4, memberFile.Value.Length);
        await memberFile.Value.Content.DisposeAsync();
        Assert.All(memberRequests.Value.Single().Hr.Documents, x =>
        {
            Assert.StartsWith("/api/vacation-requests/", x.StreamUrl);
            Assert.EndsWith("/download", x.DownloadUrl);
        });
    }

    [Fact]
    public async Task ExtendingReturnDate_SupersedesCurrentVisa_AndRequiresNewVisa()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        await AssignWorkflowRolesAsync(db);
        var service = new VacationService(db, new MemoryVacationDocumentStorage());
        var vacation = await CreateAndFullyApproveAsync(service);
        await service.UploadHrDocumentAsync("hr", vacation.Id, VacationHrDocumentType.Ticket, true, "ticket.pdf", "application/pdf", 1, new MemoryStream([1]));
        await service.UploadHrDocumentAsync("hr", vacation.Id, VacationHrDocumentType.ExitReentryVisa, true, "visa.pdf", "application/pdf", 1, new MemoryStream([2]));

        var change = await service.RequestDateChangeAsync(
            "member", 100, vacation.Id,
            new CreateVacationDateChangeRequest(vacation.StartDate, vacation.EndDate.AddDays(5), "Return flight moved"));
        var pausedHrUpload = await service.UploadHrDocumentAsync(
            "hr", vacation.Id, VacationHrDocumentType.ExitReentryVisa, true,
            "new-visa.pdf", "application/pdf", 1, new MemoryStream([3]));
        var resolved = await service.ResolveDateChangeAsync(
            "master", change.Value.Id,
            new ResolveVacationAmendmentRequest(VacationDecision.Approved, "Extension approved"));
        var updated = await service.GetMemberRequestsAsync(100);

        Assert.True(pausedHrUpload.IsFailure);
        Assert.Equal("Vacation.WorkflowPaused", pausedHrUpload.Error.Code);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(VacationHrStatus.AwaitingExitReentryVisa, updated.Value.Single().Hr.Status);
        Assert.True(updated.Value.Single().Hr.TicketCompleted);
        Assert.False(updated.Value.Single().Hr.ExitReentryVisaCompleted);
        Assert.True(updated.Value.Single().Hr.Documents.Single(x => x.Type == VacationHrDocumentType.ExitReentryVisa).IsSuperseded);
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
            new ApplicationUser { Id = "hr", UserName = "hr", FullName = "HR User" },
            new ApplicationUser { Id = "master", UserName = "master", FullName = "Master" });
        db.Housings.Add(new Housing { Id = 1, Name = "Housing", Address = "Riyadh", Capacity = 10, ManagerIqamaNo = 100 });
        db.Companies.Add(new Company { Id = 1, Name = "Company" });
        db.Employees.Add(new Employees
        {
            IqamaNo = 200,
            IqamaEndM = new DateOnly(2030, 1, 1),
            IqamaEndH = new DateOnly(2030, 1, 1),
            PassportNo = "P123456",
            PassportEnd = new DateOnly(2031, 1, 1),
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

    private static async Task AssignWorkflowRolesAsync(ApplicationDbcontext db)
    {
        db.VacationUserRoleAssignments.AddRange(
            new VacationUserRoleAssignment { UserId = "reviewer", Role = VacationRole.Operation, GrantedBy = "master" },
            new VacationUserRoleAssignment { UserId = "reviewer", Role = VacationRole.Accountant, GrantedBy = "master" },
            new VacationUserRoleAssignment { UserId = "administrator", Role = VacationRole.Administration, GrantedBy = "master" },
            new VacationUserRoleAssignment { UserId = "hr", Role = VacationRole.HR, GrantedBy = "master" });
        await db.SaveChangesAsync();
    }

    private static async Task<VacationRequestResponse> CreateAndFullyApproveAsync(VacationService service)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(3);
        var created = await service.CreateForMemberAsync("member", 100, new CreateVacationRequest(1, start, start.AddDays(4)));
        await service.DecideAsync("reviewer", created.Value.Id, new VacationDecisionRequest(VacationDecision.Approved, "Operations approved"));
        await service.DecideAsync("reviewer", created.Value.Id, new VacationDecisionRequest(VacationDecision.Approved, "Accounting approved"));
        return (await service.DecideAsync("administrator", created.Value.Id, new VacationDecisionRequest(VacationDecision.Approved, "Administration approved"))).Value;
    }

    private sealed class MemoryVacationDocumentStorage : IVacationDocumentStorage
    {
        private readonly Dictionary<string, byte[]> files = [];

        public async Task<StoredVacationDocument> SaveAsync(Guid vacationRequestId, Guid documentId, string category, string fileName, Stream content, CancellationToken cancellationToken = default)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var path = $"{vacationRequestId:N}/{category}/{documentId:N}.pdf";
            files[path] = buffer.ToArray();
            return new StoredVacationDocument(path, "application/pdf", buffer.Length);
        }

        public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(files.TryGetValue(relativePath, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            files.Remove(relativePath);
            return Task.CompletedTask;
        }
    }
}
