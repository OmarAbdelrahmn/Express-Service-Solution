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


    [HttpPost("update-working-ids")]
    public async Task<IActionResult> UpdateRiderWorkingIdsAsync(IFormFile file)
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

        var result = await service.UpdateRiderWorkingIdsAsync(file, uploadedBy);

        if (result.IsSuccess)
        {
            return Ok(new
            {
                success = true,
                data = result.Value,
                summary = new
                {
                    totalRecords = result.Value.TotalRecords,
                    successfulUpdates = result.Value.SuccessfulUpdates,
                    failedRecords = result.Value.FailedRecords,
                    iqamaNotFound = result.Value.IqamaNotFound,
                    riderDetailsNotFound = result.Value.RiderDetailsNotFound,
                    successRate = result.Value.TotalRecords > 0
                        ? $"{(result.Value.SuccessfulUpdates * 100.0 / result.Value.TotalRecords):F1}%"
                        : "0%"
                },
                notFoundIqamas = result.Value.NotFoundIqamas
            });
        }

        return result.ToProblem();
    }

    [HttpGet("working-id-template-info")]
    public IActionResult GetWorkingIdTemplateInfo()
    {
        return Ok(new
        {
            requiredColumns = new[]
            {
            "IqamaNumber / رقم الإقامة (Must exist in database)",
            "WorkingId / معرف العمل (New Working ID to assign)"
        },
            columnVariations = new
            {
                iqamaNumber = new[]
                {
                "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
                "رقم الاقامة", "رقم الإقامة", "الاقامة"
            },
                workingId = new[]
                {
                "WorkingId", "Working Id", "Working ID", "WorkingID",
                "معرف العمل", "معرف الشغل", "رقم العمل"
            }
            },
            importBehavior = new
            {
                employeeNotFound = "IqamaNo not found in database - will be skipped and listed in response",
                riderDetailsNotFound = "Employee exists but has no RiderDetails - will be skipped",
                successfulUpdate = "Working ID will be updated and old value will be returned in response",
                transactionSafety = "Each row is processed in its own transaction"
            },
            response = new
            {
                totalRecords = "Total number of rows processed",
                successfulUpdates = "Number of successfully updated Working IDs",
                failedRecords = "Number of records that failed to update",
                iqamaNotFound = "Number of Iqama numbers not found in database",
                riderDetailsNotFound = "Number of employees without RiderDetails",
                notFoundIqamas = "List of all Iqama numbers that were not found",
                results = "Detailed results for each row with old and new Working IDs"
            },
            notes = new[]
            {
            "Column order doesn't matter - matched by name (Arabic or English)",
            "IqamaNo must exist in Employees table",
            "Employee must have associated RiderDetails record",
            "Old Working ID value is returned in response for reference",
            "Rider name (AR & EN) included in response for verification",
            "All not-found Iqama numbers are listed separately",
            "Maximum file size: 10MB",
            "Each update is transactional - failures don't affect other rows"
        },
            exampleExcel = new
            {
                headers = new[] { "IqamaNumber", "WorkingId" },
                sampleData = new[]
                {
                new { IqamaNumber = "2234567890", WorkingId = "WID001" },
                new { IqamaNumber = "2345678901", WorkingId = "WID002" },
                new { IqamaNumber = "2456789012", WorkingId = "WID003" }
            }
            }
        });
    }

    [HttpPost("bulk-import-housing")]
    public async Task<IActionResult> BulkAssignToHousing(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { message = "Only Excel files (.xlsx, .xls) are allowed" });

        var userName = "omar";

        var response = await service.BulkAssignEmployeesToHousingAsync(file, userName);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }


    [HttpPost("deleted-employees")]
    public async Task<IActionResult> ImportDeletedEmployeesAsync(IFormFile file)
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

        var result = await service.ImportDeletedEmployeesAsync(file, uploadedBy);

        if (result.IsSuccess)
        {
            return Ok(new
            {
                success = true,
                data = result.Value,
                summary = new
                {
                    totalRecords = result.Value.TotalRecords,
                    successfulImports = result.Value.SuccessfulImports,
                    failedRecords = result.Value.FailedRecords,
                    duplicateIqamas = result.Value.DuplicateIqamas,
                    successRate = result.Value.TotalRecords > 0
                        ? $"{(result.Value.SuccessfulImports * 100.0 / result.Value.TotalRecords):F1}%"
                        : "0%"
                }
            });
        }

        return result.ToProblem();
    }

    [HttpGet("deleted-employees-template-info")]
    public IActionResult GetDeletedEmployeesTemplateInfo()
    {
        return Ok(new
        {
            requiredColumns = new[]
            {
            "IqamaNumber / رقم الإقامة (MUST be unique)"
        },
            highlyRecommendedColumns = new[]
            {
            "WorkingId / معرف العمل (Present in ~90% of records, defaults to 'N/A' if missing)",
            "NameEN / الاسم بالإنجليزية (Defaults to 'Unknown')",
            "NameAR / الاسم بالعربية (Defaults to 'غير معروف')",
            "CompanyName / اسم الشركة (Used to link to Company table)"
        },
            optionalColumns = new[]
            {
            "IqamaEndM / تاريخ انتهاء الاقامة ميلادي (Defaults to +1 year)",
            "IqamaEndH / تاريخ انتهاء الاقامة هجري (Defaults to +1 year)",
            "PassportNo / رقم الجواز",
            "PassportEnd / تاريخ انتهاء الجواز",
            "Sponsor / الكفيل (Defaults to 'الخدمة السريعة')",
            "JobTitle / المسمى الوظيفي (Defaults to 'سائق دراجة نارية')",
            "Country / الجنسية (Defaults to 'Unknown')",
            "Phone / رقم الجوال (Defaults to '05')",
            "DateOfBirth / تاريخ الميلاد (Defaults to 1990-01-01)",
            "Status / الحالة (Defaults to 'disable')",
            "IBAN / رقم الآيبان",
            "INKSA / في السعودية (Defaults to true)",
            "TshirtSize / مقاس القميص",
            "LicenseNumber / رقم الرخصة"
        },
            importBehavior = new
            {
                duplicateIqamas = "Skipped - IqamaNo must be unique in DeletedEmployees table",
                missingWorkingId = "Defaults to 'N/A' (90% of records have WorkingId)",
                missingCompany = "CompanyId set to null if CompanyName not found",
                missingNames = "NameEN defaults to 'Unknown', NameAR defaults to 'غير معروف'",
                transactionSafety = "Each row processed in its own transaction",
                statusDefault = "Always defaults to 'disable' if not provided"
            },
            useCases = new
            {
                description = "Import historical records of deleted employees/riders",
                workingIdTracking = "Preserves WorkingId history for substitution lookups",
                reportingPurposes = "Maintains deleted employee data for reports and audits",
                dataRecovery = "Allows tracking of previously deleted employee information"
            },
            notes = new[]
            {
            "Only IqamaNo is strictly required - all other fields have defaults",
            "WorkingId is highly recommended as it's present in 90% of records",
            "Column order doesn't matter - columns matched by name (Arabic or English)",
            "Duplicate IqamaNo will be skipped and reported in response",
            "CompanyName must match exactly (case-insensitive) to link to Company table",
            "Maximum file size: 10MB",
            "All dates support multiple formats (dd/MM/yyyy, yyyy-MM-dd, etc.)",
            "Hijri dates automatically converted to Gregorian"
        },
            exampleExcel = new
            {
                headers = new[] { "IqamaNumber", "NameEN", "NameAR", "WorkingId", "CompanyName" },
                sampleData = new[]
                {
                new { IqamaNumber = "2234567890", NameEN = "Ali Ahmed", NameAR = "علي أحمد", WorkingId = "WID001", CompanyName = "Hunger" },
                new { IqamaNumber = "2345678901", NameEN = "Mohammed Ali", NameAR = "محمد علي", WorkingId = "WID002", CompanyName = "Keta" },
                new { IqamaNumber = "2456789012", NameEN = "Ahmed Hassan", NameAR = "أحمد حسن", WorkingId = "N/A", CompanyName = "ToYou" }
            }
            }
        });
    }
}