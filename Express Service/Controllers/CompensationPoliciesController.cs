using Application.Contracts.Common;
using Application.Contracts.Compensation;
using Application.Extensions;
using Application.Service.Compensation;
using Domain.Entities.AccountingPlatform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting/compensation-policies")]
[ApiController]
[Authorize(Roles = "Master,Accountant")]
public class CompensationPoliciesController(ICompensationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] int legalEntityId,
        [FromQuery] int? platformAccountId,
        [FromQuery] string? category,
        [FromQuery] CompensationPolicyStatus? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct) => await WithActor(actor => service.GetPoliciesAsync(pagination, legalEntityId, platformAccountId, category, status, fromDate, toDate, search, sortBy, sortDirection, actor, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompensationPolicyRequest request, CancellationToken ct) => await WithActor(actor => service.CreatePolicyAsync(request, actor, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => await WithActor(actor => service.GetPolicyAsync(id, actor, ct));

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, [FromBody] ActivateCompensationPolicyRequest request, CancellationToken ct) => await WithActor(actor => service.ActivatePolicyAsync(id, request, actor, ct));

    [HttpPost("{id:guid}/versions")]
    public async Task<IActionResult> CloneVersion(Guid id, [FromBody] CloneCompensationPolicyVersionRequest request, CancellationToken ct) => await WithActor(actor => service.CloneVersionAsync(id, request, actor, ct));

    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id, [FromBody] RetireCompensationPolicyRequest request, CancellationToken ct) => await WithActor(actor => service.RetirePolicyAsync(id, request, actor, ct));

    [HttpPost("{id:guid}/simulate")]
    public async Task<IActionResult> Simulate(Guid id, [FromBody] SimulateCompensationPolicyRequest request, CancellationToken ct) => await WithActor(actor => service.SimulateAsync(id, request, actor, ct));

    private async Task<IActionResult> WithActor<T>(Func<string, Task<Application.Abstraction.Result<T>>> action)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await action(actor);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
