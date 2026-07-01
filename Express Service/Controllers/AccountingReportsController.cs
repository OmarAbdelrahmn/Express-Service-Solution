using Application.Service.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting/reports")]
[ApiController]
[Authorize(Roles = "Master,Admin,Accountant")]
public class AccountingReportsController(IAccountingReportService reportService) : ControllerBase
{
    [HttpGet("~/api/accounting/companies/{companyId:int}/reports/trial-balance")]
    public async Task<IActionResult> GetCompanyTrialBalance(
        int companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetTrialBalanceAsync(from, to, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("trial-balance")]
    public async Task<IActionResult> GetTrialBalance(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? companyId,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetTrialBalanceAsync(from, to, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("~/api/accounting/companies/{companyId:int}/reports/general-ledger")]
    public async Task<IActionResult> GetCompanyGeneralLedger(
        int companyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? accountId,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetGeneralLedgerAsync(from, to, accountId, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("general-ledger")]
    public async Task<IActionResult> GetGeneralLedger(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? accountId,
        [FromQuery] int? companyId,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetGeneralLedgerAsync(from, to, accountId, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
