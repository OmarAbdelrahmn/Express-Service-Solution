using Application.Abstraction;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.temp;

public class Temp(ApplicationDbcontext dbcontext) : ITemp
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<Result<BulkUploadResult>> UploadEmployeeExcelAsync(Stream excelStream, string uploadedBy)
    {
        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
                return Result.Failure<BulkUploadResult>(
                    new Error("NoWorksheet", "Excel file contains no worksheets", 400));

            var columnMapping = MapColumns(worksheet);

            if (!columnMapping.ContainsKey("IqamaNo"))
                return Result.Failure<BulkUploadResult>(
                    new Error("MissingIqama", "Excel must contain IqamaNo column", 400));

            // Get all employees from database
            var allEmployees = await dbcontext.Employees
                .AsNoTracking()
                .ToListAsync();

            var tempUpdates = new List<TempEmployeeUpdate>();
            var rowCount = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            int skippedCount = 0;

            // Track all IqamaNo values from Excel
            var excelIqamaNumbers = new HashSet<long>();
            var newEmployeesFromExcel = new List<EmployeeRowInfo>();

            for (int row = 2; row <= rowCount; row++)
            {
                var iqamaNoValue = worksheet.Cell(row, columnMapping["IqamaNo"]).Value.ToString();

                if (string.IsNullOrWhiteSpace(iqamaNoValue) || !long.TryParse(iqamaNoValue, out long IqamaNo))
                    continue;

                excelIqamaNumbers.Add(IqamaNo);

                var existingEmployee = allEmployees.FirstOrDefault(e => e.IqamaNo == IqamaNo);

                // Get new values from Excel
                var newIqamaEndM = GetDateValue(worksheet, row, columnMapping, "IqamaEndM");
                var newIqamaEndH = GetDateValue(worksheet, row, columnMapping, "IqamaEndH");
                var newPassportNo = GetStringValue(worksheet, row, columnMapping, "PassportNo");
                var newPassportEnd = GetDateValue(worksheet, row, columnMapping, "PassportEnd");
                var newSponsor = GetStringValue(worksheet, row, columnMapping, "Sponsor");
                var newSponsorNo = GetLongValue(worksheet, row, columnMapping, "SponsorNo");
                var newJobTitle = GetStringValue(worksheet, row, columnMapping, "JobTitle");
                var newNameAR = GetStringValue(worksheet, row, columnMapping, "NameAR");
                var newNameEN = GetStringValue(worksheet, row, columnMapping, "NameEN");
                var newCountry = GetStringValue(worksheet, row, columnMapping, "Country");
                var newPhone = GetStringValue(worksheet, row, columnMapping, "Phone");
                var newDateOfBirth = GetDateOnlyValue(worksheet, row, columnMapping, "DateOfBirth");
                var newStatus = GetStringValue(worksheet, row, columnMapping, "Status");
                var newIBAN = GetStringValue(worksheet, row, columnMapping, "IBAN");
                var newINKSA = GetBoolValue(worksheet, row, columnMapping, "INKSA");

                // Check if this is a new employee (in Excel but not in DB)
                if (existingEmployee == null)
                {
                    // Store information but DON'T add to TempEmployeeUpdate
                    newEmployeesFromExcel.Add(new EmployeeRowInfo(
                        IqamaNo: IqamaNo,
                        IqamaEndM: newIqamaEndM,
                        IqamaEndH: newIqamaEndH,
                        PassportNo: newPassportNo,
                        PassportEnd: newPassportEnd,
                        Sponsor: newSponsor,
                        SponsorNo: newSponsorNo,
                        JobTitle: newJobTitle,
                        NameAR: newNameAR,
                        NameEN: newNameEN,
                        Country: newCountry,
                        Phone: newPhone,
                        DateOfBirth: newDateOfBirth,
                        Status: newStatus,
                        IBAN: newIBAN,
                        INKSA: newINKSA.HasValue ? !newINKSA.Value : null
                    ));
                    continue;
                }

                var tempUpdateExisting = new TempEmployeeUpdate
                {
                    IqamaNo = IqamaNo,
                    IsNewEmployee = false,
                    UploadedAt = DateTime.UtcNow.AddHours(3),
                    UploadedBy = uploadedBy
                };

                bool hasChanges = false;

                if (HasChanged(existingEmployee.IqamaEndM, newIqamaEndM))
                {
                    tempUpdateExisting.OldIqamaEndM = existingEmployee.IqamaEndM;
                    tempUpdateExisting.NewIqamaEndM = newIqamaEndM;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.IqamaEndH, newIqamaEndH))
                {
                    tempUpdateExisting.OldIqamaEndH = existingEmployee.IqamaEndH;
                    tempUpdateExisting.NewIqamaEndH = newIqamaEndH;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.PassportNo, newPassportNo))
                {
                    tempUpdateExisting.OldPassportNo = existingEmployee.PassportNo;
                    tempUpdateExisting.NewPassportNo = newPassportNo;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.PassportEnd, newPassportEnd))
                {
                    tempUpdateExisting.OldPassportEnd = existingEmployee.PassportEnd;
                    tempUpdateExisting.NewPassportEnd = newPassportEnd;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.Sponsor, newSponsor))
                {
                    tempUpdateExisting.OldSponsor = existingEmployee.Sponsor;
                    tempUpdateExisting.NewSponsor = newSponsor;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.sponsorNo, newSponsorNo))
                {
                    tempUpdateExisting.OldSponsorNo = existingEmployee.sponsorNo;
                    tempUpdateExisting.NewSponsorNo = newSponsorNo;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.JobTitle, newJobTitle))
                {
                    tempUpdateExisting.OldJobTitle = existingEmployee.JobTitle;
                    tempUpdateExisting.NewJobTitle = newJobTitle;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.NameAR, newNameAR))
                {
                    tempUpdateExisting.OldNameAR = existingEmployee.NameAR;
                    tempUpdateExisting.NewNameAR = newNameAR;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.NameEN, newNameEN))
                {
                    tempUpdateExisting.OldNameEN = existingEmployee.NameEN;
                    tempUpdateExisting.NewNameEN = newNameEN;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.Country, newCountry))
                {
                    tempUpdateExisting.OldCountry = existingEmployee.Country;
                    tempUpdateExisting.NewCountry = newCountry;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.Phone, newPhone))
                {
                    tempUpdateExisting.OldPhone = existingEmployee.Phone;
                    tempUpdateExisting.NewPhone = newPhone;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.DateOfBirth, newDateOfBirth))
                {
                    tempUpdateExisting.OldDateOfBirth = existingEmployee.DateOfBirth;
                    tempUpdateExisting.NewDateOfBirth = newDateOfBirth;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.Status, newStatus))
                {
                    tempUpdateExisting.OldStatus = existingEmployee.Status;
                    tempUpdateExisting.NewStatus = newStatus;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.IBAN, newIBAN))
                {
                    tempUpdateExisting.OldIBAN = existingEmployee.IBAN;
                    tempUpdateExisting.NewIBAN = newIBAN;
                    hasChanges = true;
                }

                if (newINKSA.HasValue)
                {
                    var reversedINKSA = !newINKSA.Value;
                    if (HasChanged(existingEmployee.INKSA, reversedINKSA))
                    {
                        tempUpdateExisting.OldINKSA = existingEmployee.INKSA;
                        tempUpdateExisting.NewINKSA = reversedINKSA;
                        hasChanges = true;
                    }
                }

                if (hasChanges)
                {
                    tempUpdates.Add(tempUpdateExisting);
                }
                else
                {
                    skippedCount++;
                }
            }

            // Find employees in DB but not in Excel (missing from Excel)
            var missingFromExcel = allEmployees
                .Where(e => !excelIqamaNumbers.Contains(e.IqamaNo))
                .Select(e => new EmployeeRowInfo(
                    IqamaNo: e.IqamaNo,
                    IqamaEndM: e.IqamaEndM,
                    IqamaEndH: e.IqamaEndH,
                    PassportNo: e.PassportNo,
                    PassportEnd: e.PassportEnd,
                    Sponsor: e.Sponsor,
                    SponsorNo: e.sponsorNo,
                    JobTitle: e.JobTitle,
                    NameAR: e.NameAR,
                    NameEN: e.NameEN,
                    Country: e.Country,
                    Phone: e.Phone,
                    DateOfBirth: e.DateOfBirth,
                    Status: e.Status,
                    IBAN: e.IBAN,
                    INKSA: e.INKSA
                ))
                .ToList();

            // Save only the updates (changes to existing employees)
            if (tempUpdates.Count > 0)
            {
                await dbcontext.TempEmployeeUpdates.AddRangeAsync(tempUpdates);
                await dbcontext.SaveChangesAsync();
            }

            var result = new BulkUploadResult(
                TotalRows: tempUpdates.Count,
                NewEmployees: 0, // We're not creating TempEmployeeUpdate for new employees anymore
                ExistingEmployees: tempUpdates.Count,
                SkippedRows: skippedCount,
                UploadedAt: DateTime.UtcNow.AddHours(3),
                Message: $"Excel uploaded successfully. {tempUpdates.Count} changes detected, {skippedCount} rows skipped (no changes).",
                EmployeesInExcelNotInDB: newEmployeesFromExcel,
                EmployeesInDBNotInExcel: missingFromExcel
            );

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkUploadResult>(
                new Error("UploadError", $"Failed to upload Excel: {ex.Message}", 500));
        }
    }
    public async Task<Result<IEnumerable<TempEmployeeUpdateResponse>>> GetPendingUpdatesAsync()
    {
        try
        {
            var pendingUpdates = await dbcontext.TempEmployeeUpdates
                .Where(t => !t.IsResolved)
                .Include(t => t.Employee)
                .OrderBy(t => t.UploadedAt)
                .ToListAsync();

            var responses = pendingUpdates.Select(MapToResponse).ToList();

            return Result.Success<IEnumerable<TempEmployeeUpdateResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<TempEmployeeUpdateResponse>>(
                new Error("GetPendingError", $"Failed to get pending updates: {ex.Message}", 500));
        }
    }

    public async Task<Result<BulkResolutionResponse>> ResolveUpdatesAsync(BulkResolutionRequest request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            if (request.Resolution != "Approved" && request.Resolution != "Rejected")
                return Result.Failure<BulkResolutionResponse>(
                    new Error("InvalidResolution", "Resolution must be 'Approved' or 'Rejected'", 400));

            var updates = await dbcontext.TempEmployeeUpdates
                .Where(t => !t.IsResolved)
                .ToListAsync();

            if (updates.Count == 0)
                return Result.Failure<BulkResolutionResponse>(
                    new Error("NoUpdates", "No pending updates found with provided IDs", 404));

            var details = new List<string>();
            int successCount = 0;
            int failedCount = 0;

            foreach (var update in updates)
            {
                try
                {
                    if (request.Resolution == "Approved")
                    {
                        if (update.IsNewEmployee)
                        {
                            // Create new employee with all new data
                            var newEmployee = new Employees
                            {
                                IqamaNo = update.IqamaNo,
                                IqamaEndM = update.NewIqamaEndM ?? default,
                                IqamaEndH = update.NewIqamaEndH ?? default,
                                PassportNo = update.NewPassportNo,
                                PassportEnd = update.NewPassportEnd,
                                Sponsor = update.NewSponsor ?? string.Empty,
                                sponsorNo = update.NewSponsorNo ?? 0,
                                JobTitle = update.NewJobTitle ?? string.Empty,
                                NameAR = update.NewNameAR ?? string.Empty,
                                NameEN = update.NewNameEN ?? string.Empty,
                                Country = update.NewCountry ?? string.Empty,
                                Phone = update.NewPhone ?? string.Empty,
                                DateOfBirth = update.NewDateOfBirth ?? default,
                                Status = update.NewStatus ?? string.Empty,
                                IBAN = update.NewIBAN,
                                INKSA = update.NewINKSA ?? true,
                                CreatedAt = DateTime.UtcNow.AddHours(3)
                            };

                            await dbcontext.Employees.AddAsync(newEmployee);
                            details.Add($"Created new employee: {newEmployee.IqamaNo} - {newEmployee.NameEN}");
                        }
                        else
                        {
                            // Update existing employee - only update changed fields
                            var employee = await dbcontext.Employees
                                .FirstOrDefaultAsync(e => e.IqamaNo == update.IqamaNo);

                            if (employee != null)
                            {
                                int changedFields = 0;

                                if (update.NewIqamaEndM.HasValue) { employee.IqamaEndM = update.NewIqamaEndM.Value; changedFields++; }
                                if (update.NewIqamaEndH.HasValue) { employee.IqamaEndH = update.NewIqamaEndH.Value; changedFields++; }
                                if (update.NewPassportNo != null) { employee.PassportNo = update.NewPassportNo; changedFields++; }
                                if (update.NewPassportEnd.HasValue) { employee.PassportEnd = update.NewPassportEnd; changedFields++; }
                                if (update.NewSponsor != null) { employee.Sponsor = update.NewSponsor; changedFields++; }
                                if (update.NewSponsorNo.HasValue) { employee.sponsorNo = update.NewSponsorNo.Value; changedFields++; }
                                if (update.NewJobTitle != null) { employee.JobTitle = update.NewJobTitle; changedFields++; }
                                if (update.NewNameAR != null) { employee.NameAR = update.NewNameAR; changedFields++; }
                                if (update.NewNameEN != null) { employee.NameEN = update.NewNameEN; changedFields++; }
                                if (update.NewCountry != null) { employee.Country = update.NewCountry; changedFields++; }
                                if (update.NewPhone != null) { employee.Phone = update.NewPhone; changedFields++; }
                                if (update.NewDateOfBirth.HasValue) { employee.DateOfBirth = update.NewDateOfBirth.Value; changedFields++; }
                                if (update.NewStatus != null) { employee.Status = update.NewStatus; changedFields++; }
                                if (update.NewIBAN != null) { employee.IBAN = update.NewIBAN; changedFields++; }
                                if (update.NewINKSA.HasValue) { employee.INKSA = update.NewINKSA.Value; changedFields++; }

                                if (changedFields > 0)
                                {
                                    dbcontext.Employees.Update(employee);
                                    details.Add($"Updated employee: {employee.IqamaNo} - {employee.NameEN} ({changedFields} fields)");
                                }
                            }
                        }
                    }
                    else
                    {
                        details.Add($"Rejected update for IqamaNo: {update.IqamaNo}");
                    }

                    update.IsResolved = true;
                    update.Resolution = request.Resolution;
                    update.ResolvedBy = request.ResolvedBy;
                    update.ResolvedAt = DateTime.UtcNow.AddHours(3);

                    successCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    details.Add($"Failed for IqamaNo {update.IqamaNo}: {ex.Message}");
                }
            }

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new BulkResolutionResponse(
                TotalProcessed: updates.Count,
                SuccessCount: successCount,
                FailedCount: failedCount,
                Details: details
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<BulkResolutionResponse>(
                new Error("ResolveError", $"Failed to resolve updates: {ex.Message}", 500));
        }
    }



    private bool HasChanged<T>(T? oldValue, T? newValue)
    {
        return !EqualityComparer<T>.Default.Equals(oldValue, newValue);
    }
    private Dictionary<string, int> MapColumns(IXLWorksheet worksheet)
    {
        var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        var columnNames = new Dictionary<string, string[]>
        {
            { "IqamaNo", new[] { "رقم الاقامة", "Iqama", "ID", "EmployeeID" } },
            { "IqamaEndM", new[] { "تاريخ انتهاء الاقامة", "IqamaExpiryM", "IqamaEndMiladi" } },
            { "IqamaEndH", new[] { "تاريخ انتهاء الاقامة بالهجري", "IqamaExpiryH", "IqamaEndHijri" } },
            { "PassportNo", new[] { "رقم الجواز", "Passport", "PassportNumber" } },
            { "PassportEnd", new[] { "تاريخ انتهاء الجواز", "PassportExpiry", "PassportExpiryDate" } },
            { "Sponsor", new[] { "Sponsor", "Kafeel" } },
            { "SponsorNo", new[] { "رقم صاحب العمل", "Sponsor Number", "KafeelNo" } },
            { "JobTitle", new[] { "المهنة", "Job", "Position" } },
            { "NameAR", new[] { "NameAR", "ArabicName", "Name_AR" } },
            { "NameEN", new[] { "NameEN", "EnglishName", "Name_EN" } },
            { "Country", new[] { "Country", "Nationality" } },
            { "Phone", new[] { "Phone", "Mobile", "PhoneNumber" } },
            { "DateOfBirth", new[] { "تاريخ الميلاد", "DOB", "BirthDate" } },
            { "Status", new[] { "Status", "EmployeeStatus" } },
            { "IBAN", new[] { "IBAN", "BankAccount" } },
            { "INKSA", new[] { "خارج المملكه", "InKSA", "InsideKSA" } }
        };

        for (int col = 1; col <= lastColumn; col++)
        {
            var headerValue = worksheet.Cell(1, col).Value.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(headerValue)) continue;

            foreach (var kvp in columnNames)
            {
                if (kvp.Value.Any(name => name.Equals(headerValue, StringComparison.OrdinalIgnoreCase)))
                {
                    mapping[kvp.Key] = col;
                    break;
                }
            }
        }

        return mapping;
    }

    private string? GetStringValue(IXLWorksheet ws, int row, Dictionary<string, int> mapping, string columnName)
    {
        if (!mapping.ContainsKey(columnName)) return null;
        return ws.Cell(row, mapping[columnName]).Value.ToString()?.Trim();
    }
    private int? GetIntValue(IXLWorksheet ws, int row, Dictionary<string, int> mapping, string columnName)
    {
        if (!mapping.ContainsKey(columnName)) return null;
        var value = ws.Cell(row, mapping[columnName]).Value.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, out int result)) return result;

        return null;
    }

    private long? GetLongValue(IXLWorksheet ws, int row, Dictionary<string, int> mapping, string columnName)
    {
        if (!mapping.ContainsKey(columnName)) return null;
        var value = ws.Cell(row, mapping[columnName]).Value.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (long.TryParse(value, out long result)) return result;
        return null;
    }

    private DateOnly? GetDateValue(IXLWorksheet ws, int row, Dictionary<string, int> mapping, string columnName)
    {
        if (!mapping.ContainsKey(columnName)) return null;
        var cell = ws.Cell(row, mapping[columnName]);

        if (cell.TryGetValue(out DateTime dt))
            return DateOnly.FromDateTime(dt);

        var value = cell.Value.ToString();
        if (!string.IsNullOrWhiteSpace(value) && DateOnly.TryParse(value, out var date))
            return date;

        return null;
    }

    private DateOnly? GetDateOnlyValue(IXLWorksheet ws, int row, Dictionary<string, int> mapping, string columnName)
    {
        if (!mapping.ContainsKey(columnName))
            return null;

        var cell = ws.Cell(row, mapping[columnName]);

        if (cell.TryGetValue(out DateTime dt))
            return DateOnly.FromDateTime(dt);

        var value = cell.Value.ToString();
        if (!string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out var dateTime))
            return DateOnly.FromDateTime(dateTime);

        return null;
    }

    private bool? GetBoolValue(IXLWorksheet ws, int row, Dictionary<string, int> mapping, string columnName)
    {
        if (!mapping.ContainsKey(columnName)) return null;
        var value = ws.Cell(row, mapping[columnName]).Value.ToString()?.ToLower();

        if (value == "true" || value == "yes" || value == "1") return true;
        if (value == "false" || value == "no" || value == "0") return false;

        return null;
    }
    private TempEmployeeUpdateResponse MapToResponse(TempEmployeeUpdate update)
    {
        var changes = new List<FieldChangeInfo>();

        if (update.NewIqamaEndM.HasValue)
            changes.Add(new FieldChangeInfo("IqamaEndM", update.OldIqamaEndM?.ToString(), update.NewIqamaEndM?.ToString()));

        if (update.NewIqamaEndH.HasValue)
            changes.Add(new FieldChangeInfo("IqamaEndH", update.OldIqamaEndH?.ToString(), update.NewIqamaEndH?.ToString()));

        if (update.NewPassportNo != null)
            changes.Add(new FieldChangeInfo("PassportNo", update.OldPassportNo, update.NewPassportNo));

        if (update.NewPassportEnd.HasValue)
            changes.Add(new FieldChangeInfo("PassportEnd", update.OldPassportEnd?.ToString(), update.NewPassportEnd?.ToString()));

        if (update.NewSponsor != null)
            changes.Add(new FieldChangeInfo("Sponsor", update.OldSponsor, update.NewSponsor));

        if (update.NewSponsorNo.HasValue)
            changes.Add(new FieldChangeInfo("SponsorNo", update.OldSponsorNo?.ToString(), update.NewSponsorNo?.ToString()));

        if (update.NewJobTitle != null)
            changes.Add(new FieldChangeInfo("JobTitle", update.OldJobTitle, update.NewJobTitle));

        if (update.NewNameAR != null)
            changes.Add(new FieldChangeInfo("NameAR", update.OldNameAR, update.NewNameAR));

        if (update.NewNameEN != null)
            changes.Add(new FieldChangeInfo("NameEN", update.OldNameEN, update.NewNameEN));

        if (update.NewCountry != null)
            changes.Add(new FieldChangeInfo("Country", update.OldCountry, update.NewCountry));

        if (update.NewPhone != null)
            changes.Add(new FieldChangeInfo("Phone", update.OldPhone, update.NewPhone));

        if (update.NewDateOfBirth.HasValue)
            changes.Add(new FieldChangeInfo("DateOfBirth", update.OldDateOfBirth?.ToString(), update.NewDateOfBirth?.ToString()));

        if (update.NewStatus != null)
            changes.Add(new FieldChangeInfo("Status", update.OldStatus, update.NewStatus));

        if (update.NewIBAN != null)
            changes.Add(new FieldChangeInfo("IBAN", update.OldIBAN, update.NewIBAN));

        if (update.NewINKSA.HasValue)
            changes.Add(new FieldChangeInfo("INKSA", update.OldINKSA?.ToString(), update.NewINKSA?.ToString()));

        return new TempEmployeeUpdateResponse(
            Id: update.Id,
            IqamaNo: update.IqamaNo,
            EmployeeNameAR: update.NewNameAR ?? update.OldNameAR ?? "N/A",
            EmployeeNameEN: update.NewNameEN ?? update.OldNameEN ?? "N/A",
            IsNewEmployee: update.IsNewEmployee,
            Changes: changes,
            UploadedAt: update.UploadedAt,
            UploadedBy: update.UploadedBy,
            IsResolved: update.IsResolved
        );
    }
}

// Updated BulkUploadResult with new properties
public record BulkUploadResult(
    int TotalRows,
    int NewEmployees,
    int ExistingEmployees,
    int SkippedRows,
    DateTime UploadedAt,
    string Message,
    List<EmployeeRowInfo> EmployeesInExcelNotInDB,
    List<EmployeeRowInfo> EmployeesInDBNotInExcel
);
// Response record for individual temp employee update
public record TempEmployeeUpdateResponse(
    int Id,
    long IqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    bool IsNewEmployee,
    List<FieldChangeInfo> Changes,
    DateTime UploadedAt,
    string UploadedBy,
    bool IsResolved
);

// Record for individual field change information
public record FieldChangeInfo(
    string FieldName,
    string? OldValue,
    string? NewValue
);

// Simple request record for bulk resolution
public record BulkResolutionRequest(
    string Resolution, // "Approved" or "Rejected"
    string ResolvedBy
);
public record EBulkResolutionRequest(
    long IqamaNo,
    string Resolution, // "Approved" or "Rejected"
    string ResolvedBy,
    string AdminNot
);

// Response record for bulk resolution
public record BulkResolutionResponse(
    int TotalProcessed,
    int SuccessCount,
    int FailedCount,
    List<string> Details
);
// New record to represent full employee row information
public record EmployeeRowInfo(
    long IqamaNo,
    DateOnly? IqamaEndM,
    DateOnly? IqamaEndH,
    string? PassportNo,
    DateOnly? PassportEnd,
    string? Sponsor,
    long? SponsorNo,
    string? JobTitle,
    string? NameAR,
    string? NameEN,
    string? Country,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Status,
    string? IBAN,
    bool? INKSA
);