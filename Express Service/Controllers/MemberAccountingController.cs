using Application.Extensions;
using Application.Service.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/member/accounting")]
[ApiController]
[Authorize(Roles = "Master,Admin,Member")]
public class MemberAccountingController(IAccountingPaymentService paymentService) : ControllerBase
{
    [HttpGet("cash-batches")]
    public async Task<IActionResult> GetCashBatches(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        var managerIqamaNo = User.GetUserIqamaNo();
        var result = await paymentService.GetCashHandoverForHousingManagerAsync(managerIqamaNo, year, month, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("cash-lines/{lineId:int}/submit")]
    public async Task<IActionResult> SubmitCashLine(
        int lineId,
        [FromBody] CashSalarySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await paymentService.SubmitCashHandoverLineAsync(
            lineId,
            request,
            User.GetUserIqamaNo(),
            User.GetUserId() ?? "member",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("cash-batches/{batchId:int}/submit")]
    public async Task<IActionResult> SubmitCashBatch(
        int batchId,
        [FromBody] CashSalarySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await paymentService.SubmitCashHandoverBatchAsync(
            batchId,
            request,
            User.GetUserIqamaNo(),
            User.GetUserId() ?? "member",
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
