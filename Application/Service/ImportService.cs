using Application.Abstraction;
using Application.Service.Empolyee;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Application.Service;

public class ImportService(ApplicationDbcontext dbcontext) : IImportService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<ImportStagingResponse>> ProcessExcelFileAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<ImportStagingResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<ImportStagingResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var batchId = Guid.NewGuid();
        var records = new List<TempEmployeeRiderImport>();
        var criticalErrors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var package = new ExcelPackage(stream);

            var worksheet = package.Workbook.Worksheets[0];
            var rowCount = worksheet.Dimension?.Rows ?? 0;

            if (rowCount <= 1)
                return Result.Failure<ImportStagingResponse>(
                    new Error("EmptyFile", "Excel file has no data rows", 400));

            if (rowCount > 501) // 500 data rows + 1 header
            {
                return Result.Failure<ImportStagingResponse>(
                    new Error("TooManyRows",
                        $"File contains {rowCount - 1} rows. Maximum allowed is 500 rows.", 400));
            }

            // Get company cache for faster lookups
            var companies = await _dbcontext.Companies
                .ToDictionaryAsync(c => c.Name.ToLower(), c => c.Id);

            // Process each row (starting from row 2, assuming row 1 is header)
            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    var record = await ParseExcelRow(worksheet, row, companies, batchId, file.FileName, uploadedBy);
                    records.Add(record);
                }
                catch (Exception ex)
                {
                    criticalErrors.Add($"Row {row}: {ex.Message}");
                }
            }

            // Save all records to temp table
            await _dbcontext.TempEmployeeRiderImports.AddRangeAsync(records);
            await _dbcontext.SaveChangesAsync();

            // Calculate statistics
            var stats = CalculateStatistics(records, criticalErrors);

            return Result.Success(new ImportStagingResponse(
                BatchId: batchId,
                FileName: file.FileName,
                TotalRecords: records.Count,
                ValidRecords: records.Count(r => !r.HasErrors),
                RecordsWithErrors: records.Count(r => r.HasErrors),
                RecordsWithWarnings: records.Count(r => !string.IsNullOrEmpty(r.ValidationWarnings)),
                NewEmployees: records.Count(r => r.IsNewEmployee),
                ExistingEmployees: records.Count(r => !r.IsNewEmployee),
                NewRiders: records.Count(r => r.IsNewRider),
                CriticalErrors: criticalErrors,
                ProcessedAt: DateTime.Now
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<ImportStagingResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private async Task<TempEmployeeRiderImport> ParseExcelRow(
        ExcelWorksheet worksheet,
        int row,
        Dictionary<string, int> companies,
        Guid batchId,
        string fileName,
        string uploadedBy)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Read raw values from Excel (adjust column indexes based on your Excel structure)
        var iqamaNoStr = GetCellValue(worksheet, row, 1); // Column A
        var nameAR = GetCellValue(worksheet, row, 2);      // Column B
        var nameEN = GetCellValue(worksheet, row, 3);      // Column C
        var iqamaEndM = GetCellValue(worksheet, row, 4);   // Column D
        var iqamaEndH = GetCellValue(worksheet, row, 5);   // Column E
        var passportNo = GetCellValue(worksheet, row, 6);  // Column F
        var passportEnd = GetCellValue(worksheet, row, 7); // Column G
        var sponsor = GetCellValue(worksheet, row, 8);     // Column H
        var sponsorNoStr = GetCellValue(worksheet, row, 9); // Column I
        var jobTitle = GetCellValue(worksheet, row, 10);   // Column J
        var country = GetCellValue(worksheet, row, 11);    // Column K
        var phone = GetCellValue(worksheet, row, 12);      // Column L
        var dateOfBirth = GetCellValue(worksheet, row, 13); // Column M
        var status = GetCellValue(worksheet, row, 14);     // Column N
        var iban = GetCellValue(worksheet, row, 15);       // Column O
        var inksaStr = GetCellValue(worksheet, row, 16);   // Column P

        // Rider Details
        var workingId = GetCellValue(worksheet, row, 17);  // Column Q
        var tshirtSize = GetCellValue(worksheet, row, 18); // Column R
        var licenseNumber = GetCellValue(worksheet, row, 19); // Column S
        var companyName = GetCellValue(worksheet, row, 20);   // Column T

        // Parse IqamaNo
        if (!long.TryParse(iqamaNoStr, out long iqamaNo) || iqamaNo <= 0)
            errors.Add("Invalid or missing IqamaNo");

        // Parse SponsorNo
        long? sponsorNo = null;
        if (!string.IsNullOrWhiteSpace(sponsorNoStr))
        {
            if (long.TryParse(sponsorNoStr, out long parsedSponsorNo))
                sponsorNo = parsedSponsorNo;
            else
                warnings.Add("Invalid SponsorNo format");
        }

        // Parse INKSA
        bool inksa = true;
        if (!string.IsNullOrWhiteSpace(inksaStr))
        {
            if (bool.TryParse(inksaStr, out bool parsedInksa))
                inksa = parsedInksa;
            else if (inksaStr.ToLower() == "yes" || inksaStr == "1")
                inksa = true;
            else if (inksaStr.ToLower() == "no" || inksaStr == "0")
                inksa = false;
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(nameAR))
            errors.Add("NameAR is required");
        if (string.IsNullOrWhiteSpace(nameEN))
            errors.Add("NameEN is required");

        // Check if employee exists
        var existingEmployee = await _dbcontext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo);

        bool isNewEmployee = existingEmployee == null;

        // Check if rider exists
        var existingRider = await _dbcontext.RiderDetails
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo);

        bool isNewRider = existingRider == null;

        // Resolve Company
        int? companyId = null;
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            if (companies.TryGetValue(companyName.ToLower(), out int foundCompanyId))
            {
                companyId = foundCompanyId;
            }
            else
            {
                errors.Add($"Company '{companyName}' not found in database");
            }
        }
        else if (!isNewRider)
        {
            // If updating existing rider, company is not required
            warnings.Add("No company specified, will keep existing company");
        }
        else
        {
            errors.Add("Company name is required for new riders");
        }

        // Parse dates
        var parsedIqamaEndM = ParseDate(iqamaEndM, "IqamaEndM", errors, warnings);
        var parsedIqamaEndH = ParseHijriDate(iqamaEndH, "IqamaEndH", errors, warnings);
        var parsedPassportEnd = ParseDate(passportEnd, "PassportEnd", errors, warnings);
        var parsedDateOfBirth = ParseDate(dateOfBirth, "DateOfBirth", errors, warnings);

        // Validate status
        if (!string.IsNullOrWhiteSpace(status) &&
            !EmployeeStatus.IsValid(status))
        {
            errors.Add($"Invalid status '{status}'. Valid: {string.Join(", ", EmployeeStatus.ValidStatuses)}");
        }

        var record = new TempEmployeeRiderImport
        {
            BatchId = batchId,
            FileName = fileName,
            RowNumber = row,
            UploadedBy = uploadedBy,

            // Employee data
            IqamaNo = iqamaNo,
            NameAR = nameAR,
            NameEN = nameEN,
            IqamaEndM = iqamaEndM,
            IqamaEndH = iqamaEndH,
            PassportNo = passportNo,
            PassportEnd = passportEnd,
            Sponsor = sponsor,
            SponsorNo = sponsorNo,
            JobTitle = jobTitle,
            Country = country,
            Phone = phone,
            DateOfBirth = dateOfBirth,
            Status = status,
            IBAN = iban,
            INKSA = inksa,

            // Rider data
            WorkingId = workingId,
            TshirtSize = tshirtSize,
            LicenseNumber = licenseNumber,
            CompanyName = companyName,
            CompanyId = companyId,

            // Parsed dates
            ParsedIqamaEndM = parsedIqamaEndM,
            ParsedIqamaEndH = parsedIqamaEndH,
            ParsedPassportEnd = parsedPassportEnd,
            ParsedDateOfBirth = parsedDateOfBirth,

            // Status
            IsNewEmployee = isNewEmployee,
            IsNewRider = isNewRider,
            HasErrors = errors.Count > 0,
            ValidationErrors = errors.Count > 0 ? JsonSerializer.Serialize(errors) : null,
            ValidationWarnings = warnings.Count > 0 ? JsonSerializer.Serialize(warnings) : null
        };

        return record;
    }

    private string GetCellValue(ExcelWorksheet worksheet, int row, int col)
    {
        var cellValue = worksheet.Cells[row, col].Value;
        return cellValue?.ToString()?.Trim() ?? string.Empty;
    }

    private DateOnly? ParseDate(string dateStr, string fieldName, List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Try multiple date formats
        string[] formats = {
            "dd/MM/yyyy", "d/M/yyyy",
            "MM/dd/yyyy", "M/d/yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd",
            "dd-MM-yyyy", "d-M-yyyy"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime parsedDate))
            {
                return DateOnly.FromDateTime(parsedDate);
            }
        }

        // Try default parsing
        if (DateTime.TryParse(dateStr, out DateTime date))
        {
            return DateOnly.FromDateTime(date);
        }

        warnings.Add($"{fieldName}: Could not parse date '{dateStr}'");
        return null;
    }

    private DateOnly? ParseHijriDate(string hijriDateStr, string fieldName,
        List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(hijriDateStr))
            return null;

        try
        {
            // Parse Hijri date format like "01/04/1447"
            var parts = hijriDateStr.Split('/');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int day) &&
                int.TryParse(parts[1], out int month) &&
                int.TryParse(parts[2], out int year))
            {
                // Create Hijri calendar date
                var hijriCalendar = new System.Globalization.HijriCalendar();

                // Validate Hijri date
                if (year >= 1300 && year <= 1500 &&
                    month >= 1 && month <= 12 &&
                    day >= 1 && day <= hijriCalendar.GetDaysInMonth(year, month))
                {
                    var gregorianDate = hijriCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);
                    return DateOnly.FromDateTime(gregorianDate);
                }
            }

            warnings.Add($"{fieldName}: Invalid Hijri date format '{hijriDateStr}'");
            return null;
        }
        catch (Exception ex)
        {
            warnings.Add($"{fieldName}: Error parsing Hijri date '{hijriDateStr}': {ex.Message}");
            return null;
        }
    }

    private ImportStatisticsResponse CalculateStatistics(
        List<TempEmployeeRiderImport> records,
        List<string> criticalErrors)
    {
        var errorBreakdown = new Dictionary<string, int>();
        var companyBreakdown = new Dictionary<string, int>();

        foreach (var record in records)
        {
            if (!string.IsNullOrEmpty(record.ValidationErrors))
            {
                var errors = JsonSerializer.Deserialize<List<string>>(record.ValidationErrors);
                foreach (var error in errors ?? new List<string>())
                {
                    var errorType = error.Split(':')[0];
                    errorBreakdown[errorType] = errorBreakdown.GetValueOrDefault(errorType) + 1;
                }
            }

            if (!string.IsNullOrEmpty(record.CompanyName))
            {
                companyBreakdown[record.CompanyName] =
                    companyBreakdown.GetValueOrDefault(record.CompanyName) + 1;
            }
        }

        return new ImportStatisticsResponse(
            BatchId: records.FirstOrDefault()?.BatchId ?? Guid.Empty,
            FileName: records.FirstOrDefault()?.FileName ?? "",
            TotalRecords: records.Count,
            ValidRecords: records.Count(r => !r.HasErrors),
            RecordsWithErrors: records.Count(r => r.HasErrors),
            RecordsWithWarnings: records.Count(r => !string.IsNullOrEmpty(r.ValidationWarnings)),
            NewEmployees: records.Count(r => r.IsNewEmployee),
            ExistingEmployees: records.Count(r => !r.IsNewEmployee),
            NewRiders: records.Count(r => r.IsNewRider),
            ResolvedRecords: 0,
            PendingRecords: records.Count,
            ErrorBreakdown: errorBreakdown,
            CompanyBreakdown: companyBreakdown,
            UploadedAt: records.FirstOrDefault()?.UploadedAt ?? DateTime.Now
        );
    }

    public async Task<Result<IEnumerable<TempEmployeeRiderImportResponse>>> GetPendingImportsAsync(
        Guid? batchId = null)
    {
        try
        {
            var query = _dbcontext.TempEmployeeRiderImports
                .Where(t => !t.IsResolved);

            if (batchId.HasValue)
                query = query.Where(t => t.BatchId == batchId.Value);

            var imports = await query
                .OrderBy(t => t.RowNumber)
                .ToListAsync();

            var responses = imports.Select(MapToResponse).ToList();

            return Result.Success<IEnumerable<TempEmployeeRiderImportResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<TempEmployeeRiderImportResponse>>(
                new Error("GetError", $"Failed to get pending imports: {ex.Message}", 500));
        }
    }

    public async Task<Result<ImportStatisticsResponse>> GetImportStatisticsAsync(Guid batchId)
    {
        try
        {
            var records = await _dbcontext.TempEmployeeRiderImports
                .Where(t => t.BatchId == batchId)
                .ToListAsync();

            if (!records.Any())
                return Result.Failure<ImportStatisticsResponse>(
                    new Error("NotFound", "Batch not found", 404));

            var stats = CalculateStatistics(records, new List<string>());
            stats = stats with
            {
                ResolvedRecords = records.Count(r => r.IsResolved),
                PendingRecords = records.Count(r => !r.IsResolved)
            };

            return Result.Success(stats);
        }
        catch (Exception ex)
        {
            return Result.Failure<ImportStatisticsResponse>(
                new Error("GetError", $"Failed to get statistics: {ex.Message}", 500));
        }
    }

    public async Task<Result<ImportResolutionResponse>> ApproveValidRecordsAsync(
        Guid batchId,
        string resolvedBy,
        string? adminNotes = null)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();
        try
        {
            var records = await _dbcontext.TempEmployeeRiderImports
                .Where(t => t.BatchId == batchId && !t.IsResolved && !t.HasErrors)
                .ToListAsync();

            if (!records.Any())
                return Result.Failure<ImportResolutionResponse>(
                    new Error("NoRecords", "No valid records found in batch", 404));

            var response = await ProcessRecords(records, resolvedBy, adminNotes);

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<ImportResolutionResponse>(
                new Error("ApproveError", $"Failed to approve records: {ex.Message}", 500));
        }
    }

    public async Task<Result> RejectBatchAsync(
        Guid batchId,
        string resolvedBy,
        string reason)
    {
        try
        {
            var records = await _dbcontext.TempEmployeeRiderImports
                .Where(t => t.BatchId == batchId && !t.IsResolved)
                .ToListAsync();

            foreach (var record in records)
            {
                record.IsResolved = true;
                record.Resolution = "Rejected";
                record.ResolvedBy = resolvedBy;
                record.ResolvedAt = DateTime.Now;
                record.AdminNotes = reason;
            }

            await _dbcontext.SaveChangesAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("RejectError", $"Failed to reject batch: {ex.Message}", 500));
        }
    }

    public async Task<Result<ImportResolutionResponse>> ApproveSelectedRecordsAsync(
        List<int> recordIds,
        string resolvedBy,
        string? adminNotes = null)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();
        try
        {
            var records = await _dbcontext.TempEmployeeRiderImports
                .Where(t => recordIds.Contains(t.Id) && !t.IsResolved)
                .ToListAsync();

            if (!records.Any())
                return Result.Failure<ImportResolutionResponse>(
                    new Error("NoRecords", "No valid records found", 404));

            var response = await ProcessRecords(records, resolvedBy, adminNotes);

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<ImportResolutionResponse>(
                new Error("ApproveError", $"Failed to approve records: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<ImportBatchSummary>>> GetAllBatchesAsync()
    {
        try
        {
            var batches = await _dbcontext.TempEmployeeRiderImports
                .GroupBy(t => t.BatchId)
                .Select(g => new ImportBatchSummary(
                    BatchId: g.Key,
                    FileName: g.First().FileName ?? "",
                    TotalRecords: g.Count(),
                    ValidRecords: g.Count(t => !t.HasErrors),
                    RecordsWithErrors: g.Count(t => t.HasErrors),
                    IsResolved: g.All(t => t.IsResolved),
                    UploadedAt: g.First().UploadedAt,
                    UploadedBy: g.First().UploadedBy
                ))
                .OrderByDescending(b => b.UploadedAt)
                .ToListAsync();

            return Result.Success<IEnumerable<ImportBatchSummary>>(batches);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<ImportBatchSummary>>(
                new Error("GetError", $"Failed to get batches: {ex.Message}", 500));
        }
    }

    private async Task<ImportResolutionResponse> ProcessRecords(
        List<TempEmployeeRiderImport> records,
        string resolvedBy,
        string? adminNotes)
    {
        int successfulEmployees = 0;
        int successfulRiders = 0;
        int failed = 0;
        var details = new List<string>();
        var errors = new List<string>();

        foreach (var record in records)
        {
            try
            {
                // Process Employee
                var employee = await ProcessEmployee(record);
                if (employee != null)
                {
                    successfulEmployees++;

                    // Process Rider if applicable
                    if (!string.IsNullOrWhiteSpace(record.WorkingId) || record.CompanyId.HasValue)
                    {
                        var riderSuccess = await ProcessRider(record, employee);
                        if (riderSuccess)
                        {
                            successfulRiders++;
                            details.Add($"Row {record.RowNumber}: Employee and Rider created/updated");
                        }
                        else
                        {
                            details.Add($"Row {record.RowNumber}: Employee created but Rider failed");
                        }
                    }
                    else
                    {
                        details.Add($"Row {record.RowNumber}: Employee created (no rider data)");
                    }
                }
                else
                {
                    failed++;
                    errors.Add($"Row {record.RowNumber}: Failed to process employee");
                }

                // Mark as resolved
                record.IsResolved = true;
                record.Resolution = "Approved";
                record.ResolvedBy = resolvedBy;
                record.ResolvedAt = DateTime.Now;
                record.AdminNotes = adminNotes;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Row {record.RowNumber}: {ex.Message}");

                record.IsResolved = true;
                record.Resolution = "Failed";
                record.ResolvedBy = resolvedBy;
                record.ResolvedAt = DateTime.Now;
                record.AdminNotes = ex.Message;
            }
        }

        return new ImportResolutionResponse(
            TotalProcessed: records.Count,
            SuccessfulEmployees: successfulEmployees,
            SuccessfulRiders: successfulRiders,
            Failed: failed,
            Details: details,
            Errors: errors
        );
    }

    private async Task<Employees?> ProcessEmployee(TempEmployeeRiderImport record)
    {
        var employee = await _dbcontext.Employees
            .FirstOrDefaultAsync(e => e.IqamaNo == record.IqamaNo);

        if (employee == null)
        {
            // Create new employee
            employee = new Employees
            {
                IqamaNo = record.IqamaNo,
                NameAR = record.NameAR ?? "",
                NameEN = record.NameEN ?? "",
                IqamaEndM = record.ParsedIqamaEndM ?? DateOnly.MinValue,
                IqamaEndH = record.ParsedIqamaEndH ?? DateOnly.MinValue,
                PassportNo = record.PassportNo,
                PassportEnd = record.ParsedPassportEnd,
                Sponsor = record.Sponsor ?? "",
                sponsorNo = record.SponsorNo ?? 0,
                JobTitle = record.JobTitle ?? "",
                Country = record.Country ?? "",
                Phone = record.Phone ?? "",
                DateOfBirth = record.ParsedDateOfBirth ?? DateOnly.MinValue,
                Status = record.Status ?? "enable",
                IBAN = record.IBAN,
                INKSA = record.INKSA,
                CreatedAt = DateTime.Now
            };

            await _dbcontext.Employees.AddAsync(employee);
        }
        else
        {
            // Update existing employee
            if (record.ParsedIqamaEndM.HasValue)
                employee.IqamaEndM = record.ParsedIqamaEndM.Value;
            if (record.ParsedIqamaEndH.HasValue)
                employee.IqamaEndH = record.ParsedIqamaEndH.Value;
            if (!string.IsNullOrWhiteSpace(record.PassportNo))
                employee.PassportNo = record.PassportNo;
            if (record.ParsedPassportEnd.HasValue)
                employee.PassportEnd = record.ParsedPassportEnd;
            if (!string.IsNullOrWhiteSpace(record.Sponsor))
                employee.Sponsor = record.Sponsor;
            if (record.SponsorNo.HasValue)
                employee.sponsorNo = record.SponsorNo.Value;
            if (!string.IsNullOrWhiteSpace(record.JobTitle))
                employee.JobTitle = record.JobTitle;
            if (!string.IsNullOrWhiteSpace(record.NameAR))
                employee.NameAR = record.NameAR;
            if (!string.IsNullOrWhiteSpace(record.NameEN))
                employee.NameEN = record.NameEN;
            if (!string.IsNullOrWhiteSpace(record.Country))
                employee.Country = record.Country;
            if (!string.IsNullOrWhiteSpace(record.Phone))
                employee.Phone = record.Phone;
            if (record.ParsedDateOfBirth.HasValue)
                employee.DateOfBirth = record.ParsedDateOfBirth.Value;
            if (!string.IsNullOrWhiteSpace(record.Status))
                employee.Status = record.Status;
            if (!string.IsNullOrWhiteSpace(record.IBAN))
                employee.IBAN = record.IBAN;

            employee.INKSA = record.INKSA;
        }

        await _dbcontext.SaveChangesAsync();
        return employee;
    }

    private async Task<bool> ProcessRider(TempEmployeeRiderImport record, Employees employee)
    {
        var rider = await _dbcontext.RiderDetails
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == record.IqamaNo);

        if (rider == null && record.CompanyId.HasValue)
        {
            // Create new rider
            rider = new RiderDetails
            {
                EmployeeIqamaNo = record.IqamaNo,
                WorkingId = record.WorkingId,
                TshirtSize = record.TshirtSize,
                LicenseNumber = record.LicenseNumber,
                CompanyId = record.CompanyId.Value,
                CreatedAt = DateTime.Now
            };

            await _dbcontext.RiderDetails.AddAsync(rider);
        }
        else if (rider != null)
        {
            // Update existing rider
            if (!string.IsNullOrWhiteSpace(record.WorkingId))
                rider.WorkingId = record.WorkingId;
            if (!string.IsNullOrWhiteSpace(record.TshirtSize))
                rider.TshirtSize = record.TshirtSize;
            if (!string.IsNullOrWhiteSpace(record.LicenseNumber))
                rider.LicenseNumber = record.LicenseNumber;
            if (record.CompanyId.HasValue)
                rider.CompanyId = record.CompanyId.Value;
        }

        await _dbcontext.SaveChangesAsync();
        return true;
    }

    private TempEmployeeRiderImportResponse MapToResponse(TempEmployeeRiderImport import)
    {
        var errors = string.IsNullOrEmpty(import.ValidationErrors)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(import.ValidationErrors) ?? new List<string>();

        var warnings = string.IsNullOrEmpty(import.ValidationWarnings)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(import.ValidationWarnings) ?? new List<string>();

        return new TempEmployeeRiderImportResponse(
            Id: import.Id,
            RowNumber: import.RowNumber,
            BatchId: import.BatchId,
            IqamaNo: import.IqamaNo,
            NameAR: import.NameAR,
            NameEN: import.NameEN,
            IqamaEndM: import.IqamaEndM,
            IqamaEndH: import.IqamaEndH,
            Phone: import.Phone,
            Status: import.Status,
            WorkingId: import.WorkingId,
            CompanyName: import.CompanyName,
            CompanyId: import.CompanyId,
            LicenseNumber: import.LicenseNumber,
            IsNewEmployee: import.IsNewEmployee,
            IsNewRider: import.IsNewRider,
            HasErrors: import.HasErrors,
            ValidationErrors: errors,
            ValidationWarnings: warnings,
            UploadedAt: import.UploadedAt,
            UploadedBy: import.UploadedBy
        );
    }

}
