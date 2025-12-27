using Application.Abstraction;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Application.Service;

public class ImportService(ApplicationDbcontext dbcontext) : IImportService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<DeletedEmployeeImportResponse>> ImportDeletedEmployeesAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<DeletedEmployeeImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<DeletedEmployeeImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<DeletedEmployeeImportRowResult>();
        var errors = new List<string>();
        int successfulImports = 0;
        int failedRecords = 0;
        int duplicateIqamas = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<DeletedEmployeeImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindDeletedEmployeeHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<DeletedEmployeeImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildDeletedEmployeeColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<DeletedEmployeeImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            // Load company lookup dictionary
            var companies = await _dbcontext.Companies
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Name.Trim().ToLower(), c => c.Id);

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseDeletedEmployeeRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new DeletedEmployeeImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            rowData.NameEN,
                            rowData.NameAR,
                            rowData.WorkingId,
                            rowData.CompanyName,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Check if already exists in DeletedEmployees
                    var existingDeleted = await _dbcontext.DeletedEmployees
                        .AnyAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (existingDeleted)
                    {
                        duplicateIqamas++;
                        failedRecords++;
                        results.Add(new DeletedEmployeeImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NameEN,
                            rowData.NameAR,
                            rowData.WorkingId,
                            rowData.CompanyName,
                            warnings,
                            "Deleted employee with this Iqama already exists"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Resolve CompanyId if CompanyName provided
                    int? companyId = null;
                    if (!string.IsNullOrWhiteSpace(rowData.CompanyName))
                    {
                        if (companies.TryGetValue(rowData.CompanyName.Trim().ToLower(), out int cId))
                        {
                            companyId = cId;
                        }
                        else
                        {
                            warnings.Add($"Company '{rowData.CompanyName}' not found - CompanyId set to null");
                        }
                    }

                    // Create DeletedEmployee record
                    var deletedEmployee = new DeletedEmployees
                    {
                        IqamaNo = rowData.IqamaNo!.Value,
                        NameEN = rowData.NameEN ?? "Unknown",
                        NameAR = rowData.NameAR ?? "غير معروف",
                        IqamaEndM = rowData.IqamaEndM ?? DateOnly.FromDateTime(DateTime.Now.AddYears(1)),
                        IqamaEndH = rowData.IqamaEndH ?? DateOnly.FromDateTime(DateTime.Now.AddYears(1)),
                        PassportNo = rowData.PassportNo,
                        PassportEnd = rowData.PassportEnd,
                        Sponsor = rowData.Sponsor ?? "الخدمة السريعة",
                        JobTitle = rowData.JobTitle ?? "سائق دراجة نارية",
                        Country = rowData.Country ?? "Unknown",
                        Phone = rowData.Phone ?? "05",
                        DateOfBirth = rowData.DateOfBirth ?? DateTime.Parse("1990-01-01"),
                        Status = rowData.Status ?? "disable",
                        AcountStatus = rowData.AcountStatus ?? "قيد التشغيل",
                        IBAN = rowData.IBAN,
                        INKSA = rowData.INKSA,
                        WorkingId = rowData.WorkingId ?? "N/A",
                        TshirtSize = rowData.TshirtSize,
                        LicenseNumber = rowData.LicenseNumber,
                        CompanyId = companyId,
                        HousingId = null,
                        VehicleId = null,
                        CreatedAt = DateTime.UtcNow.AddHours(3)
                    };

                    await _dbcontext.DeletedEmployees.AddAsync(deletedEmployee);
                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulImports++;
                    results.Add(new DeletedEmployeeImportRowResult(
                        rowNumber,
                        true,
                        deletedEmployee.IqamaNo.ToString(),
                        deletedEmployee.NameEN,
                        deletedEmployee.NameAR,
                        deletedEmployee.WorkingId,
                        rowData.CompanyName,
                        warnings,
                        null
                    ));

                    if (string.IsNullOrWhiteSpace(rowData.WorkingId))
                    {
                        warnings.Add("WorkingId not provided - using default 'N/A'");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new DeletedEmployeeImportRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        null,
                        null,
                        null,
                        null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new DeletedEmployeeImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulImports: successfulImports,
                FailedRecords: failedRecords,
                DuplicateIqamas: duplicateIqamas,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<DeletedEmployeeImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    public async Task<Result<HousingAssignmentResponse>> BulkAssignEmployeesToHousingAsync(
     IFormFile file,
     string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<HousingAssignmentResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<HousingAssignmentResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<HousingAssignmentRowResult>();
        var errors = new List<string>();
        int successfulAssignments = 0;
        int failedRecords = 0;
        int employeeNotFound = 0;
        int housingNotFound = 0;
        int alreadyAssigned = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<HousingAssignmentResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindHousingHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<HousingAssignmentResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildHousingColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<HousingAssignmentResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            // Load housing lookup dictionary
            var housings = await _dbcontext.Housings
                .AsNoTracking()
                .ToDictionaryAsync(h => h.Name.Trim().ToLower(), h => h.Id);

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseHousingRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new HousingAssignmentRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            "N/A",
                            "N/A",
                            rowData.HousingName ?? "N/A",
                            false,
                            null,
                            false,
                            null,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Find employee with rider details
                    var employee = await _dbcontext.Employees
                        .Include(e => e.Housing)
                        .Include(e => e.RiderDetails)
                            .ThenInclude(rd => rd.Company)
                        .FirstOrDefaultAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (employee == null)
                    {
                        employeeNotFound++;
                        failedRecords++;
                        results.Add(new HousingAssignmentRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            "N/A",
                            "N/A",
                            rowData.HousingName!,
                            false,
                            null,
                            false,
                            null,
                            warnings,
                            "Employee with this Iqama number not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Check if this person is a rider
                    bool isRider = employee.RiderDetails != null;
                    string? companyName = employee.RiderDetails?.Company?.Name;

                    if (isRider)
                    {
                        warnings.Add($"This is a rider from company: {companyName}");
                    }

                    // Find housing
                    if (!housings.TryGetValue(rowData.HousingName!.Trim().ToLower(), out int housingId))
                    {
                        housingNotFound++;
                        failedRecords++;
                        results.Add(new HousingAssignmentRowResult(
                            rowNumber,
                            false,
                            employee.IqamaNo.ToString(),
                            employee.NameEN,
                            employee.NameAR,
                            rowData.HousingName!,
                            isRider,
                            companyName,
                            false,
                            null,
                            warnings,
                            $"Housing '{rowData.HousingName}' not found in database"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Check if already assigned
                    string? previousHousing = null;
                    bool wasAlreadyAssigned = false;

                    if (employee.HousingId.HasValue)
                    {
                        previousHousing = employee.Housing?.Name;

                        if (employee.HousingId == housingId)
                        {
                            alreadyAssigned++;
                            warnings.Add($"Already assigned to {rowData.HousingName}");

                            results.Add(new HousingAssignmentRowResult(
                                rowNumber,
                                true,
                                employee.IqamaNo.ToString(),
                                employee.NameEN,
                                employee.NameAR,
                                rowData.HousingName!,
                                isRider,
                                companyName,
                                true,
                                previousHousing,
                                warnings,
                                null
                            ));

                            await transaction.CommitAsync();
                            continue;
                        }

                        wasAlreadyAssigned = true;
                        warnings.Add($"Changed housing from '{previousHousing}' to '{rowData.HousingName}'");
                    }

                    // Assign to housing (works for both employees and riders)
                    employee.HousingId = housingId;

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulAssignments++;
                    results.Add(new HousingAssignmentRowResult(
                        rowNumber,
                        true,
                        employee.IqamaNo.ToString(),
                        employee.NameEN,
                        employee.NameAR,
                        rowData.HousingName!,
                        isRider,
                        companyName,
                        wasAlreadyAssigned,
                        previousHousing,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new HousingAssignmentRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        "N/A",
                        "N/A",
                        "N/A",
                        false,
                        null,
                        false,
                        null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new HousingAssignmentResponse(
                TotalRecords: dataRows.Count,
                SuccessfulAssignments: successfulAssignments,
                FailedRecords: failedRecords,
                EmployeeNotFound: employeeNotFound,
                HousingNotFound: housingNotFound,
                AlreadyAssigned: alreadyAssigned,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingAssignmentResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }


    public async Task<Result<DirectImportResponse>> ImportEmployeesAndRidersAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<DirectImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<DirectImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<ImportRowResult>();
        var errors = new List<string>();
        int successfulEmployees = 0;
        int successfulRiders = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<DirectImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<DirectImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            // Map columns by finding their positions
            var columnMap = BuildColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<DirectImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            // Load company lookup dictionary
            var companies = await _dbcontext.Companies
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Name.Trim().ToLower(), c => c.Id);

            var dataRows = worksheet.RowsUsed()
            .Where(r => r.RowNumber() > headerRow.RowNumber())
            .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new ImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo.ToString() ?? "N/A",
                            rowData.NameEN ?? "N/A",
                            rowData.NameAR ?? "N/A",
                            rowData.CompanyName,
                            false, false, false, false,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Process Employee
                    var (employeeCreated, employeeUpdated, employeeError) =
                        await ProcessEmployee(rowData, warnings);

                    if (employeeError != null)
                    {
                        await transaction.RollbackAsync();
                        failedRecords++;
                        results.Add(new ImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo.ToString(),
                            rowData.NameEN ?? "N/A",
                            rowData.NameAR ?? "N/A",
                            rowData.CompanyName,
                            false, false, false, false,
                            warnings,
                            employeeError
                        ));
                        continue;
                    }

                    if (employeeCreated || employeeUpdated)
                        successfulEmployees++;

                    // Process Rider (if company data exists)
                    bool riderCreated = false;
                    bool riderUpdated = false;

                    if (!string.IsNullOrWhiteSpace(rowData.CompanyName))
                    {
                        if (companies.TryGetValue(rowData.CompanyName.Trim().ToLower(), out int companyId))
                        {
                            rowData.CompanyId = companyId;
                            var (created, updated, riderError) =
                                await ProcessRider(rowData, warnings);

                            if (riderError != null)
                            {
                                warnings.Add($"Rider processing failed: {riderError}");
                            }
                            else
                            {
                                riderCreated = created;
                                riderUpdated = updated;
                                if (created || updated)
                                    successfulRiders++;
                            }
                        }
                        else
                        {
                            warnings.Add($"Company '{rowData.CompanyName}' not found in database");
                        }
                    }

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    results.Add(new ImportRowResult(
                        rowNumber,
                        true,
                        rowData.IqamaNo.ToString(),
                        rowData.NameEN ?? "",
                        rowData.NameAR ?? "",
                        rowData.CompanyName,
                        employeeCreated,
                        employeeUpdated,
                        riderCreated,
                        riderUpdated,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new ImportRowResult(
                        rowNumber,
                        false,
                        "N/A", "N/A", "N/A", null,
                        false, false, false, false,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new DirectImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulEmployees: successfulEmployees,
                SuccessfulRiders: successfulRiders,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<DirectImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    public async Task<Result<VehicleImportResponse>> ImportVehiclesAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<VehicleImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<VehicleImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<VehicleImportRowResult>();
        var errors = new List<string>();
        int successfulVehicles = 0;
        int updatedVehicles = 0;
        int assignedToRiders = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<VehicleImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindHeaderRow1(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<VehicleImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildColumnMapping1(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<VehicleImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new VehicleImportRowResult(
                            rowNumber,
                            false,
                            rowData.VehicleNumber ?? "N/A",
                            rowData.PlateNumberA ?? "N/A",
                            rowData.SerialNumber,
                            false, false, false, null,
                            new List<string>(),
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();
                    var changes = new List<string>();

                    var (vehicleCreated, vehicleUpdated, vehicleError, vehicleChanges) =
                        await ProcessVehicle(rowData, warnings, uploadedBy);

                    if (vehicleError != null)
                    {
                        await transaction.RollbackAsync();
                        failedRecords++;
                        results.Add(new VehicleImportRowResult(
                            rowNumber,
                            false,
                            rowData.VehicleNumber!,
                            rowData.PlateNumberA!,
                            rowData.SerialNumber,
                            false, false, false, null,
                            new List<string>(),
                            warnings,
                            vehicleError
                        ));
                        continue;
                    }

                    if (vehicleCreated)
                        successfulVehicles++;
                    else if (vehicleUpdated)
                        updatedVehicles++;

                    changes.AddRange(vehicleChanges);

                    bool assignedToRider = false;
                    string? assignedRiderIqama = null;

                    if (rowData.RiderIqamaNo.HasValue)
                    {
                        var (assigned, assignError) =
                            await ProcessRiderAssignment(rowData, warnings, uploadedBy);

                        if (assignError != null)
                        {
                            warnings.Add($"Rider assignment failed: {assignError}");
                        }
                        else if (assigned)
                        {
                            assignedToRider = true;
                            assignedRiderIqama = rowData.RiderIqamaNo.Value.ToString();
                            assignedToRiders++;
                            changes.Add($"Assigned to rider {rowData.RiderIqamaNo.Value}");
                        }
                    }

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    results.Add(new VehicleImportRowResult(
                        rowNumber,
                        true,
                        rowData.VehicleNumber!,
                        rowData.PlateNumberA!,
                        rowData.SerialNumber,
                        vehicleCreated,
                        vehicleUpdated,
                        assignedToRider,
                        assignedRiderIqama,
                        changes,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new VehicleImportRowResult(
                        rowNumber,
                        false,
                        "N/A", "N/A", 0,
                        false, false, false, null,
                        new List<string>(),
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new VehicleImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulVehicles: successfulVehicles,
                UpdatedVehicles: updatedVehicles,
                AssignedToRiders: assignedToRiders,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private VehicleColumnMapping BuildColumnMapping1(IXLRow headerRow)
    {
        var mapping = new VehicleColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.VehicleNumberCol = FindColumn1(cells,
            "VehicleNumber", "Vehicle Number", "رقم الهيكل", "Vehicle ID", "VIN");

        mapping.SerialNumberCol = FindColumn1(cells,
            "SerialNumber", "Serial Number", "الرقم التسلسلي", "Serial No", "Serial");

        mapping.PlateNumberACol = FindColumn1(cells,
            "PlateNumberA", "Plate Number A", "رقم اللوحة", "اللوحة العربية", "Plate A", "Arabic Plate");

        mapping.PlateNumberECol = FindColumn1(cells,
            "PlateNumberE", "Plate Number E", "رقم اللوحة En", "اللوحة الانجليزية", "Plate E", "English Plate");

        mapping.VehicleTypeCol = FindColumn1(cells,
            "VehicleType", "Vehicle Type", "نوع المركبة", "طراز المركبة");

        mapping.ManufacturerCol = FindColumn1(cells,
            "Manufacturer", "الصانع", "المصنع", "ماركة المركبة", "Brand");

        mapping.ManufactureYearCol = FindColumn1(cells,
            "ManufactureYear", "Manufacture Year", "سنة الصنع", "Year", "Model Year");

        mapping.LicenseExpiryDateCol = FindColumn1(cells,
            "LicenseExpiryDate", "License Expiry Date", "تاريخ انتهاء الرخصة", "License Expiry", "Expiry Date");

        mapping.LocationCol = FindColumn1(cells,
            "Location", "الموقع", "المكان");

        mapping.StatusCol = FindColumn1(cells,
            "Status", "الحالة", "ملاحظات");

        mapping.RiderIqamaNoCol = FindColumn1(cells,
            "RiderIqamaNo", "Rider Iqama", "رقم اقامة السائق", "Driver Iqama", "EmployeeIqamaNo", "IqamaNo");

        var missing = new List<string>();
        if (mapping.VehicleNumberCol == 0) missing.Add("Vehicle Number");
        if (mapping.SerialNumberCol == 0) missing.Add("Serial Number");
        if (mapping.PlateNumberACol == 0) missing.Add("Plate Number A");
        if (mapping.PlateNumberECol == 0) missing.Add("Plate Number E");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private int FindColumn1(List<IXLCell> cells, params string[] possibleNames)
    {
        foreach (var cell in cells)
        {
            try
            {
                if (cell.IsEmpty()) continue;

                string headerValue = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString().Trim()
                    : cell.GetString().Trim();

                if (string.IsNullOrWhiteSpace(headerValue)) continue;

                foreach (var name in possibleNames)
                {
                    if (headerValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }

                string headerNoSpaces = headerValue.Replace(" ", "");
                foreach (var name in possibleNames)
                {
                    string nameNoSpaces = name.Replace(" ", "");
                    if (headerNoSpaces.Equals(nameNoSpaces, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }

                foreach (var name in possibleNames)
                {
                    if (headerValue.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }
            }
            catch { }
        }

        return 0;
    }

    private VehicleRowData ParseRowData(IXLRow row, VehicleColumnMapping map, int rowNumber)
    {
        var data = new VehicleRowData { RowNumber = rowNumber };

        try
        {
            data.VehicleNumber = GetCellValue1(row, map.VehicleNumberCol);
            if (string.IsNullOrWhiteSpace(data.VehicleNumber))
            {
                data.IsValid = false;
                data.ErrorMessage = "Vehicle Number is required";
                return data;
            }

            var serialStr = GetCellValue1(row, map.SerialNumberCol);
            if (string.IsNullOrWhiteSpace(serialStr) ||
                !int.TryParse(serialStr.Replace(" ", ""), out int serialNumber))
            {
                data.IsValid = false;
                data.ErrorMessage = "Valid Serial Number is required";
                return data;
            }
            data.SerialNumber = serialNumber;

            data.PlateNumberA = GetCellValue1(row, map.PlateNumberACol)?.Replace(" ","");
            if (string.IsNullOrWhiteSpace(data.PlateNumberA))
            {
                data.IsValid = false;
                data.ErrorMessage = "Plate Number A is required";
                return data;
            }

            data.PlateNumberE = GetCellValue1(row, map.PlateNumberECol)?.Replace(" ", "");
            if (string.IsNullOrWhiteSpace(data.PlateNumberE))
            {
                data.IsValid = false;
                data.ErrorMessage = "Plate Number E is required";
                return data;
            }

            data.VehicleType = GetCellValue1(row, map.VehicleTypeCol) ?? "دراجة نارية";
            data.Manufacturer = GetCellValue1(row, map.ManufacturerCol) ?? "Unknown";
            data.Location = GetCellValue1(row, map.LocationCol) ?? "الشركة";
            data.Status = GetCellValue1(row, map.StatusCol) ?? "Returned";

            var yearStr = GetCellValue1(row, map.ManufactureYearCol);
            data.ManufactureYear = int.TryParse(yearStr, out int year) && year >= 1900 && year <= DateTime.Now.Year + 1
                ? year
                : DateTime.Now.Year;

            data.LicenseExpiryDate = ParseDate(GetCellValue1(row, map.LicenseExpiryDateCol))
                ?? DateOnly.FromDateTime(DateTime.Now.AddYears(1));

            var riderIqamaStr = GetCellValue1(row, map.RiderIqamaNoCol);
            if (!string.IsNullOrWhiteSpace(riderIqamaStr) &&
                long.TryParse(riderIqamaStr.Replace(" ", ""), out long riderIqama))
            {
                data.RiderIqamaNo = riderIqama;
            }

            data.OwnerName = "الخدمة السريعة";
            data.OwnerId = 7010962889;

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    private async Task<(bool created, bool updated, string? error, List<string> changes)> ProcessVehicle(
        VehicleRowData data,
        List<string> warnings,
        string uploadedBy)
    {
        var changes = new List<string>();

        try
        {
            var conflictingVehicle = await _dbcontext.Vehicles
                .Where(v => v.VehicleNumber != data.VehicleNumber &&
                           (v.SerialNumber == data.SerialNumber ||
                            v.PlateNumberA == data.PlateNumberA ||
                            v.PlateNumberE == data.PlateNumberE))
                .FirstOrDefaultAsync();

            if (conflictingVehicle != null)
            {
                return (false, false,
                    $"Conflict: Serial/Plate already exists on vehicle {conflictingVehicle.VehicleNumber}",
                    changes);
            }

            var vehicle = await _dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleNumber == data.VehicleNumber);

            if (vehicle == null)
            {
                vehicle = new Vehicle
                {
                    VehicleNumber = data.VehicleNumber!,
                    SerialNumber = data.SerialNumber,
                    PlateNumberA = data.PlateNumberA!,
                    PlateNumberE = data.PlateNumberE!,
                    VehicleType = data.VehicleType!,
                    Manufacturer = data.Manufacturer!,
                    ManufactureYear = data.ManufactureYear,
                    LicenseExpiryDate = data.LicenseExpiryDate!.Value,
                    Location = data.Location!,
                    OwnerName = data.OwnerName!,
                    OwnerId = data.OwnerId,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                };

                await _dbcontext.Vehicles.AddAsync(vehicle);
                changes.Add("Vehicle created");

                return (true, false, null, changes);
            }
            else
            {
                bool hasChanges = false;

                if (vehicle.SerialNumber != data.SerialNumber)
                {
                    changes.Add($"Serial changed: {vehicle.SerialNumber} → {data.SerialNumber}");
                    vehicle.SerialNumber = data.SerialNumber;
                    hasChanges = true;
                }

                if (vehicle.PlateNumberA != data.PlateNumberA)
                {
                    changes.Add($"Plate A changed: {vehicle.PlateNumberA} → {data.PlateNumberA}");
                    vehicle.PlateNumberA = data.PlateNumberA!;
                    hasChanges = true;
                }

                if (vehicle.PlateNumberE != data.PlateNumberE)
                {
                    changes.Add($"Plate E changed: {vehicle.PlateNumberE} → {data.PlateNumberE}");
                    vehicle.PlateNumberE = data.PlateNumberE!;
                    hasChanges = true;
                }

                if (vehicle.VehicleType != data.VehicleType)
                {
                    vehicle.VehicleType = data.VehicleType!;
                    hasChanges = true;
                }

                if (vehicle.Manufacturer != data.Manufacturer)
                {
                    vehicle.Manufacturer = data.Manufacturer!;
                    hasChanges = true;
                }

                if (vehicle.ManufactureYear != data.ManufactureYear)
                {
                    vehicle.ManufactureYear = data.ManufactureYear;
                    hasChanges = true;
                }

                if (vehicle.LicenseExpiryDate != data.LicenseExpiryDate!.Value)
                {
                    vehicle.LicenseExpiryDate = data.LicenseExpiryDate.Value;
                    hasChanges = true;
                }

                if (vehicle.Location != data.Location)
                {
                    changes.Add($"Location changed: {vehicle.Location} → {data.Location}");
                    vehicle.Location = data.Location!;
                    hasChanges = true;
                }

                await HandleStatusChanges(vehicle, data, changes, uploadedBy);

                if (hasChanges)
                {
                    changes.Add("Vehicle updated");
                    return (false, true, null, changes);
                }
                else
                {
                    warnings.Add("Vehicle exists with same data - no changes");
                    return (false, false, null, changes);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, false, $"Vehicle processing error: {ex.Message}", changes);
        }
    }

    private async Task HandleStatusChanges(
        Vehicle vehicle,
        VehicleRowData data,
        List<string> changes,
        string uploadedBy)
    {
        // Check current status
        var currentActiveStatus = await _dbcontext.RiderVehicleStatus
            .Where(s => s.VehicleNumber == vehicle.VehicleNumber && s.IsActive)
            .FirstOrDefaultAsync();

        string currentStatus = currentActiveStatus?.StatusType.ToString() ?? "Available";

        // If status in Excel differs from current status
        if (data.Status != null && !data.Status.Equals(currentStatus, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"Status changed: {currentStatus} → {data.Status}");

            // Deactivate old status
            if (currentActiveStatus != null)
            {
                currentActiveStatus.IsActive = false;
            }

            // Add new status if not "Available"
            if (!data.Status.Equals("Available", StringComparison.OrdinalIgnoreCase))
            {
                VehicleStatusType newStatusType = data.Status.ToLower() switch
                {
                    "problem" => VehicleStatusType.Problem,
                    "stolen" => VehicleStatusType.Stolen,
                    "breakup" or "break up" => VehicleStatusType.BreakUp,
                    _ => VehicleStatusType.Returned
                };

                _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                {
                    VehicleNumber = vehicle.VehicleNumber,
                    EmployeeIqamaNo = null,
                    StatusType = newStatusType,
                    Reason = $"Status updated via import by {uploadedBy}",
                    IsActive = true,
                    Timestamp = DateTime.UtcNow.AddHours(3)
                });
            }
        }
    }

    private async Task<(bool assigned, string? error)> ProcessRiderAssignment(
        VehicleRowData data,
        List<string> warnings,
        string uploadedBy)
    {
        try
        {
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == data.RiderIqamaNo!.Value);

            if (rider == null)
                return (false, $"Rider with Iqama {data.RiderIqamaNo} not found");

            if (rider.Employee.Status != "enable")
                return (false, "Rider is disabled");

            // Check if rider already has a vehicle
            if (!string.IsNullOrEmpty(rider.VehicleNumber))
            {
                warnings.Add($"Rider already has vehicle {rider.VehicleNumber}, replacing it");

                // Return old vehicle
                var oldVehicleStatus = await _dbcontext.RiderVehicleStatus
                    .FirstOrDefaultAsync(s => s.VehicleNumber == rider.VehicleNumber &&
                                             s.EmployeeIqamaNo == rider.EmployeeIqamaNo &&
                                             s.IsActive &&
                                             s.StatusType == VehicleStatusType.Taken);

                if (oldVehicleStatus != null)
                {
                    oldVehicleStatus.IsActive = false;
                    _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                    {
                        VehicleNumber = rider.VehicleNumber,
                        EmployeeIqamaNo = rider.EmployeeIqamaNo,
                        StatusType = VehicleStatusType.Returned,
                        Reason = "Replaced by import",
                        IsActive = false,
                        Timestamp = DateTime.UtcNow.AddHours(3)
                    });
                }
            }

            // Check if vehicle is available
            var vehicleUnavailable = await _dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == data.VehicleNumber &&
                              s.IsActive &&
                              (s.StatusType == VehicleStatusType.Taken ||
                               s.StatusType == VehicleStatusType.Problem ||
                               s.StatusType == VehicleStatusType.Stolen));

            if (vehicleUnavailable)
            {
                // Deactivate old statuses
                var oldStatuses = await _dbcontext.RiderVehicleStatus
                    .Where(s => s.VehicleNumber == data.VehicleNumber && s.IsActive)
                    .ToListAsync();

                foreach (var status in oldStatuses)
                {
                    status.IsActive = false;
                }

                warnings.Add("Vehicle was unavailable, forcing assignment");
            }

            // Assign vehicle to rider
            rider.VehicleNumber = data.VehicleNumber;

            // Add history
            _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                VehicleNumber = data.VehicleNumber!,
                EmployeeIqamaNo = data.RiderIqamaNo!.Value,
                StatusType = VehicleStatusType.Taken,
                Reason = $"Assigned via import by {uploadedBy}",
                IsActive = true,
                Timestamp = DateTime.UtcNow.AddHours(3)
            });

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Assignment error: {ex.Message}");
        }
    }

    private IXLRow FindHeaderRow1(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
            "VehicleNumber", "Vehicle Number", "SerialNumber", "Serial Number",
            "PlateNumberA", "Plate A", "PlateNumberE", "Plate E",
            "رقم المركبة", "الرقم التسلسلي", "رقم اللوحة"
        };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        // Fallback
        return worksheet.Row(1);
    }

    private string? GetCellValue1(IXLRow row, int columnIndex)
    {
        if (columnIndex == 0) return null;

        try
        {
            var cell = row.Cell(columnIndex);
            if (cell.IsEmpty()) return null;

            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime().ToString("dd/MM/yyyy");

            if (cell.DataType == XLDataType.Number)
                return cell.GetDouble().ToString();

            if (cell.DataType == XLDataType.Text)
                return cell.GetText().Trim();

            if (cell.DataType == XLDataType.Boolean)
                return cell.GetBoolean().ToString();

            return cell.Value.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private DateOnly? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        string[] formats = {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
            "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy",
            "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d",
            "dd.MM.yyyy", "d.M.yyyy"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return DateOnly.FromDateTime(date);
            }
        }

        if (DateTime.TryParse(dateStr, out DateTime generalDate))
        {
            return DateOnly.FromDateTime(generalDate);
        }

        return null;
    }

    private ColumnMapping BuildColumnMapping(IXLRow headerRow)
    {
        var mapping = new ColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();

                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
                Console.WriteLine($"Header Column {cell.Address.ColumnNumber} ({cell.Address.ColumnLetter}): '{val}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading column {cell.Address.ColumnNumber}: {ex.Message}");
            }
        }

        // Map columns
        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة", "Iqama No", "IqamaNo", "الاقامة");
        Console.WriteLine($"IqamaNo mapped to column: {mapping.IqamaNoCol}");

        mapping.NameARCol = FindColumn(cells,
            "NameAR", "Name AR", "الاسم بالعربية", "الاسم العربي", "Arabic Name", "الاسم");
        Console.WriteLine($"NameAR mapped to column: {mapping.NameARCol}");

        mapping.NameENCol = FindColumn(cells,
            "NameEN", "Name EN", "الاسم بالإنجليزية", "الاسم الانجليزي", "English Name", "Name");
        Console.WriteLine($"NameEN mapped to column: {mapping.NameENCol}");

        mapping.IqamaEndMCol = FindColumn(cells,
            "IqamaEndM", "Iqama End M", "تاريخ انتهاء الاقامة ميلادي", "انتهاء الاقامة", "Iqama Expiry");

        mapping.IqamaEndHCol = FindColumn(cells,
            "IqamaEndH", "Iqama End H", "تاريخ انتهاء الاقامة هجري", "Hijri Date");

        mapping.PassportNoCol = FindColumn(cells,
            "IqamaNumber", "Passport Number", "رقم الجواز", "رقم جواز السفر", "Passport No", "PassportNo");

        mapping.PassportEndCol = FindColumn(cells,
            "PassportEnd", "Passport End", "تاريخ انتهاء الجواز", "انتهاء الجواز", "Passport Expiry");

        mapping.SponsorCol = FindColumn(cells,
            "Sponsor", "الكفيل", "اسم الكفيل", "Sponsor Name");

        mapping.SponsorNoCol = FindColumn(cells,
            "SponsorNo", "Sponsor No", "رقم الكفيل", "Sponsor Number");

        mapping.JobTitleCol = FindColumn(cells,
            "JobTitle", "Job Title", "المسمى الوظيفي", "الوظيفة", "Position");

        mapping.CountryCol = FindColumn(cells,
            "Country", "الجنسية", "البلد", "Nationality");

        mapping.PhoneCol = FindColumn(cells,
            "Phone", "رقم الجوال", "الجوال", "Mobile", "Phone Number");

        mapping.DateOfBirthCol = FindColumn(cells,
            "DateOfBirth", "Date Of Birth", "تاريخ الميلاد", "الميلاد", "Birth Date", "DOB");

        mapping.StatusCol = FindColumn(cells,
            "Status", "الحالة", "Employee Status");

        mapping.IBANCol = FindColumn(cells,
            "IBAN", "رقم الآيبان", "الآيبان", "Bank Account");

        mapping.INKSACol = FindColumn(cells,
            "INKSA", "في السعودية", "In KSA");

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingID", "Working ID", "معرف العمل", "رقم العمل", "Work ID", "Employee ID");

        mapping.TshirtSizeCol = FindColumn(cells,
            "TshirtSize", "Tshirt Size", "مقاس القميص", "القميص", "T-shirt", "Shirt Size");

        mapping.LicenseNumberCol = FindColumn(cells,
            "LicenseNumber", "License Number", "رقم الرخصة", "الرخصة", "License No", "Driving License");

        mapping.CompanyNameCol = FindColumn(cells,
            "CompanyName", "Company Name", "اسم الشركة", "الشركة", "Company", "اسم شركة العميل");

        // Validate required columns
        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.NameARCol == 0) missing.Add("NameAR");
        if (mapping.NameENCol == 0) missing.Add("NameEN");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)} \n" +
                                  $"Header row number: {headerRow.RowNumber()}\n" +
                                  $"Columns found in header row:\n{string.Join("\n", actualHeaders)} \n" +
                                  $"Expected variations for NameAR: NameAR, Name AR, الاسم بالعربية  \n" +
                                  $"Expected variations for NameEN: NameEN, Name EN, Name, الاسم بالإنجليزية";

            Console.WriteLine($"ERROR: {mapping.ErrorMessage}");
        }
        else
        {
            mapping.IsValid = true;
            Console.WriteLine("SUCCESS: All required columns found!");
        }

        return mapping;
    }
    private int FindColumn(List<IXLCell> cells, params string[] possibleNames)
    {
        foreach (var cell in cells)
        {
            try
            {
                if (cell.IsEmpty()) continue;

                string headerValue = "";

                if (cell.IsMerged())
                {
                    headerValue = cell.MergedRange().FirstCell().GetString().Trim();
                }
                else
                {
                    switch (cell.DataType)
                    {
                        case XLDataType.Text:
                            headerValue = cell.GetText().Trim();
                            break;
                        case XLDataType.Number:
                            headerValue = cell.GetDouble().ToString().Trim();
                            break;
                        case XLDataType.Boolean:
                            headerValue = cell.GetBoolean().ToString().Trim();
                            break;
                        default:
                            headerValue = cell.GetString().Trim();
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(headerValue))
                    continue;

                // Clean up the header value
                headerValue = headerValue.Trim();

                // Method 1: Exact match (case-insensitive)
                foreach (var name in possibleNames)
                {
                    if (headerValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return cell.Address.ColumnNumber;
                    }
                }

                // Method 2: Match without any spaces (NameAR = Name AR)
                string headerNoSpaces = headerValue.Replace(" ", "").Replace("\t", "").Replace("\n", "").Replace("\r", "");
                foreach (var name in possibleNames)
                {
                    string nameNoSpaces = name.Replace(" ", "").Replace("\t", "").Replace("\n", "").Replace("\r", "");
                    if (headerNoSpaces.Equals(nameNoSpaces, StringComparison.OrdinalIgnoreCase))
                    {
                        return cell.Address.ColumnNumber;
                    }
                }

                // Method 3: Partial match (contains) - as last resort
                foreach (var name in possibleNames)
                {
                    if (headerValue.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return cell.Address.ColumnNumber;
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        return 0;
    }

    private RowData ParseRowData(IXLRow row, ColumnMapping map, int rowNumber)
    {
        var data = new RowData { RowNumber = rowNumber };
        var errors = new List<string>();

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            // Parse Names (REQUIRED)
            data.NameAR = GetCellValue(row, map.NameARCol);
            data.NameEN = GetCellValue(row, map.NameENCol);

            if (string.IsNullOrWhiteSpace(data.NameAR))
            {
                data.IsValid = false;
                data.ErrorMessage = "Name AR is required";
                return data;
            }

            if (string.IsNullOrWhiteSpace(data.NameEN))
            {
                data.IsValid = false;
                data.ErrorMessage = "Name EN is required";
                return data;
            }

            // Parse Dates
            data.IqamaEndM = ParseGregorianDate(GetCellValue(row, map.IqamaEndMCol));
            data.IqamaEndH = ParseHijriDate(GetCellValue(row, map.IqamaEndHCol));
            data.PassportEnd = ParseGregorianDate(GetCellValue(row, map.PassportEndCol));
            data.DateOfBirth = ParseGregorianDate(GetCellValue(row, map.DateOfBirthCol));

            // Default dates if missing
            data.IqamaEndM ??= DateOnly.FromDateTime(DateTime.Now.AddYears(1));
            data.IqamaEndH ??= DateOnly.FromDateTime(DateTime.Now.AddYears(1));
            data.DateOfBirth ??= DateOnly.FromDateTime(new DateTime(1990, 1, 1));

            // Parse other fields
            data.PassportNo = GetCellValue(row, map.PassportNoCol);
            data.Sponsor = GetCellValue(row, map.SponsorCol) ?? "الخدمة السريعة";

            var sponsorNoStr = GetCellValue(row, map.SponsorNoCol);
            data.SponsorNo = long.TryParse(sponsorNoStr?.Replace(" ", ""), out long sNo) ? sNo : 0;

            data.JobTitle = GetCellValue(row, map.JobTitleCol) ?? "سائق دراجة نارية";
            data.Country = GetCellValue(row, map.CountryCol) ?? "Unknown";
            data.Phone = GetCellValue(row, map.PhoneCol) ?? "05";
            data.Status = GetCellValue(row, map.StatusCol) ?? "enable";
            data.IBAN = GetCellValue(row, map.IBANCol);

            // Parse INKSA
            var inksaStr = GetCellValue(row, map.INKSACol);
            data.INKSA = string.IsNullOrWhiteSpace(inksaStr) ||
                         inksaStr.ToLower() == "yes" ||
                         inksaStr == "1" ||
                         inksaStr.ToLower() == "true";

            // Rider fields
            data.WorkingId = GetCellValue(row, map.WorkingIdCol) ?? "0";
            data.TshirtSize = GetCellValue(row, map.TshirtSizeCol)??"s";
            data.LicenseNumber = GetCellValue(row, map.LicenseNumberCol)??"0";
            data.CompanyName = GetCellValue(row, map.CompanyNameCol);

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    private IXLRow FindHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "NameEN", "Name EN", "NameAR", "Name AR",
        "IqamaNumber", "Iqama Number", "رقم الإقامة", "رقم الاقامة",
        "Phone", "Sponsor", "Country"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = "";
                    if (cell.IsMerged())
                    {
                        value = cell.MergedRange().FirstCell().GetString().Trim();
                    }
                    else
                    {
                        value = cell.GetString().Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        cellValues.Add(value);
                    }
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 3)
            {
                Console.WriteLine($"Found header row at row {i} with {matchCount} matching columns");
                return row;
            }
        }

        IXLRow? bestRow = null;
        int maxNonEmptyCells = 0;

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var nonEmptyCells = row.CellsUsed().Count(c =>
                !string.IsNullOrWhiteSpace(GetCellValueSafe(c)));

            if (nonEmptyCells > maxNonEmptyCells)
            {
                maxNonEmptyCells = nonEmptyCells;
                bestRow = row;
            }
        }

        Console.WriteLine($"Fallback: Using row {bestRow?.RowNumber()} with {maxNonEmptyCells} cells");
        return bestRow ?? worksheet.Row(1);
    }
    private string GetCellValueSafe(IXLCell cell)
    {
        try
        {
            if (cell.IsEmpty()) return "";

            if (cell.IsMerged())
            {
                var mergedRange = cell.MergedRange();
                cell = mergedRange.FirstCell();
            }

            if (cell.DataType == XLDataType.Text)
                return cell.GetText().Trim();
            else if (cell.DataType == XLDataType.Number)
                return cell.GetDouble().ToString().Trim();
            else if (cell.DataType == XLDataType.Boolean)
                return cell.GetBoolean().ToString().Trim();
            else
                return cell.Value.ToString()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private string? GetCellValue(IXLRow row, int columnIndex)
    {
        if (columnIndex == 0) return null;

        try
        {
            var cell = row.Cell(columnIndex);
            if (cell.IsEmpty()) return null;

            if (cell.DataType == XLDataType.DateTime)
            {
                return cell.GetDateTime().ToString("dd/MM/yyyy");
            }

            if (cell.DataType == XLDataType.Number)
            {
                return cell.GetDouble().ToString();
            }

            if (cell.DataType == XLDataType.Text)
            {
                return cell.GetText().Trim();
            }

            if (cell.DataType == XLDataType.Boolean)
            {
                return cell.GetBoolean().ToString();
            }

            var cellValue = cell.Value;
            return cellValue.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private DateOnly? ParseGregorianDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        string[] formats = {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
            "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy",
            "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d",
            "dd.MM.yyyy", "d.M.yyyy"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return DateOnly.FromDateTime(date);
            }
        }

        if (DateTime.TryParse(dateStr, out DateTime generalDate))
        {
            return DateOnly.FromDateTime(generalDate);
        }

        return null;
    }

    private DateOnly? ParseHijriDate(string? hijriDateStr)
    {
        if (string.IsNullOrWhiteSpace(hijriDateStr))
            return null;

        try
        {
            var parts = hijriDateStr.Split('/', '-');
            if (parts.Length != 3)
                return null;

            if (!int.TryParse(parts[0], out int day) ||
                !int.TryParse(parts[1], out int month) ||
                !int.TryParse(parts[2], out int year))
                return null;

            if (year < 1300 || year > 1500 ||
                month < 1 || month > 12 ||
                day < 1 || day > 30)
                return null;

            var hijriCalendar = new HijriCalendar();

            int maxDays = hijriCalendar.GetDaysInMonth(year, month);
            if (day > maxDays)
                day = maxDays;

            var gregorianDate = hijriCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            return DateOnly.FromDateTime(gregorianDate);
        }
        catch
        {
            return null;
        }
    }

    private async Task<(bool created, bool updated, string? error)> ProcessEmployee(
        RowData data,
        List<string> warnings)
    {
        try
        {
            var employee = await _dbcontext.Employees
                .FirstOrDefaultAsync(e => e.IqamaNo == data.IqamaNo);

            if (employee == null)
            {
                employee = new Employees
                {
                    IqamaNo = data.IqamaNo,
                    NameAR = data.NameAR!,
                    NameEN = data.NameEN!,
                    IqamaEndM = data.IqamaEndM!.Value,
                    IqamaEndH = data.IqamaEndH!.Value,
                    PassportNo = data.PassportNo,
                    PassportEnd = data.PassportEnd,
                    Sponsor = data.Sponsor!,
                    sponsorNo = data.SponsorNo,
                    JobTitle = data.JobTitle!,
                    Country = data.Country!,
                    Phone = data.Phone!,
                    DateOfBirth = data.DateOfBirth!.Value,
                    Status = data.Status!,
                    IBAN = data.IBAN,
                    INKSA = data.INKSA,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                };

                await _dbcontext.Employees.AddAsync(employee);
                await _dbcontext.SaveChangesAsync();

                return (true, false, null);
            }
            else
            {
                bool hasChanges = false;

                if (data.IqamaEndM.HasValue && employee.IqamaEndM != data.IqamaEndM.Value)
                {
                    employee.IqamaEndM = data.IqamaEndM.Value;
                    hasChanges = true;
                }

                if (data.IqamaEndH.HasValue && employee.IqamaEndH != data.IqamaEndH.Value)
                {
                    employee.IqamaEndH = data.IqamaEndH.Value;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.PassportNo) && employee.PassportNo != data.PassportNo)
                {
                    employee.PassportNo = data.PassportNo;
                    hasChanges = true;
                }

                if (data.PassportEnd.HasValue && employee.PassportEnd != data.PassportEnd)
                {
                    employee.PassportEnd = data.PassportEnd;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Sponsor) && employee.Sponsor != data.Sponsor)
                {
                    employee.Sponsor = data.Sponsor;
                    hasChanges = true;
                }

                if (data.SponsorNo != 0 && employee.sponsorNo != data.SponsorNo)
                {
                    employee.sponsorNo = data.SponsorNo;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.JobTitle) && employee.JobTitle != data.JobTitle)
                {
                    employee.JobTitle = data.JobTitle;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.NameAR) && employee.NameAR != data.NameAR)
                {
                    employee.NameAR = data.NameAR;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.NameEN) && employee.NameEN != data.NameEN)
                {
                    employee.NameEN = data.NameEN;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Country) && employee.Country != data.Country)
                {
                    employee.Country = data.Country;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Phone) && employee.Phone != data.Phone)
                {
                    employee.Phone = data.Phone;
                    hasChanges = true;
                }

                if (data.DateOfBirth.HasValue && employee.DateOfBirth != data.DateOfBirth.Value)
                {
                    employee.DateOfBirth = data.DateOfBirth.Value;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Status) && employee.Status != data.Status)
                {
                    employee.Status = data.Status;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.IBAN) && employee.IBAN != data.IBAN)
                {
                    employee.IBAN = data.IBAN;
                    hasChanges = true;
                }

                employee.INKSA = data.INKSA;

                if (hasChanges)
                {
                    await _dbcontext.SaveChangesAsync();
                    warnings.Add("Employee record updated with new data");
                    return (false, true, null);
                }
                else
                {
                    warnings.Add("Employee exists with same data - no changes made");
                    return (false, false, null);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, false, $"Employee processing error: {ex.Message}");
        }
    }

    private async Task<(bool created, bool updated, string? error)> ProcessRider(
        RowData data,
        List<string> warnings)
    {
        try
        {
            if (!data.CompanyId.HasValue)
            {
                return (false, false, "Company ID not resolved");
            }

            var rider = await _dbcontext.RiderDetails
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == data.IqamaNo);

            if (rider == null)
            {
                if (string.IsNullOrWhiteSpace(data.WorkingId) && !data.CompanyId.HasValue)
                {
                    warnings.Add("No rider data provided - skipping rider creation");
                    return (false, false, null);
                }

                rider = new RiderDetails
                {
                    EmployeeIqamaNo = data.IqamaNo,
                    WorkingId = data.WorkingId,
                    TshirtSize = data.TshirtSize,
                    LicenseNumber = data.LicenseNumber,
                    CompanyId = data.CompanyId.Value,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                };

                await _dbcontext.RiderDetails.AddAsync(rider);
                await _dbcontext.SaveChangesAsync();

                return (true, false, null);
            }
            else
            {
                bool hasChanges = false;

                if (!string.IsNullOrWhiteSpace(data.WorkingId) && rider.WorkingId != data.WorkingId)
                {
                    rider.WorkingId = data.WorkingId;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.TshirtSize) && rider.TshirtSize != data.TshirtSize)
                {
                    rider.TshirtSize = data.TshirtSize;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.LicenseNumber) && rider.LicenseNumber != data.LicenseNumber)
                {
                    rider.LicenseNumber = data.LicenseNumber;
                    hasChanges = true;
                }

                if (data.CompanyId.HasValue && rider.CompanyId != data.CompanyId.Value)
                {
                    rider.CompanyId = data.CompanyId.Value;
                    hasChanges = true;
                    warnings.Add($"Rider company changed to {data.CompanyName}");
                }

                if (hasChanges)
                {
                    await _dbcontext.SaveChangesAsync();
                    warnings.Add("Rider record updated with new data");
                    return (false, true, null);
                }
                else
                {
                    warnings.Add("Rider exists with same data - no changes made");
                    return (false, false, null);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, false, $"Rider processing error: {ex.Message}");
        }
    }


    public async Task<Result<WorkingIdUpdateResponse>> UpdateRiderWorkingIdsAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<WorkingIdUpdateResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<WorkingIdUpdateResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<WorkingIdUpdateRowResult>();
        var errors = new List<string>();
        var notFoundIqamas = new List<string>();
        int successfulUpdates = 0;
        int failedRecords = 0;
        int iqamaNotFound = 0;
        int riderDetailsNotFound = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<WorkingIdUpdateResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindWorkingIdHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<WorkingIdUpdateResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            // Map columns by finding their positions
            var columnMap = BuildWorkingIdColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<WorkingIdUpdateResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseWorkingIdRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new WorkingIdUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            rowData.NewWorkingId,
                            null,
                            null,
                            null,
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    // Find employee and rider details
                    var employee = await _dbcontext.Employees
                        .Include(e => e.RiderDetails)
                        .FirstOrDefaultAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (employee == null)
                    {
                        iqamaNotFound++;
                        failedRecords++;
                        notFoundIqamas.Add(rowData.IqamaNo!.Value.ToString());

                        results.Add(new WorkingIdUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NewWorkingId,
                            null,
                            null,
                            null,
                            "Employee with this Iqama number not found"
                        ));

                        await transaction.RollbackAsync();
                        continue;
                    }

                    if (employee.RiderDetails == null)
                    {
                        riderDetailsNotFound++;
                        failedRecords++;

                        results.Add(new WorkingIdUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NewWorkingId,
                            null,
                            employee.NameEN,
                            employee.NameAR,
                            "Employee exists but has no RiderDetails record"
                        ));

                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Update WorkingId
                    string? oldWorkingId = employee.RiderDetails.WorkingId;
                    employee.RiderDetails.WorkingId = rowData.NewWorkingId;

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulUpdates++;
                    results.Add(new WorkingIdUpdateRowResult(
                        rowNumber,
                        true,
                        rowData.IqamaNo!.Value.ToString(),
                        rowData.NewWorkingId,
                        oldWorkingId,
                        employee.NameEN,
                        employee.NameAR,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new WorkingIdUpdateRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        null,
                        null,
                        null,
                        null,
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new WorkingIdUpdateResponse(
                TotalRecords: dataRows.Count,
                SuccessfulUpdates: successfulUpdates,
                FailedRecords: failedRecords,
                IqamaNotFound: iqamaNotFound,
                RiderDetailsNotFound: riderDetailsNotFound,
                Results: results,
                NotFoundIqamas: notFoundIqamas,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<WorkingIdUpdateResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private IXLRow FindWorkingIdHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
        "رقم الاقامة", "رقم الإقامة", "الاقامة",
        "WorkingId", "Working Id", "Working ID",
        "معرف العمل", "معرف الشغل", "رقم العمل"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var nonEmptyCells = row.CellsUsed().Count(c =>
                !string.IsNullOrWhiteSpace(GetCellValueSafe(c)));

            if (nonEmptyCells >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private WorkingIdColumnMapping BuildWorkingIdColumnMapping(IXLRow headerRow)
    {
        var mapping = new WorkingIdColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
            "رقم الاقامة", "رقم الإقامة", "الاقامة");

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working Id", "Working ID", "WorkingID",
            "معرف العمل", "معرف الشغل", "رقم العمل");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.WorkingIdCol == 0) missing.Add("Working ID");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private WorkingIdRowData ParseWorkingIdRowData(IXLRow row, WorkingIdColumnMapping map, int rowNumber)
    {
        var data = new WorkingIdRowData { RowNumber = rowNumber };

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            data.NewWorkingId = GetCellValue(row, map.WorkingIdCol);
            if (string.IsNullOrWhiteSpace(data.NewWorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "Working ID is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }


    internal class WorkingIdColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
        public int WorkingIdCol { get; set; }
    }

    internal class WorkingIdRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? IqamaNo { get; set; }
        public string? NewWorkingId { get; set; }
    }

    private IXLRow FindHousingHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
        "رقم الاقامة", "رقم الإقامة", "الاقامة",
        "HousingName", "Housing Name", "Housing", "السكن", "اسم السكن"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private HousingColumnMapping BuildHousingColumnMapping(IXLRow headerRow)
    {
        var mapping = new HousingColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
            "رقم الاقامة", "رقم الإقامة", "الاقامة");

        mapping.HousingNameCol = FindColumn(cells,
            "HousingName", "Housing Name", "Housing",
            "السكن", "اسم السكن", "المسكن");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.HousingNameCol == 0) missing.Add("Housing Name");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private HousingRowData ParseHousingRowData(IXLRow row, HousingColumnMapping map, int rowNumber)
    {
        var data = new HousingRowData { RowNumber = rowNumber };

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            data.HousingName = GetCellValue(row, map.HousingNameCol);
            if (string.IsNullOrWhiteSpace(data.HousingName))
            {
                data.IsValid = false;
                data.ErrorMessage = "Housing Name is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    private IXLRow FindDeletedEmployeeHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة",
        "NameEN", "Name EN", "NameAR", "Name AR",
        "WorkingId", "Working ID", "معرف العمل"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private DeletedEmployeeColumnMapping BuildDeletedEmployeeColumnMapping(IXLRow headerRow)
    {
        var mapping = new DeletedEmployeeColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        // Map all columns (only IqamaNo is required)
        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة", "IqamaNo");

        mapping.NameARCol = FindColumn(cells,
            "NameAR", "Name AR", "الاسم بالعربية", "الاسم العربي", "Arabic Name");

        mapping.NameENCol = FindColumn(cells,
            "NameEN", "Name EN", "الاسم بالإنجليزية", "English Name", "Name");

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working ID", "معرف العمل", "WorkID", "رقم العمل","المعرف");

        mapping.CompanyNameCol = FindColumn(cells,
            "CompanyName", "Company Name", "اسم الشركة", "الشركة");

        mapping.IqamaEndMCol = FindColumn(cells,
            "IqamaEndM", "Iqama End M", "تاريخ انتهاء الاقامة", "Iqama Expiry");

        mapping.IqamaEndHCol = FindColumn(cells,
            "IqamaEndH", "Iqama End H", "تاريخ انتهاء الاقامة هجري");

        mapping.PassportNoCol = FindColumn(cells,
            "PassportNo", "Passport Number", "رقم الجواز");

        mapping.PassportEndCol = FindColumn(cells,
            "PassportEnd", "Passport End", "تاريخ انتهاء الجواز");

        mapping.SponsorCol = FindColumn(cells,
            "Sponsor", "الكفيل", "اسم الكفيل");

        mapping.JobTitleCol = FindColumn(cells,
            "JobTitle", "Job Title", "المسمى الوظيفي", "الوظيفة");

        mapping.CountryCol = FindColumn(cells,
            "Country", "الجنسية", "البلد");

        mapping.PhoneCol = FindColumn(cells,
            "Phone", "رقم الجوال", "الجوال", "Mobile");

        mapping.DateOfBirthCol = FindColumn(cells,
            "DateOfBirth", "Date Of Birth", "تاريخ الميلاد");

        mapping.StatusCol = FindColumn(cells,
            "Status", "الحالة", "Employee Status");

        mapping.AcountStatusCol = FindColumn(cells,
            "AccountStatus", "حالة الحساب", "Account Status");

        mapping.IBANCol = FindColumn(cells,
            "IBAN", "رقم الآيبان", "الآيبان");

        mapping.INKSACol = FindColumn(cells,
            "INKSA", "في السعودية", "In KSA");

        mapping.TshirtSizeCol = FindColumn(cells,
            "TshirtSize", "Tshirt Size", "مقاس القميص");

        mapping.LicenseNumberCol = FindColumn(cells,
            "LicenseNumber", "License Number", "رقم الرخصة");

        // Only IqamaNo is required
        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required column missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private DeletedEmployeeRowData ParseDeletedEmployeeRowData(
        IXLRow row,
        DeletedEmployeeColumnMapping map,
        int rowNumber)
    {
        var data = new DeletedEmployeeRowData { RowNumber = rowNumber };

        try
        {
            // Parse IqamaNo (REQUIRED)
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            // Parse all optional fields
            data.NameAR = GetCellValue(row, map.NameARCol);
            data.NameEN = GetCellValue(row, map.NameENCol);
            data.WorkingId = GetCellValue(row, map.WorkingIdCol); // ~90% have this
            data.CompanyName = GetCellValue(row, map.CompanyNameCol);
            data.IqamaEndM = ParseGregorianDate(GetCellValue(row, map.IqamaEndMCol));
            data.IqamaEndH = ParseHijriDate(GetCellValue(row, map.IqamaEndHCol));
            data.PassportNo = GetCellValue(row, map.PassportNoCol);
            data.PassportEnd = ParseGregorianDate(GetCellValue(row, map.PassportEndCol));
            data.Sponsor = GetCellValue(row, map.SponsorCol);
            data.JobTitle = GetCellValue(row, map.JobTitleCol);
            data.Country = GetCellValue(row, map.CountryCol);
            data.Phone = GetCellValue(row, map.PhoneCol);
            data.Status = GetCellValue(row, map.StatusCol);
            data.AcountStatus = GetCellValue(row, map.AcountStatusCol);
            data.IBAN = GetCellValue(row, map.IBANCol);
            data.TshirtSize = GetCellValue(row, map.TshirtSizeCol);
            data.LicenseNumber = GetCellValue(row, map.LicenseNumberCol);

            // Parse DateOfBirth
            var dobStr = GetCellValue(row, map.DateOfBirthCol);
            if (!string.IsNullOrWhiteSpace(dobStr) && DateTime.TryParse(dobStr, out var dob))
            {
                data.DateOfBirth = dob;
            }

            // Parse INKSA
            var inksaStr = GetCellValue(row, map.INKSACol);
            data.INKSA = string.IsNullOrWhiteSpace(inksaStr) ||
                         inksaStr.ToLower() == "yes" ||
                         inksaStr == "1" ||
                         inksaStr.ToLower() == "true";

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // ✅ Internal Classes for Deleted Employee Import

    internal class DeletedEmployeeColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        // Required
        public int IqamaNoCol { get; set; }

        // Optional
        public int NameARCol { get; set; }
        public int NameENCol { get; set; }
        public int WorkingIdCol { get; set; }
        public int CompanyNameCol { get; set; }
        public int IqamaEndMCol { get; set; }
        public int IqamaEndHCol { get; set; }
        public int PassportNoCol { get; set; }
        public int PassportEndCol { get; set; }
        public int SponsorCol { get; set; }
        public int JobTitleCol { get; set; }
        public int CountryCol { get; set; }
        public int PhoneCol { get; set; }
        public int DateOfBirthCol { get; set; }
        public int StatusCol { get; set; }
        public int AcountStatusCol { get; set; }
        public int IBANCol { get; set; }
        public int INKSACol { get; set; }
        public int TshirtSizeCol { get; set; }
        public int LicenseNumberCol { get; set; }
    }

    internal class DeletedEmployeeRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        // Required
        public long? IqamaNo { get; set; }

        // Optional
        public string? NameAR { get; set; }
        public string? NameEN { get; set; }
        public string? WorkingId { get; set; }
        public string? CompanyName { get; set; }
        public DateOnly? IqamaEndM { get; set; }
        public DateOnly? IqamaEndH { get; set; }
        public string? PassportNo { get; set; }
        public DateOnly? PassportEnd { get; set; }
        public string? Sponsor { get; set; }
        public string? JobTitle { get; set; }
        public string? Country { get; set; }
        public string? Phone { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Status { get; set; }
        public string? AcountStatus { get; set; }
        public string? IBAN { get; set; }
        public bool INKSA { get; set; } = true;
        public string? TshirtSize { get; set; }
        public string? LicenseNumber { get; set; }
    }

    public async Task<Result<VehicleAssignmentImportResponse>> ImportVehicleAssignmentsAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<VehicleAssignmentImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<VehicleAssignmentImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<VehicleAssignmentRowResult>();
        var errors = new List<string>();
        int successfulAssignments = 0;
        int employeesConvertedToRiders = 0;
        int failedRecords = 0;
        int employeeNotFound = 0;
        int vehicleNotFound = 0;
        int vehicleUnavailable = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<VehicleAssignmentImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindVehicleAssignmentHeaderRow(worksheet);
            if (headerRow == null)
            {
                return Result.Failure<VehicleAssignmentImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildVehicleAssignmentColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<VehicleAssignmentImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseVehicleAssignmentRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new VehicleAssignmentRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            "N/A", "N/A",
                            rowData.PlateNumberA ?? "N/A",
                            "N/A",
                            false, false,
                            null, null,
                            rowData.Permission,
                            rowData.PermissionStartDate,
                            rowData.PermissionEndDate,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Trim spaces from identifiers
                    var cleanIqamaNo = rowData.IqamaNo!.Value;
                    var cleanPlateNumber = rowData.PlateNumberA!.Replace(" ", "").Trim();

                    // Find employee
                    var employee = await _dbcontext.Employees
                        .Include(e => e.RiderDetails)
                            .ThenInclude(rd => rd.Company)
                        .Include(e => e.Housing)
                        .FirstOrDefaultAsync(e => e.IqamaNo == cleanIqamaNo);

                    if (employee == null)
                    {
                        employeeNotFound++;
                        failedRecords++;
                        results.Add(new VehicleAssignmentRowResult(
                            rowNumber,
                            false,
                            cleanIqamaNo.ToString(),
                            "N/A", "N/A",
                            cleanPlateNumber,
                            "N/A",
                            false, false,
                            null, null,
                            rowData.Permission,
                            rowData.PermissionStartDate,
                            rowData.PermissionEndDate,
                            warnings,
                            "Employee with this Iqama number not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    bool wasConvertedToRider = false;

                    // Convert employee to rider if needed
                    if (employee.RiderDetails == null)
                    {
                        // Get a default company (or use the first one, or make nullable)
                        var defaultCompany = await _dbcontext.Companies
                            .OrderBy(c => c.Id)
                            .FirstOrDefaultAsync();

                        if (defaultCompany == null)
                        {
                            failedRecords++;
                            results.Add(new VehicleAssignmentRowResult(
                                rowNumber,
                                false,
                                cleanIqamaNo.ToString(),
                                employee.NameEN,
                                employee.NameAR,
                                cleanPlateNumber,
                                "N/A",
                                false, false,
                                null, null,
                                rowData.Permission,
                                rowData.PermissionStartDate,
                                rowData.PermissionEndDate,
                                warnings,
                                "No company found in database - cannot create rider"
                            ));
                            await transaction.RollbackAsync();
                            continue;
                        }

                        var newRider = new RiderDetails
                        {
                            EmployeeIqamaNo = employee.IqamaNo,
                            WorkingId = $"AUTO_{employee.IqamaNo}",
                            TshirtSize = "M",
                            LicenseNumber = "N/A",
                            CompanyId = defaultCompany.Id,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };

                        await _dbcontext.RiderDetails.AddAsync(newRider);
                        await _dbcontext.SaveChangesAsync();

                        employee.RiderDetails = newRider;
                        wasConvertedToRider = true;
                        employeesConvertedToRiders++;
                        warnings.Add($"Employee auto-converted to rider with WorkingId: {newRider.WorkingId}");
                    }

                    // Find vehicle
                    var vehicle = await _dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.PlateNumberA.Replace(" ", "") == cleanPlateNumber);

                    if (vehicle == null)
                    {
                        vehicleNotFound++;
                        failedRecords++;
                        results.Add(new VehicleAssignmentRowResult(
                            rowNumber,
                            false,
                            cleanIqamaNo.ToString(),
                            employee.NameEN,
                            employee.NameAR,
                            cleanPlateNumber,
                            "N/A",
                            wasConvertedToRider, false,
                            null, null,
                            rowData.Permission,
                            rowData.PermissionStartDate,
                            rowData.PermissionEndDate,
                            warnings,
                            $"Vehicle with plate number '{cleanPlateNumber}' not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    var isUnavailable = await _dbcontext.RiderVehicleStatus
                        .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber &&
                                      s.IsActive &&
                                      (s.StatusType == VehicleStatusType.Taken ||
                                       s.StatusType == VehicleStatusType.Problem ||
                                       s.StatusType == VehicleStatusType.Stolen ||
                                       s.StatusType == VehicleStatusType.BreakUp));

                    if (isUnavailable)
                    {
                        vehicleUnavailable++;
                        failedRecords++;

                        var currentStatus = await _dbcontext.RiderVehicleStatus
                            .Where(s => s.VehicleNumber == vehicle.VehicleNumber && s.IsActive)
                            .Select(s => s.StatusType.ToString())
                            .FirstOrDefaultAsync();

                        results.Add(new VehicleAssignmentRowResult(
                            rowNumber,
                            false,
                            cleanIqamaNo.ToString(),
                            employee.NameEN,
                            employee.NameAR,
                            cleanPlateNumber,
                            vehicle.VehicleNumber,
                            wasConvertedToRider, false,
                            null, null,
                            rowData.Permission,
                            rowData.PermissionStartDate,
                            rowData.PermissionEndDate,
                            warnings,
                            $"Vehicle is not available (Status: {currentStatus})"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    if (!string.IsNullOrEmpty(employee.RiderDetails.VehicleNumber))
                    {
                        warnings.Add($"Rider already has vehicle {employee.RiderDetails.VehicleNumber}, will be replaced");

                        // Return old vehicle
                        var oldVehicleStatus = await _dbcontext.RiderVehicleStatus
                            .FirstOrDefaultAsync(s => s.VehicleNumber == employee.RiderDetails.VehicleNumber &&
                                                     s.EmployeeIqamaNo == employee.IqamaNo &&
                                                     s.IsActive &&
                                                     s.StatusType == VehicleStatusType.Taken);

                        if (oldVehicleStatus != null)
                        {
                            oldVehicleStatus.IsActive = false;
                            oldVehicleStatus.PermissionEndDate = DateTime.UtcNow.AddHours(3);

                            _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                            {
                                VehicleNumber = employee.RiderDetails.VehicleNumber,
                                EmployeeIqamaNo = employee.IqamaNo,
                                StatusType = VehicleStatusType.Returned,
                                Reason = "Replaced by bulk import",
                                IsActive = false,
                                Permission = oldVehicleStatus.Permission,
                                PermissionStartDate = oldVehicleStatus.PermissionStartDate,
                                PermissionEndDate = DateTime.UtcNow.AddHours(3),
                                Timestamp = DateTime.UtcNow.AddHours(3)
                            });
                        }
                    }

                    // Handle permission defaults
                    string finalPermission = rowData.Permission ?? "تصريح عام";

                    // Check if permission contains "مرور" (traffic)
                    if (!string.IsNullOrWhiteSpace(rowData.Permission) &&
                        rowData.Permission.Contains("مرور"))
                    {
                        finalPermission = "تصريح مرور";

                        // Set default dates if missing
                        rowData.PermissionStartDate ??= DateTime.UtcNow.AddHours(3);
                        rowData.PermissionEndDate ??= DateTime.UtcNow.AddHours(3).AddDays(30);

                        warnings.Add("Traffic permission detected - using default 30-day period");
                    }

                    // Update vehicle location to housing name
                    string previousLocation = vehicle.Location;
                    string newLocation = employee.Housing?.Name ?? "غير محدد";
                    vehicle.Location = newLocation;

                    // Assign vehicle to rider
                    employee.RiderDetails.VehicleNumber = vehicle.VehicleNumber;

                    // Create vehicle status history
                    _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                    {
                        VehicleNumber = vehicle.VehicleNumber,
                        EmployeeIqamaNo = employee.IqamaNo,
                        StatusType = VehicleStatusType.Taken,
                        Reason = $"Bulk import by {uploadedBy}",
                        IsActive = true,
                        Permission = finalPermission,
                        PermissionStartDate = rowData.PermissionStartDate ?? DateTime.UtcNow.AddHours(3),
                        PermissionEndDate = rowData.PermissionEndDate,
                        Timestamp = DateTime.UtcNow.AddHours(3)
                    });

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulAssignments++;
                    results.Add(new VehicleAssignmentRowResult(
                        rowNumber,
                        true,
                        cleanIqamaNo.ToString(),
                        employee.NameEN,
                        employee.NameAR,
                        cleanPlateNumber,
                        vehicle.VehicleNumber,
                        wasConvertedToRider,
                        true,
                        previousLocation,
                        newLocation,
                        finalPermission,
                        rowData.PermissionStartDate,
                        rowData.PermissionEndDate,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new VehicleAssignmentRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        "N/A", "N/A",
                        "N/A",
                        "N/A",
                        false, false,
                        null, null,
                        null, null, null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new VehicleAssignmentImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulAssignments: successfulAssignments,
                EmployeesConvertedToRiders: employeesConvertedToRiders,
                FailedRecords: failedRecords,
                EmployeeNotFound: employeeNotFound,
                VehicleNotFound: vehicleNotFound,
                VehicleUnavailable: vehicleUnavailable,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleAssignmentImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private IXLRow FindVehicleAssignmentHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة",
        "PlateNumberA", "Plate Number A", "رقم اللوحة", "اللوحة العربية",
        "Permission", "التصريح", "الصلاحية",
        "PermissionStartDate", "تاريخ بداية التصريح",
        "PermissionEndDate", "تاريخ نهاية التصريح"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private VehicleAssignmentColumnMapping BuildVehicleAssignmentColumnMapping(IXLRow headerRow)
    {
        var mapping = new VehicleAssignmentColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "رقم الاقامة", "رقم الإقامة");

        mapping.PlateNumberACol = FindColumn(cells,
            "PlateNumberA", "Plate Number A", "PlateA", "رقم اللوحة", "اللوحة العربية", "اللوحة");

        mapping.PermissionCol = FindColumn(cells,
            "Permission", "التصريح", "الصلاحية", "نوع التصريح");

        mapping.PermissionStartDateCol = FindColumn(cells,
            "PermissionStartDate", "Permission Start Date", "تاريخ بداية التصريح", "تاريخ البداية", "بداية التصريح");

        mapping.PermissionEndDateCol = FindColumn(cells,
            "PermissionEndDate", "Permission End Date", "تاريخ نهاية التصريح", "تاريخ النهاية", "نهاية التصريح");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.PlateNumberACol == 0) missing.Add("Plate Number A");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private VehicleAssignmentRowData ParseVehicleAssignmentRowData(
        IXLRow row,
        VehicleAssignmentColumnMapping map,
        int rowNumber)
    {
        var data = new VehicleAssignmentRowData { RowNumber = rowNumber };

        try
        {
            // Parse and trim IqamaNo
            var iqamaStr = GetCellValue(row, map.IqamaNoCol)?.Replace(" ", "").Trim();
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr, out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            // Parse and trim PlateNumberA
            data.PlateNumberA = GetCellValue(row, map.PlateNumberACol)?.Replace(" ", "").Trim();
            if (string.IsNullOrWhiteSpace(data.PlateNumberA))
            {
                data.IsValid = false;
                data.ErrorMessage = "Plate Number A is required";
                return data;
            }

            // Parse optional fields
            data.Permission = GetCellValue(row, map.PermissionCol)?.Trim();

            // Parse dates
            var startDateStr = GetCellValue(row, map.PermissionStartDateCol);
            if (!string.IsNullOrWhiteSpace(startDateStr))
            {
                if (DateTime.TryParse(startDateStr, out DateTime startDate))
                {
                    data.PermissionStartDate = startDate;
                }
            }

            var endDateStr = GetCellValue(row, map.PermissionEndDateCol);
            if (!string.IsNullOrWhiteSpace(endDateStr))
            {
                if (DateTime.TryParse(endDateStr, out DateTime endDate))
                {
                    data.PermissionEndDate = endDate;
                }
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    internal class VehicleAssignmentColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
        public int PlateNumberACol { get; set; }
        public int PermissionCol { get; set; }
        public int PermissionStartDateCol { get; set; }
        public int PermissionEndDateCol { get; set; }
    }

    internal class VehicleAssignmentRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? IqamaNo { get; set; }
        public string? PlateNumberA { get; set; }
        public string? Permission { get; set; }
        public DateTime? PermissionStartDate { get; set; }
        public DateTime? PermissionEndDate { get; set; }
    }


public async Task<Result<VehicleUsageCheckResponse>> CheckVehicleUsageFromExcelAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<VehicleUsageCheckResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<VehicleUsageCheckResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<VehicleUsageRowResult>();
        var errors = new List<VehicleUsageError>();
        int vehiclesInUse = 0;
        int vehiclesAvailable = 0;
        int vehiclesNotFound = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<VehicleUsageCheckResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindVehicleUsageHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<VehicleUsageCheckResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildVehicleUsageColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<VehicleUsageCheckResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                try
                {
                    var rowData = ParseVehicleUsageRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        errors.Add(new VehicleUsageError(
                            rowNumber,
                            rowData.PlateNumberA ?? "N/A",
                            "ValidationError",
                            rowData.ErrorMessage!
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Normalize plate number - remove all spaces
                    var normalizedPlateNumber = rowData.PlateNumberA!.Replace(" ", "").Trim();

                    // Find vehicle with rider details
                    var vehicle = await _dbcontext.Vehicles
                        .Include(v => v.RiderDetails)
                            .ThenInclude(rd => rd.Employee)
                        .Include(v => v.RiderDetails)
                            .ThenInclude(rd => rd.Company)
                        .FirstOrDefaultAsync(v => v.PlateNumberA.Replace(" ", "") == normalizedPlateNumber);

                    if (vehicle == null)
                    {
                        vehiclesNotFound++;
                        results.Add(new VehicleUsageRowResult(
                            rowNumber,
                            true,
                            rowData.PlateNumberA!,
                            "N/A",
                            "N/A",
                            VehicleUsageStatus.NotFound,
                            null,
                            warnings
                        ));
                        continue;
                    }

                    // Check if vehicle is assigned to a rider
                    if (vehicle.RiderDetails != null)
                    {
                        var employee = vehicle.RiderDetails.Employee;

                        // Validation warnings
                        if (employee.Status != "enable")
                        {
                            warnings.Add($"Rider status is '{employee.Status}' (not enabled)");
                        }

                        if (string.IsNullOrWhiteSpace(vehicle.RiderDetails.WorkingId))
                        {
                            warnings.Add("Rider has no Working ID assigned");
                        }

                        vehiclesInUse++;
                        results.Add(new VehicleUsageRowResult(
                            rowNumber,
                            true,
                            vehicle.PlateNumberA,
                            vehicle.VehicleNumber,
                            vehicle.VehicleType,
                            VehicleUsageStatus.InUse,
                            new RiderUsageInfo(
                                employee.IqamaNo,
                                employee.NameAR,
                                employee.NameEN,
                                vehicle.RiderDetails.WorkingId,
                                vehicle.RiderDetails.Company?.Name ?? "N/A"
                            ),
                            warnings
                        ));
                    }
                    else
                    {
                        vehiclesAvailable++;
                        results.Add(new VehicleUsageRowResult(
                            rowNumber,
                            true,
                            vehicle.PlateNumberA,
                            vehicle.VehicleNumber,
                            vehicle.VehicleType,
                            VehicleUsageStatus.Available,
                            null,
                            warnings
                        ));
                    }
                }
                catch (Exception ex)
                {
                    failedRecords++;
                    errors.Add(new VehicleUsageError(
                        rowNumber,
                        "N/A",
                        "ProcessingError",
                        $"Unexpected error: {ex.Message}"
                    ));
                }
            }

            var response = new VehicleUsageCheckResponse(
                TotalVehicles: dataRows.Count,
                VehiclesInUse: vehiclesInUse,
                VehiclesAvailable: vehiclesAvailable,
                VehiclesNotFound: vehiclesNotFound,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleUsageCheckResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private IXLRow FindVehicleUsageHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "PlateNumber", "Plate Number", "PlateNumberA", "رقم اللوحة",
        "اللوحة", "اللوحة العربية", "Plate A", "Arabic Plate"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 1)
                return row;
        }

        // Fallback to row 1
        return worksheet.Row(1);
    }

    private VehicleUsageColumnMapping BuildVehicleUsageColumnMapping(IXLRow headerRow)
    {
        var mapping = new VehicleUsageColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.PlateNumberACol = FindColumn(cells,
            "PlateNumber", "Plate Number", "PlateNumberA", "Plate Number A",
            "رقم اللوحة", "اللوحة", "اللوحة العربية", "Plate A", "Arabic Plate");

        if (mapping.PlateNumberACol == 0)
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = "Required column 'Plate Number' not found\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private VehicleUsageRowData ParseVehicleUsageRowData(
        IXLRow row,
        VehicleUsageColumnMapping map,
        int rowNumber)
    {
        var data = new VehicleUsageRowData { RowNumber = rowNumber };

        try
        {
            data.PlateNumberA = GetCellValue(row, map.PlateNumberACol)?.Trim();

            if (string.IsNullOrWhiteSpace(data.PlateNumberA))
            {
                data.IsValid = false;
                data.ErrorMessage = "Plate Number is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

// Internal classes for Vehicle Usage Check
internal class VehicleUsageColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int PlateNumberACol { get; set; }
}

internal class VehicleUsageRowData
{
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PlateNumberA { get; set; }
}
}
internal class HousingColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int IqamaNoCol { get; set; }
    public int HousingNameCol { get; set; }
}

internal class HousingRowData
{
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public long? IqamaNo { get; set; }
    public string? HousingName { get; set; }
}
internal class VehicleColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public int VehicleNumberCol { get; set; }
    public int SerialNumberCol { get; set; }
    public int PlateNumberACol { get; set; }
    public int PlateNumberECol { get; set; }

    public int VehicleTypeCol { get; set; }
    public int ManufacturerCol { get; set; }
    public int ManufactureYearCol { get; set; }
    public int LicenseExpiryDateCol { get; set; }
    public int LocationCol { get; set; }
    public int StatusCol { get; set; }
    public int RiderIqamaNoCol { get; set; }
}

internal class VehicleRowData
{
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public string? VehicleNumber { get; set; }
    public int SerialNumber { get; set; }
    public string? PlateNumberA { get; set; }
    public string? PlateNumberE { get; set; }

    public string? VehicleType { get; set; }
    public string? Manufacturer { get; set; }
    public int ManufactureYear { get; set; }
    public DateOnly? LicenseExpiryDate { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }
    public long? RiderIqamaNo { get; set; }

    public string? OwnerName { get; set; }
    public long OwnerId { get; set; }

}

internal class ColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public int IqamaNoCol { get; set; }
    public int NameARCol { get; set; }
    public int NameENCol { get; set; }
    public int IqamaEndMCol { get; set; }
    public int IqamaEndHCol { get; set; }
    public int PassportNoCol { get; set; }
    public int PassportEndCol { get; set; }
    public int SponsorCol { get; set; }
    public int SponsorNoCol { get; set; }
    public int JobTitleCol { get; set; }
    public int CountryCol { get; set; }
    public int PhoneCol { get; set; }
    public int DateOfBirthCol { get; set; }
    public int StatusCol { get; set; }
    public int IBANCol { get; set; }
    public int INKSACol { get; set; }

    public int WorkingIdCol { get; set; }
    public int TshirtSizeCol { get; set; }
    public int LicenseNumberCol { get; set; }
    public int CompanyNameCol { get; set; }
}

internal class RowData
{
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public long IqamaNo { get; set; }
    public string? NameAR { get; set; }
    public string? NameEN { get; set; }
    public DateOnly? IqamaEndM { get; set; }
    public DateOnly? IqamaEndH { get; set; }
    public string? PassportNo { get; set; }
    public DateOnly? PassportEnd { get; set; }
    public string? Sponsor { get; set; }
    public long SponsorNo { get; set; }
    public string? JobTitle { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Status { get; set; }
    public string? IBAN { get; set; }
    public bool INKSA { get; set; }

    public string? WorkingId { get; set; }
    public string? TshirtSize { get; set; }
    public string? LicenseNumber { get; set; }
    public string? CompanyName { get; set; }
    public int? CompanyId { get; set; }
}

