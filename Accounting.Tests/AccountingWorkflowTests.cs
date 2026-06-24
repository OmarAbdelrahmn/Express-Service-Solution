using Application.Service.Accounting;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Accounting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Accounting.Tests;

public class AccountingWorkflowTests
{
    [Fact]
    public async Task ImportCompanyBill_DoesNotPostUntilApproval()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        var service = new AccountingImportService(db);

        var file = CreateCompanyBillFile(rider.WorkingId!, 10, 100);
        var result = await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(file, 2026, 6, rider.CompanyId, CompanyBillTemplateType.Generic, null),
            "accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountingRecordStatus.PendingReview, result.Value.Status);
        Assert.Equal(0, await db.CompanyReceivables.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync());

        var approve = await service.ApproveCompanyBillImportAsync(result.Value.Id, "accountant");

        Assert.True(approve.IsSuccess);
        Assert.Equal(AccountingRecordStatus.Posted, approve.Value.Status);
        Assert.Equal(1, await db.CompanyReceivables.CountAsync());
        var journal = await db.JournalEntries.SingleAsync(j => j.SourceType == "CompanyBillImport");
        Assert.Equal(result.Value.Id, journal.SourceId);
    }

    [Fact]
    public async Task ApproveCompanyBillImport_WithUnresolvedRows_IsRejected()
    {
        await using var db = CreateDb();
        var service = new AccountingImportService(db);

        var import = new CompanyBillImport
        {
            Year = 2026,
            Month = 6,
            SourceFileName = "bad.xlsx",
            UploadedBy = "accountant",
            Status = AccountingRecordStatus.PendingReview,
            RiderSummaries =
            {
                new CompanyBillRiderSummary
                {
                    SourceRiderId = "missing",
                    ResolutionStatus = ImportResolutionStatus.Unresolved,
                    BasicPayment = 100,
                    NetAmount = 100
                }
            }
        };

        db.CompanyBillImports.Add(import);
        await db.SaveChangesAsync();

        var result = await service.ApproveCompanyBillImportAsync(import.Id, "accountant");

        Assert.True(result.IsFailure);
        Assert.Equal(0, await db.CompanyReceivables.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task SalaryApproval_PostsOnce_WithRealSalaryId()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        db.RiderEarnings.Add(new RiderEarning
        {
            PaidRiderId = rider.Id,
            CompanyId = rider.CompanyId,
            Year = 2026,
            Month = 6,
            AcceptedOrders = 20,
            GrossAmount = 200,
            SalaryAmount = 200,
            SourceType = "test",
            Status = AccountingRecordStatus.Approved
        });
        await db.SaveChangesAsync();

        var service = new AccountingSalaryService(db);
        var generated = await service.GenerateMonthlySalariesAsync(new GenerateSalaryRequest(2026, 6, rider.CompanyId), "accountant");

        Assert.True(generated.IsSuccess);
        var salary = generated.Value.Single();
        Assert.Equal(SalaryStatus.Draft, salary.Status);
        Assert.Equal(0, await db.JournalEntries.CountAsync(j => j.SourceType == "RiderMonthlySalary"));

        var approved = await service.ApproveSalaryAsync(salary.Id, "accountant");
        var approvedAgain = await service.ApproveSalaryAsync(salary.Id, "accountant");

        Assert.True(approved.IsSuccess);
        Assert.True(approvedAgain.IsSuccess);
        var journal = await db.JournalEntries.SingleAsync(j => j.SourceType == "RiderMonthlySalary");
        Assert.Equal(salary.Id, journal.SourceId);
    }

    [Fact]
    public async Task BankSend_DoesNotPayUntilConfirmation()
    {
        await using var db = CreateDb();
        var salary = await SeedApprovedSalaryAsync(db, iban: "SA123");
        var service = new AccountingPaymentService(db);

        var batch = await service.CreateBankPaymentBatchAsync(new CreatePaymentBatchRequest(2026, 6, null, null), "accountant");
        var sent = await service.MarkBankPaymentBatchSentAsync(batch.Value.Id, "accountant");

        Assert.True(sent.IsSuccess);
        Assert.Equal(PaymentBatchStatus.Sent, sent.Value.Status);
        Assert.Equal(0, (await db.RiderMonthlySalaries.FindAsync(salary.Id))!.PaidAmount);

        var payment = sent.Value.Payments.Single();
        var confirmed = await service.ConfirmBankPaymentBatchAsync(
            sent.Value.Id,
            new BankPaymentConfirmationRequest(
                [new BankPaymentConfirmationLine(payment.Id, "BANK-1", null)],
                [],
                null),
            "accountant");

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(PaymentBatchStatus.Confirmed, confirmed.Value.Status);
        Assert.Equal(salary.NetSalary, (await db.RiderMonthlySalaries.FindAsync(salary.Id))!.PaidAmount);
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.SourceType == "RiderSalaryPayment"));
    }

    [Fact]
    public async Task CashSubmission_IsIdempotent_AndRequiresManagerHousing()
    {
        await using var db = CreateDb();
        var salary = await SeedApprovedSalaryAsync(db, iban: null, managerIqama: 777);
        var service = new AccountingPaymentService(db);

        var housingId = salary.Rider.Employee.HousingId;
        var batch = await service.CreateCashHandoverBatchAsync(new CreateCashHandoverBatchRequest(2026, 6, housingId, null, null), "accountant");
        var line = batch.Value.Lines.Single();

        var denied = await service.SubmitCashHandoverLineAsync(
            line.Id,
            new CashSalarySubmissionRequest(CashHandoverLineStatus.Delivered, null, null),
            999,
            "member");
        Assert.True(denied.IsFailure);

        var delivered = await service.SubmitCashHandoverLineAsync(
            line.Id,
            new CashSalarySubmissionRequest(CashHandoverLineStatus.Delivered, null, null),
            777,
            "member");
        var deliveredAgain = await service.SubmitCashHandoverLineAsync(
            line.Id,
            new CashSalarySubmissionRequest(CashHandoverLineStatus.Delivered, null, null),
            777,
            "member");

        Assert.True(delivered.IsSuccess);
        Assert.True(deliveredAgain.IsSuccess);
        Assert.Equal(salary.NetSalary, (await db.RiderMonthlySalaries.FindAsync(salary.Id))!.PaidAmount);
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.SourceType == "CashSalaryHandoverLine"));
    }

    [Fact]
    public async Task Receipt_RejectsOverpaymentAndDuplicateReference()
    {
        await using var db = CreateDb();
        var company = new Company { Name = "Client" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.CompanyReceivables.AddRange(
            new CompanyReceivable { CompanyId = company.Id, Year = 2026, Month = 6, NetAmount = 100, PendingAmount = 100, Status = AccountingRecordStatus.Posted },
            new CompanyReceivable { CompanyId = company.Id, Year = 2026, Month = 6, NetAmount = 50, PendingAmount = 50, Status = AccountingRecordStatus.Posted });
        await db.SaveChangesAsync();
        var receivables = await db.CompanyReceivables.OrderBy(r => r.Id).ToListAsync();
        var service = new CompanyFinanceService(db);

        var receipt = await service.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(receivables[0].Id, company.Id, new DateOnly(2026, 6, 20), 100, "REF-1", "Bank", null),
            "accountant");
        var duplicate = await service.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(null, company.Id, new DateOnly(2026, 6, 20), 10, "REF-1", "Bank", null),
            "accountant");
        var overpayment = await service.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(receivables[1].Id, company.Id, new DateOnly(2026, 6, 21), 60, "REF-2", "Bank", null),
            "accountant");

        Assert.True(receipt.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.True(overpayment.IsFailure);
    }

    [Fact]
    public async Task ClosedPeriod_RejectsReceiptPosting()
    {
        await using var db = CreateDb();
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026,
            Month = 6,
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = AccountingPeriodStatus.Closed
        });
        await db.SaveChangesAsync();
        var service = new CompanyFinanceService(db);

        var result = await service.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(null, null, new DateOnly(2026, 6, 10), 10, "REF", "Bank", null),
            "accountant");

        Assert.True(result.IsFailure);
    }

    private static ApplicationDbcontext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = (ApplicationDbcontext)Activator.CreateInstance(typeof(ApplicationDbcontext), options)!;
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<RiderDetails> SeedRiderAsync(ApplicationDbcontext db, string? iban, long managerIqama = 777)
    {
        var company = new Company { Name = "Client" };
        var housing = new Housing { Name = "H1", Address = "A", Capacity = 10, ManagerIqamaNo = managerIqama };
        var employee = new Employees
        {
            IqamaNo = Random.Shared.NextInt64(100000, 999999),
            IqamaEndM = new DateOnly(2027, 1, 1),
            IqamaEndH = new DateOnly(1448, 1, 1),
            Sponsor = "Sponsor",
            JobTitle = "Rider",
            NameAR = "Rider",
            NameEN = "Rider",
            Country = "SA",
            Phone = "0500000000",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IBAN = iban,
            Housing = housing
        };
        var rider = new RiderDetails
        {
            WorkingId = $"W{Random.Shared.Next(1000, 9999)}",
            Company = company,
            Employee = employee
        };

        db.RiderDetails.Add(rider);
        await db.SaveChangesAsync();
        return rider;
    }

    private static async Task<RiderMonthlySalary> SeedApprovedSalaryAsync(ApplicationDbcontext db, string? iban, long managerIqama = 777)
    {
        var rider = await SeedRiderAsync(db, iban, managerIqama);
        var salary = new RiderMonthlySalary
        {
            RiderId = rider.Id,
            Rider = rider,
            Year = 2026,
            Month = 6,
            PaymentMethod = string.IsNullOrWhiteSpace(iban) ? RiderPaymentMethod.Cash : RiderPaymentMethod.BankTransfer,
            Status = SalaryStatus.Approved,
            NetSalary = 100,
            GrossEarnings = 100,
            RemainingAmount = 100,
            GeneratedBy = "test"
        };
        db.RiderMonthlySalaries.Add(salary);
        await db.SaveChangesAsync();
        return salary;
    }

    private static IFormFile CreateCompanyBillFile(string workingId, int acceptedOrders, decimal netAmount)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Summary");
        ws.Cell(1, 1).Value = "rider id";
        ws.Cell(1, 2).Value = "completed orders";
        ws.Cell(1, 3).Value = "basic payment";
        ws.Cell(1, 4).Value = "net amount";
        ws.Cell(2, 1).Value = workingId;
        ws.Cell(2, 2).Value = acceptedOrders;
        ws.Cell(2, 3).Value = netAmount;
        ws.Cell(2, 4).Value = netAmount;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "company-bill.xlsx");
    }
}
