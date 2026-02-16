using Application.Contracts.Auth;
using Application.Service.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(IAuthService service) : ControllerBase
{
    private readonly IAuthService service = service;

    [HttpPost("register")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await service.RegisterAsync(request);

        return response.IsSuccess ?
            Ok(new Resu("Done please try to Login")) :
            response.ToProblem();
    }

    [HttpPost("register/admin")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> AdminRegister([FromBody] RegisterRequest request)
    {
        var response = await service.AdminRegisterAsync(request);

        return response.IsSuccess ?
            Ok(new Resu("Done please try to Login")) :
            response.ToProblem();
    }

    [HttpPost("register/master")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> MasterRegister([FromBody] RegisterRequest request)
    {
        var response = await service.MasterRegisterAsync(request);

        return response.IsSuccess ?
            Ok(new Resu("Done please try to Login")) :
            response.ToProblem();
    }

    [HttpPost("login")]

    public async Task<IActionResult> login([FromBody] AuthRequest request)
    {
        var response = await service.SingInAsync(request);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    public class Resu(string massage)
    {
        public string Massage { get; set; } = massage;
    }
}