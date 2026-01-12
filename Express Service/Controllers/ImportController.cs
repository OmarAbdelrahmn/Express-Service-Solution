using Application.Service;
using Application.Service.Backgroundimports;
using k8s.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using static Application.Service.ImportService;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ImportController(IImportService service , IBackgroundImportService service1) : ControllerBase
{
    private readonly IImportService service = service;
    private readonly IBackgroundImportService service1 = service1;

    [HttpPost("rider-shifts/start")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 524288000)] // 500MB
    public async Task<IActionResult> StartRiderShiftImport(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { error = "File must be Excel format (.xlsx or .xls)" });

        var uploadedBy = "omar";
        var jobId = await service1.StartRiderShiftImportAsync(file, uploadedBy);

        return Ok(new
        {
            jobId,
            message = "Rider shift import started in background",
            estimatedTime = "Large files (200K+ rows) may take 10-30 minutes",
            statusUrl = $"/api/import/rider-shifts/status/{jobId}",
            resultUrl = $"/api/import/rider-shifts/result/{jobId}"
        });
    }

    /// <summary>
    /// Check rider shift import job progress - Poll every 5-10 seconds for large imports
    /// </summary>
    [HttpGet("rider-shifts/status/{jobId}")]
    public IActionResult GetRiderShiftImportStatus(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
            return NotFound(new { error = "Job not found or expired" });

        return Ok(new
        {
            status.JobId,
            status.Status,
            status.Progress,
            status.ProcessedRows,
            status.TotalRows,
            status.StartTime,
            status.EndTime,
            status.ElapsedTime,
            status.ErrorMessage,
            estimatedTimeRemaining = status.Progress > 0 && status.Progress < 100
                ? CalculateEstimatedTime(status)
                : null
        });
    }

    /// <summary>
    /// Get complete rider shift import results after job completion
    /// </summary>
    [HttpGet("rider-shifts/result/{jobId}")]
    public IActionResult GetRiderShiftImportResult(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
        {
            return NotFound(new { error = "Job not found or expired" });
        }

        if (status.Status != "Completed")
        {
            return BadRequest(new
            {
                error = $"Job is still {status.Status}",
                status = status
            });
        }

        var result = service1.GetRiderShiftImportResult(jobId);

        if (result == null)
        {
            return NotFound(new
            {
                error = "Result not available",
                status = status,
                message = "Job completed but result file is missing. This may indicate a storage issue."
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get summary of rider shift import (lighter response without all details)
    /// </summary>
    [HttpGet("rider-shifts/summary/{jobId}")]
    public IActionResult GetRiderShiftImportSummary(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
            return NotFound(new { error = "Job not found or expired" });

        if (status.Status != "Completed")
            return BadRequest(new { error = $"Job is still {status.Status}", status });

        var result = service1.GetRiderShiftImportResult(jobId);

        if (result == null)
            return NotFound(new { error = "Result not available" });

        return Ok(new
        {
            jobId,
            status = status.Status,
            totalRecordsProcessed = result.TotalRecordsProcessed,
            successfulShifts = result.SuccessfulShifts,
            updatedShifts = result.UpdatedShifts,
            skippedDuplicates = result.SkippedDuplicates,
            workingIdNotFound = result.WorkingIdNotFound,
            housingNotFound = result.HousingNotFound,
            validationErrors = result.ValidationErrors,
            processedAt = result.ProcessedAt,
            elapsedTime = status.ElapsedTime,
            successRate = result.TotalRecordsProcessed > 0
                ? $"{((result.SuccessfulShifts + result.UpdatedShifts) * 100.0 / result.TotalRecordsProcessed):F1}%"
                : "0%",
            detailsCount = result.Details.Count,
            hasDetails = true,
            detailsUrl = $"/api/import/rider-shifts/result/{jobId}"
        });
    }

    /// <summary>
    /// Get paginated rider shift import details
    /// </summary>
    [HttpGet("rider-shifts/details/{jobId}")]
    public IActionResult GetRiderShiftImportDetails(
        string jobId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? status = null)
    {
        var result = service1.GetRiderShiftImportResult(jobId);

        if (result == null)
            return NotFound(new { error = "Result not available" });

        var filteredDetails = result.Details.AsEnumerable();

        // Filter by status if provided
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<ImportStatus>(status, true, out var statusEnum))
            {
                filteredDetails = filteredDetails.Where(d => d.Status == statusEnum);
            }
        }

        var detailsList = filteredDetails.ToList();
        var totalPages = (int)Math.Ceiling(detailsList.Count / (double)pageSize);
        var details = detailsList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            page,
            pageSize,
            totalPages,
            totalRecords = detailsList.Count,
            filter = status,
            details
        });
    }

    /// <summary>
    /// Cancel a running rider shift import job
    /// </summary>
    [HttpPost("rider-shifts/cancel/{jobId}")]
    public IActionResult CancelRiderShiftImport(string jobId)
    {
        var cancelled = service1.CancelJob(jobId);

        if (!cancelled)
            return NotFound(new { error = "Job not found or already completed" });

        return Ok(new { message = "Rider shift import cancellation requested" });
    }


    // Helper method for estimated time calculation
    private string? CalculateEstimatedTime(ImportJobStatus status)
    {
        if (status.ElapsedTime == null || status.ProcessedRows == 0)
            return null;

        var rowsRemaining = status.TotalRows - status.ProcessedRows;
        var timePerRow = status.ElapsedTime.Value.TotalSeconds / status.ProcessedRows;
        var secondsRemaining = timePerRow * rowsRemaining;

        if (secondsRemaining < 60)
            return $"{secondsRemaining:F0} seconds";
        else if (secondsRemaining < 3600)
            return $"{(secondsRemaining / 60):F0} minutes";
        else
            return $"{(secondsRemaining / 3600):F1} hours";
    }

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




    [HttpPost("vehicle-assignments")]
    public async Task<IActionResult> ImportVehicleAssignmentsAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded or file is empty" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { error = "File must be Excel format (.xlsx or .xls)" });

        var uploadedBy = User?.Identity?.Name ?? "System";

        var result = await service.ImportVehicleAssignmentsAsync(file, uploadedBy);

        if (result.IsSuccess)
        {
            return Ok(new
            {
                success = true,
                data = result.Value,
                summary = new
                {
                    totalRecords = result.Value.TotalRecords,
                    successfulAssignments = result.Value.SuccessfulAssignments,
                    employeesConvertedToRiders = result.Value.EmployeesConvertedToRiders,
                    vehiclesReassigned = result.Value.VehicleUnavailable, // Now represents reassignments
                    failedRecords = result.Value.FailedRecords,
                    employeeNotFound = result.Value.EmployeeNotFound,
                    vehicleNotFound = result.Value.VehicleNotFound,
                    successRate = result.Value.TotalRecords > 0
                        ? $"{(result.Value.SuccessfulAssignments * 100.0 / result.Value.TotalRecords):F1}%"
                        : "0%"
                },
                systemSync = new
                {
                    message = "System synchronized with Excel sheet",
                    vehiclesInExcel = result.Value.SuccessfulAssignments,
                    vehiclesReassigned = result.Value.VehicleUnavailable,
                    note = "Vehicles not in Excel were automatically returned to available status"
                }
            });
        }

        return result.ToProblem();
    }

    [HttpGet("vehicle-assignment-template-info")]
    public IActionResult GetVehicleAssignmentTemplateInfo()
    {
        return Ok(new
        {
            requiredColumns = new[]
            {
            "IqamaNumber / رقم الإقامة (Employee Iqama - spaces will be removed)",
            "PlateNumberA / رقم اللوحة (Arabic Plate Number - spaces will be removed)"
        },
            optionalColumns = new[]
            {
            "Permission / التصريح (Permission type - defaults to 'تصريح عام')",
            "PermissionStartDate / تاريخ بداية التصريح (Format: dd/MM/yyyy or yyyy-MM-dd)",
            "PermissionEndDate / تاريخ نهاية التصريح (Format: dd/MM/yyyy or yyyy-MM-dd)"
        },
            criticalBehavior = new
            {
                systemSync = "The system will MATCH the Excel sheet exactly",
                reassignment = "Vehicles with different riders will be reassigned automatically",
                autoReturn = "Vehicles NOT in Excel will be auto-returned to available status",
                statusClearing = "All vehicle problems/statuses will be cleared during assignment",
                history = "Every change is tracked in RiderVehicleStatus history table"
            },
            specialBehavior = new
            {
                autoConversion = "Employees without RiderDetails will be automatically converted to riders",
                spaceRemoval = "All spaces removed from IqamaNo and PlateNumberA for matching",
                housingLocation = "Vehicle location automatically updated to employee's housing name",
                trafficPermission = "If permission contains 'مرور', defaults to 30-day period",
                replaceVehicle = "If rider already has a vehicle, it will be returned and replaced"
            },
            examples = new
            {
                scenario1 = new
                {
                    name = "Reassignment",
                    description = "Vehicle ABC-123 is with Rider A. Excel shows it should be with Rider B.",
                    result = "Vehicle returned from Rider A (with history), then assigned to Rider B (with new history)"
                },
                scenario2 = new
                {
                    name = "Auto-Return",
                    description = "Vehicle XYZ-789 is with Rider C. It's NOT in the Excel.",
                    result = "Vehicle automatically returned from Rider C and marked as available"
                },
                scenario3 = new
                {
                    name = "Status Clearing",
                    description = "Vehicle DEF-456 marked as 'Problem'. Excel shows it should be with Rider D.",
                    result = "Problem status cleared, vehicle assigned to Rider D with fresh history"
                }
            },
            historyTracking = new
            {
                takenRecords = "Created when vehicle assigned to rider (IsActive = true)",
                returnedRecords = "Created when vehicle returned (IsActive = false)",
                reassignmentRecords = "Both return and new taken records created",
                permission = "Tracked with start/end dates for each assignment",
                reason = "Every status change includes reason and who made it"
            },
            notes = new[]
            {
            "⚠️ SYSTEM WILL MATCH EXCEL EXACTLY - Vehicles not in Excel will be auto-returned",
            "Spaces automatically removed from IqamaNo and PlateNumberA",
            "Employee must exist - will not create new employees",
            "Vehicle must exist - will not create new vehicles",
            "Employees without RiderDetails converted automatically",
            "Vehicle location updated to employee's housing name",
            "Permission containing 'مرور' gets special handling",
            "All assignments and returns tracked in RiderVehicleStatus history",
            "Problem/Stolen/BreakUp statuses cleared during assignment",
            "Each row processed independently in transactions",
            "Maximum file size: 10MB"
        },
            exampleExcel = new
            {
                headers = new[]
                {
                "IqamaNumber",
                "PlateNumberA",
                "Permission",
                "PermissionStartDate",
                "PermissionEndDate"
            },
                sampleData = new[]
                {
                new
                {
                    IqamaNumber = "2234567890",
                    PlateNumberA = "أ ب ج 1234",
                    Permission = "تصريح عمل",
                    PermissionStartDate = "01/01/2025",
                    PermissionEndDate = "31/12/2025"
                },
                new
                {
                    IqamaNumber = "2345678901",
                    PlateNumberA = "د هـ و 5678",
                    Permission = "تصريح مرور",
                    PermissionStartDate = "15/01/2025",
                    PermissionEndDate = "15/02/2025"
                }
            }
            }
        });
    }

    [HttpPost("vehicle-checker")]
    public async Task<IActionResult> ImportVeicleAssignmentsAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded or file is empty" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { error = "File must be Excel format (.xlsx or .xls)" });



        var result = await service.CheckVehicleUsageFromExcelAsync(file, "omar");

        return result.IsFailure ?
        result.ToProblem() : Ok(result.Value);

    }


    [HttpPost("bulk-ifo")]
    public async Task<IActionResult> VerifyRidersFromExcelAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded or file is empty" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { error = "File must be Excel format (.xlsx or .xls)" });



        var result = await service.VerifyRidersFromExcelAsync(file, "omar");

        return result.IsFailure ?
        result.ToProblem() : Ok(result.Value);

    }


    /// <summary>
    /// Start rider verification in background - Returns immediately with Job ID
    /// </summary>
    [HttpPost("verify-riders/start")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 524288000)]
    public async Task<IActionResult> StartRiderVerification(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        var uploadedBy = "omar";
        var jobId = await service1.StartRiderVerificationAsync(file, uploadedBy);

        return Ok(new
        {
            jobId,
            message = "Verification started in background",
            statusUrl = $"/api/import/verify-riders/status/{jobId}",
            resultUrl = $"/api/import/verify-riders/result/{jobId}"
        });
    }

    /// <summary>
    /// Check job progress - Poll this endpoint every 2-5 seconds
    /// </summary>
    [HttpGet("verify-riders/status/{jobId}")]
    public IActionResult GetVerificationStatus(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
            return NotFound(new { error = "Job not found or expired" });

        return Ok(status);
    }


    /// <summary>
    /// OLD SYNCHRONOUS METHOD - Will timeout on large files!
    /// Use /start endpoint instead for files with 10K+ rows
    /// </summary>
    [HttpPost("verify-riders")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 524288000)]
    [RequestTimeout(600000)] // 10 minutes
    public async Task<IActionResult> VerifyRidersSync(IFormFile file)
    {
        var uploadedBy = User.Identity?.Name ?? "Unknown";
        var result = await service.VerifyRidersFromExcelAsync(file, uploadedBy);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("verify-riders/cancel/{jobId}")]
    public IActionResult CancelVerification(string jobId)
    {
        var cancelled = service1.CancelJob(jobId);

        if (!cancelled)
            return NotFound(new { error = "Job not found or already completed" });

        return Ok(new { message = "Job cancellation requested" });
    }

    [HttpGet("verify-riders/result/{jobId}")]
    public IActionResult GetVerificationResult(string jobId)
    {

        var status = service1.GetJobStatus(jobId);

        if (status == null)
        {
            return NotFound(new { error = "Job not found or expired" });
        }


        if (status.Status != "Completed")
        {
            return BadRequest(new
            {
                error = $"Job is still {status.Status}",
                status = status
            });
        }

        var result = service1.GetJobResult(jobId);

        if (result == null)
        {
            return NotFound(new
            {
                error = "Result not available",
                status = status,
                message = "Job completed but result file is missing. This may indicate a storage issue."
            });
        }



        return Ok(result);
    }

    [HttpGet("verify-riders/summary/{jobId}")]
    public IActionResult GetVerificationSummary(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
            return NotFound(new { error = "Job not found or expired" });

        if (status.Status != "Completed")
            return BadRequest(new { error = $"Job is still {status.Status}", status });

        var result = service1.GetJobResult(jobId);

        if (result == null)
            return NotFound(new { error = "Result not available" });

        // Return summary without all details (lighter response)
        return Ok(new
        {
            jobId,
            status = status.Status,
            totalRecordsProcessed = result.TotalRecordsProcessed,
            fullyMatched = result.FullyMatched,
            workingIdFoundNameMismatch = result.WorkingIdFoundNameMismatch,
            nameFoundWorkingIdMismatch = result.NameFoundWorkingIdMismatch,
            completelyNotFound = result.CompletelyNotFound,
            errorRecords = result.ErrorRecords,
            processedAt = result.ProcessedAt,
            elapsedTime = status.ElapsedTime,
            // Only include error details, not all matching details
            detailsCount = result.Details.Count,
            hasDetails = true,
            detailsUrl = $"/api/import/verify-riders/result/{jobId}"
        });
    }

    [HttpGet("verify-riders/details/{jobId}")]
    public IActionResult GetVerificationDetails(
        string jobId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var result = service1.GetJobResult(jobId);

        if (result == null)
            return NotFound(new { error = "Result not available" });

        var totalPages = (int)Math.Ceiling(result.Details.Count / (double)pageSize);
        var details = result.Details
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            page,
            pageSize,
            totalPages,
            totalRecords = result.Details.Count,
            details
        });
    }


    // Add these endpoints to ImportController.cs

    /// <summary>
    /// Start WorkingId sync in background - Returns immediately with Job ID
    /// Syncs WorkingIds from Excel: adds to history if different, creates RiderDetails if missing
    /// </summary>
    [HttpPost("sync-working-ids/start")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 524288000)]
    public async Task<IActionResult> StartWorkingIdSync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { error = "File must be Excel format (.xlsx or .xls)" });

        var uploadedBy = User?.Identity?.Name ?? "System";
        var jobId = await service1.StartWorkingIdSyncAsync(file, uploadedBy);

        return Ok(new
        {
            jobId,
            message = "WorkingId sync started in background",
            statusUrl = $"/api/import/sync-working-ids/status/{jobId}",
            resultUrl = $"/api/import/sync-working-ids/result/{jobId}"
        });
    }

    /// <summary>
    /// Check WorkingId sync job progress - Poll this endpoint every 2-5 seconds
    /// </summary>
    [HttpGet("sync-working-ids/status/{jobId}")]
    public IActionResult GetWorkingIdSyncStatus(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
            return NotFound(new { error = "Job not found or expired" });

        return Ok(status);
    }

    /// <summary>
    /// Get complete WorkingId sync results after job completion
    /// </summary>
    [HttpGet("sync-working-ids/result/{jobId}")]
    public IActionResult GetWorkingIdSyncResult(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
        {
            return NotFound(new { error = "Job not found or expired" });
        }

        if (status.Status != "Completed")
        {
            return BadRequest(new
            {
                error = $"Job is still {status.Status}",
                status = status
            });
        }

        var result = service1.GetWorkingIdSyncResult(jobId);

        if (result == null)
        {
            return NotFound(new
            {
                error = "Result not available",
                status = status,
                message = "Job completed but result file is missing. This may indicate a storage issue."
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get summary of WorkingId sync results (lighter response without all details)
    /// </summary>
    [HttpGet("sync-working-ids/summary/{jobId}")]
    public IActionResult GetWorkingIdSyncSummary(string jobId)
    {
        var status = service1.GetJobStatus(jobId);

        if (status == null)
            return NotFound(new { error = "Job not found or expired" });

        if (status.Status != "Completed")
            return BadRequest(new { error = $"Job is still {status.Status}", status });

        var result = service1.GetWorkingIdSyncResult(jobId);

        if (result == null)
            return NotFound(new { error = "Result not available" });

        return Ok(new
        {
            jobId,
            status = status.Status,
            totalRecordsProcessed = result.TotalRecordsProcessed,
            workingIdHistoriesAdded = result.WorkingIdHistoriesAdded,
            riderDetailsCreated = result.RiderDetailsCreated,
            alreadyCorrect = result.AlreadyCorrect,
            duplicatesSkipped = result.DuplicatesSkipped,
            nameNotFound = result.NameNotFound,
            errorRecords = result.ErrorRecords,
            processedAt = result.ProcessedAt,
            elapsedTime = status.ElapsedTime,
            detailsCount = result.Details.Count,
            hasDetails = true,
            detailsUrl = $"/api/import/sync-working-ids/result/{jobId}"
        });
    }

    /// <summary>
    /// Get paginated WorkingId sync details
    /// </summary>
    [HttpGet("sync-working-ids/details/{jobId}")]
    public IActionResult GetWorkingIdSyncDetails(
        string jobId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var result = service1.GetWorkingIdSyncResult(jobId);

        if (result == null)
            return NotFound(new { error = "Result not available" });

        var totalPages = (int)Math.Ceiling(result.Details.Count / (double)pageSize);
        var details = result.Details
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            page,
            pageSize,
            totalPages,
            totalRecords = result.Details.Count,
            details
        });
    }

    /// <summary>
    /// Cancel a running WorkingId sync job
    /// </summary>
    [HttpPost("sync-working-ids/cancel/{jobId}")]
    public IActionResult CancelWorkingIdSync(string jobId)
    {
        var cancelled = service1.CancelJob(jobId);

        if (!cancelled)
            return NotFound(new { error = "Job not found or already completed" });

        return Ok(new { message = "WorkingId sync job cancellation requested" });
    }

  
}
