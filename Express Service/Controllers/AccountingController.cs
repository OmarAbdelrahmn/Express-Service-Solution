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
    IAccountingImportService importService,
    IAccountingSalaryService salaryService,
    IAccountingPaymentService paymentService,
    IRiderAccountingProfileService riderProfileService) : ControllerBase
{
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
        var result = await importService.ImportCompanyBillAsync(
            new ImportCompanyBillRequest(file, year, month, companyId, templateType, notes),
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

    [HttpPost("salaries/generate")]
    public async Task<IActionResult> GenerateSalaries([FromBody] GenerateSalaryRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.GenerateMonthlySalariesAsync(
            request,
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

    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] RiderLoanRequest request, CancellationToken cancellationToken)
    {
        var result = await salaryService.CreateLoanAsync(
            request,
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
}
