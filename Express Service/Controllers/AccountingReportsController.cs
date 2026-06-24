using Application.Service.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting/reports")]
[ApiController]
[Authorize(Roles = "Master,Admin,Accountant")]
public class AccountingReportsController(IAccountingReportService reportService) : ControllerBase
{
    [HttpGet("trial-balance")]
    public async Task<IActionResult> GetTrialBalance(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetTrialBalanceAsync(from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("general-ledger")]
    public async Task<IActionResult> GetGeneralLedger(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? accountId,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetGeneralLedgerAsync(from, to, accountId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
