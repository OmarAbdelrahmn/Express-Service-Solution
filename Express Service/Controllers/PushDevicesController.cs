using Application.Authentication;
using Application.Contracts.Notifications;
using Application.Service.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/rider-mobile/devices")]
[Authorize(AuthenticationSchemes = RiderMobileTokenProvider.Scheme)]
[EnableRateLimiting("notification-devices")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PushDevicesController(IPushDeviceService service) : ControllerBase
{
    private int RiderId => int.Parse(User.FindFirstValue(RiderMobileTokenProvider.RiderIdClaim)!, System.Globalization.CultureInfo.InvariantCulture);
    private Guid SessionId => Guid.Parse(User.FindFirstValue(RiderMobileTokenProvider.SessionClaim)!);

    [HttpPut("{installationId:guid}")]
    public async Task<IActionResult> Register(Guid installationId, RegisterPushDeviceRequest request, CancellationToken ct)
    {
        var result = await service.RegisterAsync(installationId, RiderId, SessionId, request, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    [HttpDelete("{installationId:guid}")]
    public async Task<IActionResult> Unregister(Guid installationId, CancellationToken ct)
    {
        var result = await service.UnregisterAsync(installationId, RiderId, SessionId, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }
}
