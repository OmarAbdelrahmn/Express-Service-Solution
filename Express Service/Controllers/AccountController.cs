using Application.Contracts.Users;
using Application.Extensions;
using Application.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace Express_Service.Controllers;
[Route("me")]
[ApiController]
//[Authorize]
public class AccountController(IUserService service) : ControllerBase
{
    private readonly IUserService service = service;

    //[HttpGet("")]
    //public async Task<IActionResult> ShowUserProfile(string userid)
    //{
    //    var result = await service.GetUserProfile(userid);

    //    return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    //}

    //[HttpPut("info")]
    //public async Task<IActionResult> UpdateUserProfile([FromBody] UpdateUserProfileRequest request , string userid)
    //{
    //    var result = await service.UpdateUserProfile(userid, request);

    //    return NoContent();
    //}

    //[HttpPut("change-passord")]
    //public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request , string userid)
    //{
    //    var result = await service.ChangePassword(userid, request);

    //    return result.IsSuccess ? NoContent(new sa() : result.ToProblem();
    //}

    public class Resu(string massage)
    {
        public string Massage { get; set; } = massage;
    }

    [HttpGet("")]
    public async Task<IActionResult> ShowUserProfile()
    {
        var result = await service.GetUserProfile(User.GetUserId()!);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("info")]
    public async Task<IActionResult> UpdateUserProfile([FromBody] UpdateUserProfileRequest request)
    {
        var result = await service.UpdateUserProfile(User.GetUserId()!, request);

        return result.IsSuccess ? Ok(new Resu("profile Updated successfully")) : result.ToProblem();
    }

    [HttpPut("change-passord")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var result = await service.ChangePassword(User.GetUserId()!, request);

        return result.IsSuccess ? Ok(new Resu("Password Changed Successfully")) : result.ToProblem();
    }
}
