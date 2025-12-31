using Application.Admin;
using Application.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Protocol;



namespace Express_Service.Controllers;
[Route("api/[controller]")]
[ApiController]
public class AdminController(IAdminService service,IUserService service1) : ControllerBase
{
    private readonly IAdminService service = service;
    private readonly IUserService service1 = service1;

    [HttpGet("users")]
    [Authorize(Roles = "Master")]
    [ResponseCache(Duration = 300)]

    public async Task<IActionResult> GetUsers()
    {
        var users = await service.GetAllUsers();

        return users is not null ?
            Ok(users) :
            BadRequest();
    }

    [HttpPost("users/role")]
    [Authorize(Roles = "Master")]
    [ResponseCache(Duration = 300)]

    public async Task<IActionResult> ChangeRoles([FromBody] Rer request)
    {
        var result = await service1.ChangeRoleForUser(request.UserName, request.NewRole);

        return result.IsSuccess ? Ok(new Re("Role updated successfully")) : result.ToProblem();
    }

    [HttpGet("users/id/{Id}")]
    [Authorize(Roles = "Master")]
    [ResponseCache(Duration = 300)]

    public async Task<IActionResult> GetUser(string Id)
    {
        var user = await service.GetUserAsync(Id);

        return user.IsSuccess ?
            Ok(user.Value) :
            user.ToProblem();
    }
    
    [HttpGet("users/name/{UserName}")]
    [Authorize(Roles = "Master")]
    [ResponseCache(Duration = 300)]

    public async Task<IActionResult> GetUser2(string UserName)
    {
        var user = await service.GetUser2Async(UserName);

        return user.IsSuccess ?
            Ok(user.Value) :
            user.ToProblem();
    }

    [HttpPut("users/{UserName}/toggle-status")]
    [Authorize(Roles = "Master")]
    [ResponseCache(Duration = 300)]

    public async Task<IActionResult> ToggleStatusAsync(string UserName)
    {
        var user = await service.ToggleStatusAsync(UserName);
        return user.IsSuccess ?
            NoContent() :
            user.ToProblem();
    }

 
}


public record Rer(string UserName,string NewRole);