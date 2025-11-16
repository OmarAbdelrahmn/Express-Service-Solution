using Application.Admin;
using Application.User;
using Microsoft.AspNetCore.Mvc;



namespace Express_Service.Controllers;
[Route("[controller]")]
[ApiController]
public class AdminController(IAdminService service,IUserService service1) : ControllerBase
{
    private readonly IAdminService service = service;
    private readonly IUserService service1 = service1;

    [HttpGet("")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await service.GetAllUsers();

        return users is not null ?
            Ok(users) :
            BadRequest();
    }

    [HttpGet("change-role")]
    public async Task<IActionResult> ChangeRoles(string UserName, string NewRole)
    {
        var result = await service1.ChangeRoleForUser(UserName, NewRole);

        return result.IsSuccess ? Ok() : result.ToProblem();
    }

    [HttpGet("id/{Id}")]
    public async Task<IActionResult> GetUser(string Id)
    {
        var user = await service.GetUserAsync(Id);

        return user.IsSuccess ?
            Ok(user.Value) :
            user.ToProblem();
    }
    
    [HttpGet("name/{UserName}")]
    public async Task<IActionResult> GetUser2(string UserName)
    {
        var user = await service.GetUser2Async(UserName);

        return user.IsSuccess ?
            Ok(user.Value) :
            user.ToProblem();
    }

    [HttpPut("toggle-status/{UserName}")]
    public async Task<IActionResult> ToggleStatusAsync(string UserName)
    {
        var user = await service.ToggleStatusAsync(UserName);
        return user.IsSuccess ?
            NoContent() :
            user.ToProblem();
    }

 
}
