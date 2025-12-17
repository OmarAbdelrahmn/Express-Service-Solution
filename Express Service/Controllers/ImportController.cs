using Application.Service;
using k8s.Models;
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


    [HttpGet("template-info")]
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


    [HttpPost("vehicles")]
    public async Task<IActionResult> ImportVehiclesAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded or file is empty" });
        }

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
        {
            return BadRequest(new { error = "File must be Excel format (.xlsx or .xls)" });
        }

        var uploadedBy = User?.Identity?.Name ?? "System";

        var result = await service.ImportVehiclesAsync(file, uploadedBy);

        if (result.IsSuccess)
        {
            return Ok(new
            {
                success = true,
                data = result.Value,
                summary = new
                {
                    totalRecords = result.Value.TotalRecords,
                    successfulVehicles = result.Value.SuccessfulVehicles,
                    updatedVehicles = result.Value.UpdatedVehicles,
                    assignedToRiders = result.Value.AssignedToRiders,
                    failedRecords = result.Value.FailedRecords,
                    successRate = result.Value.TotalRecords > 0
                        ? $"{((result.Value.SuccessfulVehicles + result.Value.UpdatedVehicles) * 100.0 / result.Value.TotalRecords):F1}%"
                        : "0%"
                }
            });
        }

        return result.ToProblem();
    }


    [HttpGet("vehicle-template-info")]
    public IActionResult GetvehicleTemplateInfo()
    {
        return Ok(new
        {
            requiredColumns = new[]
            {
                "VehicleNumber / رقم المركبة (Primary Key - Must be unique)",
                "SerialNumber / الرقم التسلسلي (Must be unique)",
                "PlateNumberA / رقم اللوحة أ (Arabic - Must be unique)",
                "PlateNumberE / رقم اللوحة E (English - Must be unique)"
            },
            optionalColumns = new[]
            {
                "VehicleType / نوع المركبة (Default: Motorcycle)",
                "Manufacturer / الصانع (Default: Unknown)",
                "ManufactureYear / سنة الصنع (Default: Current year)",
                "LicenseExpiryDate / تاريخ انتهاء الرخصة (Default: +1 year)",
                "Location / الموقع (Default: Unknown)",
                "Status / الحالة (Default: Available | Options: Available, Problem, Stolen, BreakUp)",
                "RiderIqamaNo / رقم اقامة السائق (Optional - assigns vehicle to rider)"
            },
            automaticDefaults = new
            {
                ownerName = "الخدمة السريعة",
                ownerId = 7010962889,
                status = "Available (if not specified)"
            },
            dateFormats = new
            {
                licenseExpiryDate = new[] { "dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd" }
            },
            importBehavior = new
            {
                newVehicles = "Creates new vehicle records with default values",
                existingVehicles = "Updates existing vehicles, tracks changes in history",
                statusChanges = "Automatically recorded in RiderVehicleStatus table",
                riderAssignment = "If RiderIqamaNo provided, assigns vehicle and creates history",
                conflicts = "Prevents duplicate Serial/Plate numbers across different vehicles"
            },
            statusOptions = new[]
            {
                "Available - Vehicle can be taken by riders",
                "Problem - Vehicle has issues",
                "Stolen - Vehicle reported stolen",
                "BreakUp - Vehicle is broken/decommissioned"
            },
            notes = new[]
            {
                "Column order doesn't matter - matched by name",
                "VehicleNumber, SerialNumber, PlateNumberA, PlateNumberE must be unique",
                "Duplicate VehicleNumber will update existing vehicle",
                "Status changes are automatically tracked in history",
                "If RiderIqamaNo provided, vehicle will be assigned to that rider",
                "Owner defaults to 'الخدمة السريعة' (ID: 7010962889)",
                "Maximum file size: 10MB"
            }
        });
    }

}
