using Application.Contracts.Roles;
using Application.Roles;
using Express_Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;



namespace SurvayBasket.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class RolesController(IRoleService roleService) : ControllerBase
{
    private readonly IRoleService roleService = roleService;

    [HttpGet("")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> GetAllRoles()
    {
        var response = await roleService.GetRolesAsync();

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();

    }

    [HttpPut("")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> Updaterole(RoleRequest request)
    {
        var response = await roleService.UpdateRoleAsync(request);

        return response.IsSuccess ?
            NoContent() :
            response.ToProblem();
    }


    [HttpPut("toggle-status")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> ToggleStatus(string RoleName)
    {
        var response = await roleService.ToggleStatusAsync(RoleName);

        return response.IsSuccess ?
            NoContent() :
            response.ToProblem();

    }

}
