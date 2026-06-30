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
    public async Task ImportCompanyBill_RequiresExistingCompany()
    {
        await using var db = CreateDb();
        var service = new AccountingImportService(db);

        var file = CreateCompanyBillFile("W404", 10, 100);
        var result = await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(file, 2026, 6, 404, CompanyBillTemplateType.Generic, null),
            "accountant");

        Assert.True(result.IsFailure);
        Assert.Equal(0, await db.CompanyBillImports.CountAsync());
    }

    [Fact]
    public async Task CompanyScopedImportRead_RejectsWrongCompany()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        var otherCompany = new Company { Name = "Other Client" };
        db.Companies.Add(otherCompany);
        await db.SaveChangesAsync();
        var service = new AccountingImportService(db);

        var file = CreateCompanyBillFile(rider.WorkingId!, 10, 100);
        var import = await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(file, 2026, 6, rider.CompanyId, CompanyBillTemplateType.Generic, null),
            "accountant");
        var wrongCompanyRead = await service.GetCompanyImportAsync(otherCompany.Id, import.Value.Id);

        Assert.True(import.IsSuccess);
        Assert.True(wrongCompanyRead.IsFailure);
    }

    [Fact]
    public async Task CompanyScopedImportList_ReturnsOnlySelectedCompany()
    {
        await using var db = CreateDb();
        var rider1 = await SeedRiderAsync(db, iban: "SA123");
        var rider2 = await SeedRiderAsync(db, iban: "SA456");
        var service = new AccountingImportService(db);

        var import1 = await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(CreateCompanyBillFile(rider1.WorkingId!, 10, 100), 2026, 6, rider1.CompanyId, CompanyBillTemplateType.Generic, null),
            "accountant");
        await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(CreateCompanyBillFile(rider2.WorkingId!, 20, 200), 2026, 6, rider2.CompanyId, CompanyBillTemplateType.Generic, null),
            "accountant");

        var list = await service.GetCompanyImportsAsync(
            new CompanyBillImportQuery(rider1.CompanyId, 2026, 6, null, null));

        Assert.True(import1.IsSuccess);
        Assert.True(list.IsSuccess);
        var item = Assert.Single(list.Value);
        Assert.Equal(import1.Value.Id, item.Id);
        Assert.Equal(rider1.CompanyId, item.CompanyId);
    }

    [Fact]
    public async Task ImportCompanyBill_DoesNotResolveRiderFromDifferentCompany()
    {
        await using var db = CreateDb();
        var sourceRider = await SeedRiderAsync(db, iban: "SA123");
        var otherCompany = new Company { Name = "Other Client" };
        db.Companies.Add(otherCompany);
        await db.SaveChangesAsync();
        var service = new AccountingImportService(db);

        var import = await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(
                CreateCompanyBillFile(sourceRider.WorkingId!, 10, 100),
                2026,
                6,
                otherCompany.Id,
                CompanyBillTemplateType.Generic,
                null),
            "accountant");
        var approve = await service.ApproveCompanyBillImportAsync(otherCompany.Id, import.Value.Id, "accountant");

        Assert.True(import.IsSuccess);
        Assert.True(import.Value.IssueCount > 0);
        Assert.True(approve.IsFailure);
        Assert.Equal(0, await db.CompanyReceivables.CountAsync());
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
    public async Task FixedMonthlyEarnings_GenerateFixedSalary_ForCompanyRiders()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        var service = new AccountingSalaryService(db);

        var earnings = await service.CreateFixedMonthlyEarningsAsync(
            new FixedMonthlyEarningRequest(2026, 6, rider.CompanyId, 2000, true, "Amazon fixed salary"),
            "accountant");
        var salaries = await service.GenerateMonthlySalariesAsync(
            new GenerateSalaryRequest(2026, 6, rider.CompanyId),
            "accountant");

        Assert.True(earnings.IsSuccess);
        Assert.True(salaries.IsSuccess);
        var salary = salaries.Value.Single();
        Assert.Equal(2000, salary.NetSalary);
        Assert.Contains(salary.Lines, l => l.SourceType == "FixedMonthlySalary" && l.Amount == 2000);
    }

    [Fact]
    public async Task SalaryRules_CrudAndFtrHungerGeneration_UsesConfiguredRule()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        var service = new AccountingSalaryService(db);

        var created = await service.CreateSalaryRuleAsync(new SalaryRuleRequest(
            rider.CompanyId,
            CompanyBillTemplateType.FtrHunger,
            "Custom Hunger salary",
            400,
            1800,
            7,
            2.5m,
            new DateOnly(2026, 1, 1),
            null,
            10,
            "Company override",
            true));
        var listed = await service.GetSalaryRulesAsync(rider.CompanyId, CompanyBillTemplateType.FtrHunger);
        var fetched = await service.GetSalaryRuleAsync(created.Value.Id);

        db.CompanyBillImports.Add(new CompanyBillImport
        {
            CompanyId = rider.CompanyId,
            CompanyNameSnapshot = "Client",
            TemplateType = CompanyBillTemplateType.FtrHunger,
            Year = 2026,
            Month = 6,
            SourceFileName = "hunger.xlsx",
            UploadedBy = "accountant",
            Status = AccountingRecordStatus.Posted,
            RiderSummaries =
            {
                new CompanyBillRiderSummary
                {
                    SourceRiderId = rider.WorkingId!,
                    OriginalRiderId = rider.Id,
                    PaidRiderId = rider.Id,
                    ResolutionStatus = ImportResolutionStatus.Resolved,
                    AcceptedOrders = 450,
                    NetAmount = 999
                }
            }
        });
        await db.SaveChangesAsync();

        var salaries = await service.GenerateMonthlySalariesAsync(
            new GenerateSalaryRequest(2026, 6, rider.CompanyId),
            "accountant");
        var updated = await service.UpdateSalaryRuleAsync(created.Value.Id, new SalaryRuleRequest(
            rider.CompanyId,
            CompanyBillTemplateType.FtrHunger,
            "Updated Hunger salary",
            400,
            1900,
            8,
            3,
            new DateOnly(2026, 1, 1),
            null,
            11,
            "Updated",
            true));
        var deleted = await service.DeleteSalaryRuleAsync(created.Value.Id);

        Assert.True(created.IsSuccess);
        Assert.True(listed.IsSuccess);
        Assert.True(fetched.IsSuccess);
        Assert.True(salaries.IsSuccess);
        Assert.True(updated.IsSuccess);
        Assert.True(deleted.IsSuccess);
        Assert.Contains(listed.Value, r => r.Id == created.Value.Id);
        Assert.Equal("Custom Hunger salary", fetched.Value.Name);
        Assert.Equal(2150, salaries.Value.Single().NetSalary);
        Assert.Equal("Updated Hunger salary", updated.Value.Name);
        Assert.False(deleted.Value.IsActive);
    }

    [Fact]
    public async Task BulkInternetReplacement_AddsReimbursementLine_ToGeneratedSalary()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        db.RiderEarnings.Add(new RiderEarning
        {
            PaidRiderId = rider.Id,
            CompanyId = rider.CompanyId,
            Year = 2026,
            Month = 6,
            SalaryAmount = 2000,
            SourceType = "test",
            Status = AccountingRecordStatus.Approved
        });
        await db.SaveChangesAsync();
        var service = new AccountingSalaryService(db);

        var replacements = await service.CreateBulkInternetReplacementAsync(
            new BulkInternetReplacementRequest(2026, 6, rider.CompanyId, 100, new DateOnly(2026, 6, 30), true, null, "Internet"),
            "accountant");
        var salaries = await service.GenerateMonthlySalariesAsync(
            new GenerateSalaryRequest(2026, 6, rider.CompanyId),
            "accountant");

        Assert.True(replacements.IsSuccess);
        Assert.True(salaries.IsSuccess);
        var salary = salaries.Value.Single();
        Assert.Equal(2100, salary.NetSalary);
        Assert.Contains(salary.Lines, l => l.Type == SalaryLineType.Reimbursement && l.Amount == 100);
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
            new CompanyPaymentReceiptRequest(receivables[0].Id, company.Id, null, new DateOnly(2026, 6, 20), 100, "REF-1", "Bank", null),
            "accountant");
        var duplicate = await service.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(null, company.Id, null, new DateOnly(2026, 6, 20), 10, "REF-1", "Bank", null),
            "accountant");
        var overpayment = await service.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(receivables[1].Id, company.Id, null, new DateOnly(2026, 6, 21), 60, "REF-2", "Bank", null),
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
            new CompanyPaymentReceiptRequest(null, null, null, new DateOnly(2026, 6, 10), 10, "REF", "Bank", null),
            "accountant");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task PeriodWorkflow_CloseBlocksPosting_AndWritesAuditValues()
    {
        await using var db = CreateDb();
        var periodService = new AccountingPeriodService(db);
        var financeService = new CompanyFinanceService(db);

        var closed = await periodService.ClosePeriodAsync(2026, 6, new PeriodStatusChangeRequest("Month reviewed"), "accountant");
        var receipt = await financeService.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(null, null, null, new DateOnly(2026, 6, 10), 10, "REF", "Bank", null),
            "accountant");
        var audit = await db.AccountingAuditLogs.SingleAsync(a => a.EntityName == "AccountingPeriod" && a.Action == "Close");

        Assert.True(closed.IsSuccess);
        Assert.Equal(AccountingPeriodStatus.Closed, closed.Value.Status);
        Assert.True(receipt.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(audit.OldValuesJson));
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValuesJson));
    }

    [Fact]
    public async Task ApproveCompanyBill_PostsVat_ToVatPayableAccount()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        var service = new AccountingImportService(db);

        var import = await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(CreateCompanyBillFile(rider.WorkingId!, 10, 115, 15), 2026, 6, rider.CompanyId, CompanyBillTemplateType.Generic, null),
            "accountant");
        var approved = await service.ApproveCompanyBillImportAsync(import.Value.Id, "accountant");

        Assert.True(import.IsSuccess);
        Assert.True(approved.IsSuccess);
        Assert.Contains(
            await db.JournalEntryLines.ToListAsync(),
            l => l.AccountId == 18 && l.Credit == 15);
        Assert.DoesNotContain(
            await db.JournalEntryLines.ToListAsync(),
            l => l.AccountId == 7 && l.Notes == "VAT payable");
    }

    [Fact]
    public async Task ResolveRiderSummary_ClearsIssue_AndAllowsApproval()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        var service = new AccountingImportService(db);

        var import = await service.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(CreateCompanyBillFile("UNKNOWN-RIDER", 10, 100), 2026, 6, rider.CompanyId, CompanyBillTemplateType.Generic, null),
            "accountant");
        var summary = await db.CompanyBillRiderSummaries.SingleAsync(s => s.CompanyBillImportId == import.Value.Id);
        var issue = await db.CompanyBillResolutionIssues.SingleAsync(i => i.CompanyBillImportId == import.Value.Id);
        var resolved = await service.ResolveRiderSummaryAsync(
            import.Value.Id,
            summary.Id,
            new ResolveRiderSummaryRequest(rider.Id, "Matched manually"),
            "accountant");
        var approved = await service.ApproveCompanyBillImportAsync(import.Value.Id, "accountant");
        var audit = await db.AccountingAuditLogs.SingleAsync(a => a.EntityName == "CompanyBillRiderSummary" && a.Action == "ResolveRider");

        Assert.True(import.IsSuccess);
        Assert.True(resolved.IsSuccess);
        Assert.True(approved.IsSuccess);
        Assert.True((await db.CompanyBillResolutionIssues.FindAsync(issue.Id))!.IsResolved);
        Assert.False(string.IsNullOrWhiteSpace(audit.OldValuesJson));
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValuesJson));
    }

    [Fact]
    public async Task Receipt_WithBankAccount_LinksCashJournalLine()
    {
        await using var db = CreateDb();
        var company = new Company { Name = "Client" };
        var bank = new BankAccount { AccountName = "Main", BankName = "Bank", Iban = "SA123" };
        db.Companies.Add(company);
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.CompanyReceivables.Add(new CompanyReceivable { CompanyId = company.Id, Year = 2026, Month = 6, NetAmount = 100, PendingAmount = 100, Status = AccountingRecordStatus.Posted });
        await db.SaveChangesAsync();
        var receivable = await db.CompanyReceivables.SingleAsync();
        var service = new CompanyFinanceService(db);

        var receipt = await service.CreateReceiptAsync(
            new CompanyPaymentReceiptRequest(receivable.Id, company.Id, bank.Id, new DateOnly(2026, 6, 20), 100, "REF-BANK", "Bank", null),
            "accountant");

        Assert.True(receipt.IsSuccess);
        Assert.Equal(bank.Id, receipt.Value.BankAccountId);
        Assert.Contains(
            await db.JournalEntryLines.ToListAsync(),
            l => l.AccountId == 1 && l.Debit == 100 && l.BankAccountId == bank.Id);
    }

    [Fact]
    public async Task FinalSettlement_CreatesSettlementEntries_AndCanWriteOffLoans()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        var service = new AccountingSalaryService(db);
        var loan = await service.CreateLoanAsync(new RiderLoanRequest(rider.Id, 100, 2026, 6, 1, "Loan"), "accountant");

        var settlement = await service.CreateFinalSettlementAsync(
            new RiderFinalSettlementRequest(rider.Id, new DateOnly(2026, 6, 25), 500, 50, 20, true, "End of service"),
            "accountant");
        var storedLoan = await db.RiderLoans.FindAsync(loan.Value.Id);

        Assert.True(loan.IsSuccess);
        Assert.True(settlement.IsSuccess);
        Assert.Equal(100, settlement.Value.LoanWriteOffAmount);
        Assert.Equal(530, settlement.Value.NetSettlementAmount);
        Assert.Equal(0, storedLoan!.RemainingAmount);
        Assert.Equal(AccountingRecordStatus.Reversed, storedLoan.Status);
        Assert.Equal(500, await db.RiderEarnings.Where(e => e.SourceType == "RiderFinalSettlement").SumAsync(e => e.SalaryAmount));
        Assert.Contains(await db.RiderFinancialItems.Include(i => i.Type).ToListAsync(), i => i.Type.Code == "FINAL_SETTLEMENT_REIMBURSEMENT" && i.Amount == 50);
        Assert.Contains(await db.RiderFinancialItems.Include(i => i.Type).ToListAsync(), i => i.Type.Code == "FINAL_SETTLEMENT_DEDUCTION" && i.Amount == 20);
    }

    [Fact]
    public async Task RefreshProfitSnapshot_UpsertsMonthlySnapshot()
    {
        await using var db = CreateDb();
        var rider = await SeedRiderAsync(db, iban: "SA123");
        db.CompanyReceivables.Add(new CompanyReceivable { CompanyId = rider.CompanyId, Year = 2026, Month = 6, GrossAmount = 1000, VatAmount = 150, NetAmount = 1150, PendingAmount = 1150, Status = AccountingRecordStatus.Posted });
        db.RiderMonthlySalaries.Add(new RiderMonthlySalary { RiderId = rider.Id, Rider = rider, Year = 2026, Month = 6, NetSalary = 400, TotalDeductions = 20, GeneratedBy = "test" });
        db.CompanyExpenses.Add(new CompanyExpense { CompanyExpenseCategoryId = 11, CompanyId = rider.CompanyId, ExpenseDate = new DateOnly(2026, 6, 20), Amount = 100, Status = AccountingRecordStatus.Approved, CreatedBy = "test" });
        await db.SaveChangesAsync();
        var service = new CompanyFinanceService(db);

        var summary = await service.RefreshProfitSnapshotAsync(2026, 6, rider.CompanyId);
        var snapshot = await db.CompanyProfitSnapshots.SingleAsync(s => s.CompanyId == rider.CompanyId && s.Year == 2026 && s.Month == 6);

        Assert.True(summary.IsSuccess);
        Assert.Equal(summary.Value.GrossIncome, snapshot.GrossIncome);
        Assert.Equal(summary.Value.Profit, snapshot.Profit);
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

    private static IFormFile CreateCompanyBillFile(string workingId, int acceptedOrders, decimal netAmount, decimal vatAmount = 0)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Summary");
        ws.Cell(1, 1).Value = "rider id";
        ws.Cell(1, 2).Value = "completed orders";
        ws.Cell(1, 3).Value = "basic payment";
        ws.Cell(1, 4).Value = "net amount";
        ws.Cell(1, 5).Value = "vat";
        ws.Cell(2, 1).Value = workingId;
        ws.Cell(2, 2).Value = acceptedOrders;
        ws.Cell(2, 3).Value = netAmount - vatAmount;
        ws.Cell(2, 4).Value = netAmount;
        ws.Cell(2, 5).Value = vatAmount;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "company-bill.xlsx");
    }
}
