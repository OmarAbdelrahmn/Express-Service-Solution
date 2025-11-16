using Application.Admin;
using Microsoft.AspNetCore.Mvc;



namespace Express_Service.Controllers;
[Route("[controller]")]
[ApiController]
public class AdminController(IAdminService service) : ControllerBase
{
    private readonly IAdminService service = service;

    [HttpGet("")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await service.GetAllUsers();

        return users is not null ?
            Ok(users) :
            BadRequest();
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

    [HttpPut("toggle-status/{UserId}")]
    public async Task<IActionResult> ToggleStatusAsync(string UserId)
    {
        var user = await service.ToggleStatusAsync(UserId);
        return user.IsSuccess ?
            NoContent() :
            user.ToProblem();
    }

 
}
