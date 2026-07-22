using Application.Extensions;
using Application.Service.Accounting;
using Domain.Entities.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting")]
[ApiController]
[Authorize(Roles = "Master,Admin,Accountant")]
public class AccountingController(
    IAccountingPeriodService periodService,
    IAccountingImportService importService,
    IAccountingSalaryService salaryService,
    IAccountingPaymentService paymentService,
    IRiderAccountingProfileService riderProfileService) : ControllerBase
{
    [HttpGet("periods")]
    public async Task<IActionResult> GetPeriods([FromQuery] int? year, CancellationToken cancellationToken)
    {
        var result = await periodService.GetPeriodsAsync(year, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("periods/{year:int}/{month:int}")]
    public async Task<IActionResult> GetPeriod(int year, int month, CancellationToken cancellationToken)
    {
        var result = await periodService.GetPeriodAsync(year, month, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("periods/{year:int}/{month:int}/close")]
    public async Task<IActionResult> ClosePeriod(
        int year,
        int month,
        [FromBody] PeriodStatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await periodService.ClosePeriodAsync(year, month, request, User.GetUserId() ?? "system", cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("periods/{year:int}/{month:int}/lock")]
    public async Task<IActionResult> LockPeriod(
        int year,
        int month,
        [FromBody] PeriodStatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await periodService.LockPeriodAsync(year, month, request, User.GetUserId() ?? "system", cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("periods/{year:int}/{month:int}/reopen")]
    public async Task<IActionResult> ReopenPeriod(
        int year,
        int month,
        [FromBody] PeriodStatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await periodService.ReopenPeriodAsync(year, month, request, User.GetUserId() ?? "system", cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("companies/{companyId:int}/imports/company-bills/info")]
    public async Task<IActionResult> GetCompanyBillImportInfo(int companyId, CancellationToken cancellationToken)
    {
        var result = await importService.GetCompanyImportInfoAsync(companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("companies/{companyId:int}/imports")]
    public async Task<IActionResult> GetCompanyImports(
        int companyId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] CompanyBillTemplateType? templateType,
        [FromQuery] AccountingRecordStatus? status,
        CancellationToken cancellationToken)
    {
        var result = await importService.GetCompanyImportsAsync(
            new CompanyBillImportQuery(companyId, year, month, templateType, status),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("companies/{companyId:int}/imports/{importId:int}")]
    public async Task<IActionResult> GetCompanyImport(int companyId, int importId, CancellationToken cancellationToken)
    {
        var result = await importService.GetCompanyImportAsync(companyId, importId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("companies/{companyId:int}/imports/company-bills/hunger-ftr")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> ImportHungerFtrCompanyBill(
        int companyId,
        IFormFile file,
        [FromForm] int year,
        [FromForm] int month,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
        => ImportCompanyBillForTemplate(companyId, CompanyBillTemplateType.FtrHunger, file, year, month, notes, cancellationToken);

    [HttpPost("companies/{companyId:int}/imports/company-bills/keeta-pay-per-order")]
    [HttpPost("companies/{companyId:int}/imports/company-bills/keeta-shifts")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> ImportKeetaPayPerOrderCompanyBill(
        int companyId,
        IFormFile file,
        [FromForm] int year,
        [FromForm] int month,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
        => ImportCompanyBillForTemplate(companyId, CompanyBillTemplateType.KeetaPayPerOrder, file, year, month, notes, cancellationToken);

    [HttpPost("companies/{companyId:int}/imports/company-bills/keeta-segment")]
    [HttpPost("companies/{companyId:int}/imports/company-bills/keeta-freelancers")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> ImportKeetaSegmentCompanyBill(
        int companyId,
        IFormFile file,
        [FromForm] int year,
        [FromForm] int month,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
        => ImportCompanyBillForTemplate(companyId, CompanyBillTemplateType.KeetaSegment, file, year, month, notes, cancellationToken);

    [HttpPost("companies/{companyId:int}/imports/company-bills/amazon")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> ImportAmazonCompanyBill(
        int companyId,
        IFormFile file,
        [FromForm] int year,
        [FromForm] int month,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
        => ImportCompanyBillForTemplate(companyId, CompanyBillTemplateType.Amazon, file, year, month, notes, cancellationToken);

    [HttpPost("companies/{companyId:int}/imports/company-bills/generic")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> ImportGenericCompanyBill(
        int companyId,
        IFormFile file,
        [FromForm] int year,
        [FromForm] int month,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
        => ImportCompanyBillForTemplate(companyId, CompanyBillTemplateType.Generic, file, year, month, notes, cancellationToken);

    [HttpPost("companies/{companyId:int}/imports/{importId:int}/approve")]
    public async Task<IActionResult> ApproveCompanyImport(int companyId, int importId, CancellationToken cancellationToken)
    {
        var result = await importService.ApproveCompanyBillImportAsync(
            companyId,
            importId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("companies/{companyId:int}/imports/{importId:int}/reverse")]
    public async Task<IActionResult> ReverseCompanyImport(int companyId, int importId, CancellationToken cancellationToken)
    {
        var result = await importService.ReverseCompanyBillImportAsync(
            companyId,
            importId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("imports/company-bills")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportCompanyBill(
        IFormFile file,
        [FromForm] int year,
        [FromForm] int month,
        [FromForm] int? companyId,
        [FromForm] CompanyBillTemplateType? templateType,
        [FromForm] string? notes,
        CancellationToken cancellationToken)
    {
        if (companyId is null)
            return BadRequest(new
            {
                error = "CompanyId is required. Use POST /api/accounting/companies/{companyId}/imports/company-bills/{template}."
            });

        var result = await importService.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(file, year, month, companyId.Value, templateType ?? CompanyBillTemplateType.Generic, notes),
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("imports/{importId:int}")]
    public async Task<IActionResult> GetImport(int importId, CancellationToken cancellationToken)
    {
        var result = await importService.GetImportAsync(importId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("imports/{importId:int}/approve")]
    public async Task<IActionResult> ApproveImport(int importId, CancellationToken cancellationToken)
    {
        var result = await importService.ApproveCompanyBillImportAsync(
            importId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("imports/{importId:int}/reverse")]
    public async Task<IActionResult> ReverseImport(int importId, CancellationToken cancellationToken)
    {
        var result = await importService.ReverseCompanyBillImportAsync(
            importId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("imports/{importId:int}/issues/{issueId:int}/resolve")]
    public async Task<IActionResult> ResolveImportIssue(
        int importId,
        int issueId,
        [FromBody] ResolveImportIssueRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.ResolveImportIssueAsync(
            importId,
            issueId,
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("imports/{importId:int}/rider-summaries/{summaryId:int}/resolve")]
    public async Task<IActionResult> ResolveRiderSummary(
        int importId,
        int summaryId,
        [FromBody] ResolveRiderSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importService.ResolveRiderSummaryAsync(
            importId,
            summaryId,
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("salaries/generate")]
    public async Task<IActionResult> GenerateSalaries([FromBody] GenerateSalaryRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.GenerateMonthlySalariesAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("companies/{companyId:int}/salaries/generate")]
    public async Task<IActionResult> GenerateCompanySalaries(
        int companyId,
        [FromBody] GenerateSalaryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.GenerateMonthlySalariesAsync(
            request with { CompanyId = companyId },
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("salaries/{salaryId:int}")]
    public async Task<IActionResult> GetSalary(int salaryId, CancellationToken cancellationToken)
    {
        var result = await salaryService.GetSalaryAsync(salaryId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("companies/{companyId:int}/salaries/{salaryId:int}")]
    public async Task<IActionResult> GetCompanySalary(int companyId, int salaryId, CancellationToken cancellationToken)
    {
        var result = await salaryService.GetCompanySalaryAsync(companyId, salaryId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("salaries/{salaryId:int}/approve")]
    public async Task<IActionResult> ApproveSalary(int salaryId, CancellationToken cancellationToken)
    {
        var result = await salaryService.ApproveSalaryAsync(
            salaryId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("salaries/{salaryId:int}/reverse")]
    public async Task<IActionResult> ReverseSalary(int salaryId, CancellationToken cancellationToken)
    {
        var result = await salaryService.ReverseSalaryAsync(
            salaryId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("bonus-rules")]
    public async Task<IActionResult> CreateBonusRule([FromBody] BonusRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateBonusRuleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("bonus-rules")]
    public async Task<IActionResult> GetBonusRules([FromQuery] int? companyId, CancellationToken cancellationToken)
    {
        var result = await salaryService.GetBonusRulesAsync(companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("companies/{companyId:int}/bonus-rules")]
    public async Task<IActionResult> CreateCompanyBonusRule(
        int companyId,
        [FromBody] BonusRuleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateBonusRuleAsync(request with { CompanyId = companyId }, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("companies/{companyId:int}/bonus-rules")]
    public async Task<IActionResult> GetCompanyBonusRules(int companyId, CancellationToken cancellationToken)
    {
        var result = await salaryService.GetBonusRulesAsync(companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("salary-rules")]
    public async Task<IActionResult> CreateSalaryRule([FromBody] SalaryRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateSalaryRuleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("salary-rules")]
    public async Task<IActionResult> GetSalaryRules(
        [FromQuery] int? companyId,
        [FromQuery] CompanyBillTemplateType? templateType,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.GetSalaryRulesAsync(companyId, templateType, includeInactive, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("salary-rules/{ruleId:int}")]
    public async Task<IActionResult> GetSalaryRule(int ruleId, CancellationToken cancellationToken)
    {
        var result = await salaryService.GetSalaryRuleAsync(ruleId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("salary-rules/{ruleId:int}")]
    public async Task<IActionResult> UpdateSalaryRule(
        int ruleId,
        [FromBody] SalaryRuleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.UpdateSalaryRuleAsync(ruleId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("salary-rules/{ruleId:int}")]
    public async Task<IActionResult> DeleteSalaryRule(int ruleId, CancellationToken cancellationToken)
    {
        var result = await salaryService.DeleteSalaryRuleAsync(ruleId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("companies/{companyId:int}/salary-rules")]
    public async Task<IActionResult> CreateCompanySalaryRule(
        int companyId,
        [FromBody] SalaryRuleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateSalaryRuleAsync(request with { CompanyId = companyId }, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("companies/{companyId:int}/salary-rules")]
    public async Task<IActionResult> GetCompanySalaryRules(
        int companyId,
        [FromQuery] CompanyBillTemplateType? templateType,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.GetSalaryRulesAsync(companyId, templateType, includeInactive, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("financial-item-types")]
    public async Task<IActionResult> CreateFinancialItemType([FromBody] FinancialItemTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateFinancialItemTypeAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("financial-items")]
    public async Task<IActionResult> CreateFinancialItem([FromBody] RiderFinancialItemRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateFinancialItemAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("financial-items/internet-replacement/bulk")]
    public async Task<IActionResult> CreateBulkInternetReplacement(
        [FromBody] BulkInternetReplacementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateBulkInternetReplacementAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("earnings/fixed-monthly")]
    public async Task<IActionResult> CreateFixedMonthlyEarnings(
        [FromBody] FixedMonthlyEarningRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateFixedMonthlyEarningsAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] RiderLoanRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateLoanAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("riders/{riderId:int}/final-settlements")]
    public async Task<IActionResult> CreateFinalSettlement(
        int riderId,
        [FromBody] RiderFinalSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateFinalSettlementAsync(
            request with { RiderId = riderId },
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("payments/bank-batches")]
    public async Task<IActionResult> CreateBankBatch([FromBody] CreatePaymentBatchRequest request, CancellationToken cancellationToken)
    {
        var result = await paymentService.CreateBankPaymentBatchAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("payments/bank-batches/{batchId:int}/export")]
    public async Task<IActionResult> ExportBankBatch(int batchId, CancellationToken cancellationToken)
    {
        var result = await paymentService.ExportBankPaymentBatchAsync(batchId, cancellationToken);
        return result.IsSuccess
            ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : result.ToProblem();
    }

    [HttpPost("payments/bank-batches/{batchId:int}/send")]
    public async Task<IActionResult> SendBankBatch(int batchId, CancellationToken cancellationToken)
    {
        var result = await paymentService.MarkBankPaymentBatchSentAsync(
            batchId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("payments/bank-batches/{batchId:int}/confirm")]
    public async Task<IActionResult> ConfirmBankBatch(
        int batchId,
        [FromBody] BankPaymentConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await paymentService.ConfirmBankPaymentBatchAsync(
            batchId,
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("payments/{paymentId:int}/reverse")]
    public async Task<IActionResult> ReversePayment(int paymentId, CancellationToken cancellationToken)
    {
        var result = await paymentService.ReverseSalaryPaymentAsync(
            paymentId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("payments/cash-batches")]
    public async Task<IActionResult> CreateCashBatch([FromBody] CreateCashHandoverBatchRequest request, CancellationToken cancellationToken)
    {
        var result = await paymentService.CreateCashHandoverBatchAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("payments/cash-batches/{batchId:int}/export")]
    public async Task<IActionResult> ExportCashBatch(int batchId, CancellationToken cancellationToken)
    {
        var result = await paymentService.ExportCashHandoverBatchAsync(batchId, cancellationToken);
        return result.IsSuccess
            ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : result.ToProblem();
    }

    [HttpGet("riders/{riderId:int}/profile")]
    public async Task<IActionResult> GetRiderAccountingProfile(
        int riderId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await riderProfileService.GetRiderProfileAsync(riderId, from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    private async Task<IActionResult> ImportCompanyBillForTemplate(
        int companyId,
        CompanyBillTemplateType templateType,
        IFormFile file,
        int year,
        int month,
        string? notes,
        CancellationToken cancellationToken)
    {
        var result = await importService.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(file, year, month, companyId, templateType, notes),
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
