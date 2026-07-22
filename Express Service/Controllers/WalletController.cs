using Application.Service.Wallet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class WalletController(IWalletService walletService) : ControllerBase
{
    private readonly IWalletService _walletService = walletService;

    // ── GET api/wallet ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns all wallet records including housing name, AR names,
    /// and IqamaNos for both riders (worked + main/original when substitution).
    /// </summary>
    
    [HttpGet("")]
    //[Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _walletService.GetAllAsync(cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    // ── POST api/wallet/import?date=2025-04-01 ───────────────────────────────

    /// <summary>
    /// Imports wallet records from an Excel file (.xlsx).
    /// Required columns: WorkingId, Amount.
    /// The date applies to all rows and is provided as a query string.
    /// Substitutions are resolved automatically (same logic as shift import).
    /// Existing records for the same rider+date are updated (upsert).
    /// </summary>
    
    [HttpPost("import")]
    //[Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> ImportFromExcel(
        [FromQuery] DateOnly date,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".xlsx" and not ".xls")
            return BadRequest(new { error = "File must be an Excel file (.xlsx or .xls)." });

        await using var stream = file.OpenReadStream();

        var result = await _walletService.ImportFromExcelAsync(stream, date, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
}
