using Application.Extensions;
using Application.Service.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting/company-finance")]
[ApiController]
[Authorize(Roles = "Master,Admin,Accountant")]
public class CompanyFinanceController(ICompanyFinanceService financeService) : ControllerBase
{
    [HttpGet("~/api/accounting/companies/{companyId:int}/finance/summary")]
    public async Task<IActionResult> GetCompanySummary(
        int companyId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetSummaryAsync(year, month, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int? companyId,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetSummaryAsync(year, month, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("~/api/accounting/companies/{companyId:int}/finance/income")]
    public async Task<IActionResult> GetCompanyIncome(
        int companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetIncomeAsync(from, to, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("income")]
    public async Task<IActionResult> GetIncome(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? companyId,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetIncomeAsync(from, to, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("~/api/accounting/companies/{companyId:int}/finance/expenses")]
    public async Task<IActionResult> GetCompanyExpenses(
        int companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetExpensesAsync(from, to, companyId, category, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("expenses")]
    public async Task<IActionResult> GetExpenses(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? companyId,
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetExpensesAsync(from, to, companyId, category, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("~/api/accounting/companies/{companyId:int}/finance/expenses")]
    public async Task<IActionResult> CreateCompanyExpense(
        int companyId,
        [FromBody] CompanyExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await financeService.CreateExpenseAsync(
            request with { CompanyId = companyId },
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] CompanyExpenseRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CreateExpenseAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("~/api/accounting/companies/{companyId:int}/finance/receipts")]
    public async Task<IActionResult> CreateCompanyReceipt(
        int companyId,
        [FromBody] CompanyPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var result = await financeService.CreateReceiptAsync(
            request with { CompanyId = companyId },
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> CreateReceipt([FromBody] CompanyPaymentReceiptRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CreateReceiptAsync(
            request,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("receipts/{receiptId:int}/reverse")]
    public async Task<IActionResult> ReverseReceipt(int receiptId, CancellationToken cancellationToken)
    {
        var result = await financeService.ReverseReceiptAsync(
            receiptId,
            User.GetUserId() ?? "system",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("~/api/accounting/companies/{companyId:int}/finance/profit-loss")]
    public async Task<IActionResult> GetCompanyProfitLoss(
        int companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetProfitLossAsync(from, to, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("profit-loss")]
    public async Task<IActionResult> GetProfitLoss(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? companyId,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetProfitLossAsync(from, to, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("cost-centers")]
    public async Task<IActionResult> GetCostCenters(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await financeService.GetCostCentersAsync(from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
