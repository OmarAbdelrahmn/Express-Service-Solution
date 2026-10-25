using Application.Contracts.Notifications;
using Application.Service.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[EnableRateLimiting("notification-staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotificationsController(INotificationService service) : ControllerBase
{
    [HttpPost("riders/{riderId:int}/notifications")]
    [Authorize(Policy = "Notifications.Send")]
    public Task<IActionResult> Send(int riderId, [FromHeader(Name = "Idempotency-Key")] Guid key,
        SendNotificationRequest request, CancellationToken ct) => Queue(riderId, key, request, ct);

    [HttpPost("notifications/broadcasts")]
    [Authorize(Policy = "Notifications.Broadcast")]
    public Task<IActionResult> Broadcast([FromHeader(Name = "Idempotency-Key")] Guid key,
        SendNotificationRequest request, CancellationToken ct) => Queue(null, key, request, ct);

    private async Task<IActionResult> Queue(int? riderId, Guid key, SendNotificationRequest request, CancellationToken ct)
    {
        var result = await service.QueueAsync(User, riderId, key, request, ct);
        return result.IsSuccess ? AcceptedAtAction(nameof(Status), new { requestId = result.Value.RequestId }, result.Value) : result.ToProblem();
    }

    [HttpGet("notifications/{requestId:guid}")]
    [Authorize(Policy = "Notifications.Read")]
    public async Task<IActionResult> Status(Guid requestId, CancellationToken ct)
    {
        var result = await service.StatusAsync(User, requestId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
