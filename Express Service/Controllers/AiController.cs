using Application.Extensions;       // for User.GetUserId()
using Application.Service.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/ai")]
[ApiController]
[Authorize]  // uncomment when ready
public class AiController(IGeminiService geminiService) : ControllerBase
{
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest request)
    {
        var response = await geminiService.ChatAsync(request, User.GetUserId()!);
        return Ok(response);
    }
}