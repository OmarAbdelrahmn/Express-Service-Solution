using Application.Authentication;
using Application.Contracts.RiderMobile;
using Application.Service.RiderMobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/rider-mobile")]
[Authorize(AuthenticationSchemes = RiderMobileTokenProvider.Scheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RiderMobileController(IRiderMobileService service) : ControllerBase
{
    // All private resources use signed identity claims; no rider ID is accepted from the client.
    private int RiderId => int.Parse(User.FindFirstValue(RiderMobileTokenProvider.RiderIdClaim)!, System.Globalization.CultureInfo.InvariantCulture);
    private Guid SessionId => Guid.Parse(User.FindFirstValue(RiderMobileTokenProvider.SessionClaim)!);
    private string Language => User.FindFirstValue(RiderMobileTokenProvider.LanguageClaim)!;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(RiderMobileLoginRequest request, CancellationToken ct)
    {
        var result = await service.LoginAsync(request, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await service.GetProfileAsync(RiderId, Language, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var result = await service.GetDashboardAsync(RiderId, Language, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("performance")]
    public async Task<IActionResult> Performance([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var result = await service.GetMonthAsync(RiderId, Language, year, month, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("shifts")]
    public async Task<IActionResult> Shifts([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var result = await service.GetMonthAsync(RiderId, Language, year, month, ct);
        return result.IsSuccess ? Ok(new { result.Value.Language, result.Value.Year, result.Value.Month,
            result.Value.AsOfDate, result.Value.Attendance }) : result.ToProblem();
    }

    [HttpPut("language")]
    public async Task<IActionResult> SetLanguage(RiderMobileLanguageRequest request, CancellationToken ct)
    {
        var result = await service.SetLanguageAsync(SessionId, RiderId, request.Language, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var result = await service.LogoutAsync(SessionId, RiderId, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }
}
