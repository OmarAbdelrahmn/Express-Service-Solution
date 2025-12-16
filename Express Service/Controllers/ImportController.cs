using Application.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ImportController(IImportService service) : ControllerBase
{
    private readonly IImportService service = service;

    [HttpPost("riders")]
    public async Task<IActionResult> ImportEmployeesAndRidersAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded or file is empty" });
        }

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
        {
            return BadRequest(new { error = "File must be Excel format (.xlsx or .xls)" });
        }

        var uploadedBy = "System";

        var result = await service.ImportEmployeesAndRidersAsync(file, uploadedBy);

        if (result.IsSuccess)
        {
            return Ok(new
            {
                success = true,
                data = result.Value,
                summary = new
                {
                    totalRecords = result.Value.TotalRecords,
                    successfulEmployees = result.Value.SuccessfulEmployees,
                    successfulRiders = result.Value.SuccessfulRiders,
                    failedRecords = result.Value.FailedRecords,
                    successRate = result.Value.TotalRecords > 0
                        ? $"{(result.Value.SuccessfulEmployees * 100.0 / result.Value.TotalRecords):F1}%"
                        : "0%"
                }
            });
        }

        return result.ToProblem();
    }

    /// <summary>
    /// Get import template information
    /// </summary>
    [HttpGet("template-info")]
    [AllowAnonymous]
    public IActionResult GetTemplateInfo()
    {
        return Ok(new
        {
            requiredColumns = new[]
            {
                "رقم الإقامة / Iqama Number",
                "الاسم بالعربية / Name AR",
                "الاسم بالإنجليزية / Name EN"
            },
            optionalColumns = new[]
            {
                "تاريخ انتهاء الاقامة ميلادي / Iqama End M",
                "تاريخ انتهاء الاقامة هجري / Iqama End H",
                "رقم الجواز / Passport No",
                "تاريخ انتهاء الجواز / Passport End",
                "الكفيل / Sponsor",
                "رقم الكفيل / Sponsor No",
                "المسمى الوظيفي / Job Title",
                "الجنسية / Country",
                "رقم الجوال / Phone",
                "تاريخ الميلاد / Date Of Birth",
                "الحالة / Status",
                "رقم الآيبان / IBAN",
                "INKSA",
                "معرف العمل / Working ID",
                "مقاس القميص / T-shirt Size",
                "رقم الرخصة / License Number",
                "اسم الشركة / Company Name"
            },
            dateFormats = new
            {
                gregorian = new[] { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd" },
                hijri = "dd/MM/yyyy (Hijri calendar, e.g., 01/04/1447)"
            },
            notes = new[]
            {
                "Column order doesn't matter - columns are matched by name",
                "Missing optional values will use defaults",
                "Duplicate Iqama numbers will update existing employees",
                "Company name must match exactly (case-insensitive)",
                "Hijri dates will be automatically converted to Gregorian",
                "Maximum file size: 10MB"
            }
        });
    }
}
