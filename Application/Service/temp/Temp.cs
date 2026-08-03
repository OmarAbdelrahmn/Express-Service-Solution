using Application.Abstraction;
using Application.Service.Empolyee;
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
           
            // NEW — scope only to this sponsor
            var allEmployees = await dbcontext.Employees
                .AsNoTracking()
                .Where(e => e.Sponsor == "الخدمة السريعة"&& !e.IsDeleted)
                .ToListAsync();

            int totalInDB = allEmployees.Count;

            var tempUpdates = new List<TempEmployeeUpdate>();
            var rowCount = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            int skippedCount = 0;
            var exitReturnNotes = new List<string>();
            var directUpdateItems = new List<(long IqamaNo, DateOnly? NewIqamaEndH, long? NewSponsorNo, string? NewJobTitle, string? NewStatus)>();
            int excelValidRowCount = 0;

            // Track all IqamaNo values from Excel
            var excelIqamaNumbers = new HashSet<long>();
            var newEmployeesFromExcel = new List<EmployeeRowInfo>();

            for (int row = 2; row <= rowCount; row++)
            {
                var iqamaNoValue = worksheet.Cell(row, columnMapping["IqamaNo"]).Value.ToString();

                if (string.IsNullOrWhiteSpace(iqamaNoValue) || !long.TryParse(iqamaNoValue, out long IqamaNo))
                    continue;

                excelIqamaNumbers.Add(IqamaNo);

                excelValidRowCount++;

                var exitReturnValue = GetStringValue(worksheet, row, columnMapping, "ExitReturn")?.Trim();

                var existingEmployee = allEmployees.FirstOrDefault(e => e.IqamaNo == IqamaNo);

                var newIqamaEndH = GetDateValue(worksheet, row, columnMapping, "IqamaEndH");
                var newPassportNo = GetStringValue(worksheet, row, columnMapping, "PassportNo");
                var newPassportEnd = GetDateValue(worksheet, row, columnMapping, "PassportEnd");
                var newSponsorNo = GetLongValue(worksheet, row, columnMapping, "SponsorNo");
                var newJobTitle = GetStringValue(worksheet, row, columnMapping, "JobTitle");

                // Check if this is a new employee (in Excel but not in DB)
                if (existingEmployee == null)
                {
                    newEmployeesFromExcel.Add(new EmployeeRowInfo(
                        IqamaNo: IqamaNo,
                        IqamaEndM: null,
                        IqamaEndH: newIqamaEndH,
                        PassportNo: newPassportNo,
                        PassportEnd: newPassportEnd,
                        Sponsor: null,
                        SponsorNo: newSponsorNo,
                        JobTitle: newJobTitle,
                        NameAR: null,
                        NameEN: null,
                        Country: null,
                        Phone: null,
                        DateOfBirth: null,
                        Status: null,
                        IBAN: null,
                        INKSA: null
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


                // Direct-update fields — bypass temp table entirely
                bool needsDirect = false;
                DateOnly? directIqamaEndH = null;
                long? directSponsorNo = null;
                string? directJobTitle = null;
                string? directStatus = null;

                if (newIqamaEndH.HasValue && HasChanged(existingEmployee.IqamaEndH, newIqamaEndH.Value))
                {
                    directIqamaEndH = newIqamaEndH;
                    needsDirect = true;
                }

                if (newSponsorNo.HasValue && HasChanged(existingEmployee.sponsorNo, newSponsorNo.Value))
                {
                    directSponsorNo = newSponsorNo;
                    needsDirect = true;
                }

                if (!string.IsNullOrWhiteSpace(newJobTitle) && HasChanged(existingEmployee.JobTitle, newJobTitle))
                {
                    directJobTitle = newJobTitle;
                    needsDirect = true;
                }

                // خروج وعودة controls the employee status directly:
                // نعم = إجازة, لا = معطل.
                if (!string.IsNullOrWhiteSpace(exitReturnValue))
                {
                    bool excelSaysVacation = exitReturnValue.Equals("نعم", StringComparison.OrdinalIgnoreCase);
                    bool excelSaysNoExitReturn = exitReturnValue.Equals("لا", StringComparison.OrdinalIgnoreCase);
                    bool dbIsVacation = existingEmployee.Status?.Equals("vacation", StringComparison.OrdinalIgnoreCase) == true;

                    if ((excelSaysVacation || excelSaysNoExitReturn) && excelSaysVacation != dbIsVacation)
                    {
                        directStatus = excelSaysVacation ? EmployeeStatus.Vacation : EmployeeStatus.Disable;
                        needsDirect = true;

                        string message = excelSaysVacation
                            ? $"تم تغيير الحالة في النظام من '{existingEmployee.Status}' إلى 'إجازة' لأن خروج وعودة في Excel = نعم."
                            : $"تم تغيير الحالة في النظام من 'إجازة' إلى 'معطل' لأن خروج وعودة في Excel = لا.";

                        exitReturnNotes.Add(
                            $"({existingEmployee.NameAR}): {message}"
                        );
                    }
                }

                if (needsDirect)
                    directUpdateItems.Add((IqamaNo, directIqamaEndH, directSponsorNo, directJobTitle, directStatus));


                if (HasChanged(existingEmployee.PassportEnd, newPassportEnd))
                {
                    tempUpdateExisting.OldPassportEnd = existingEmployee.PassportEnd;
                    tempUpdateExisting.NewPassportEnd = newPassportEnd;
                    hasChanges = true;
                }

                if (HasChanged(existingEmployee.sponsorNo, newSponsorNo))
                {
                    tempUpdateExisting.OldSponsorNo = existingEmployee.sponsorNo;
                    tempUpdateExisting.NewSponsorNo = newSponsorNo;
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    tempUpdates.Add(tempUpdateExisting);
                }
                else if (!needsDirect)
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

            // Apply direct updates immediately
            var directlyUpdatedInfos = new List<DirectUpdateInfo>();

            if (directUpdateItems.Count > 0)
            {
                var iqamaNosToUpdate = directUpdateItems.Select(d => d.IqamaNo).ToList();
                var employeesToUpdate = await dbcontext.Employees
                    .Where(e => iqamaNosToUpdate.Contains(e.IqamaNo))
                    .ToListAsync();

                foreach (var item in directUpdateItems)
                {
                    var emp = employeesToUpdate.FirstOrDefault(e => e.IqamaNo == item.IqamaNo);
                    if (emp == null) continue;

                    var changedFields = new List<string>();

                    if (item.NewIqamaEndH.HasValue)
                    {
                        changedFields.Add($"تاريخ انتهاء الإقامة بالهجري: {emp.IqamaEndH} ← {item.NewIqamaEndH.Value}");
                        emp.IqamaEndH = item.NewIqamaEndH.Value;
                        emp.IqamaEndM = HijriToGregorian(item.NewIqamaEndH.Value);
                    }

                    if (item.NewSponsorNo.HasValue)
                    {
                        changedFields.Add($"رقم صاحب العمل: {emp.sponsorNo} ← {item.NewSponsorNo.Value}");
                        emp.sponsorNo = item.NewSponsorNo.Value;
                    }

                    if (item.NewJobTitle != null)
                    {
                        changedFields.Add($"المهنة: '{emp.JobTitle}' ← '{item.NewJobTitle}'");
                        emp.JobTitle = item.NewJobTitle;
                    }

                    if (item.NewStatus != null)
                    {
                        changedFields.Add($"الحالة: '{emp.Status}' ← '{item.NewStatus}'");
                        dbcontext.EmployeeStatusLogs.Add(new EmployeeStatusLog
                        {
                            EmployeeIqamaNo = emp.IqamaNo,
                            OldStatus = emp.Status,
                            NewStatus = item.NewStatus,
                            ChangedBy = uploadedBy,
                            ChangedAt = DateTime.UtcNow.AddHours(3),
                            Reason = "تحديث تلقائي من ملف Excel بناءً على خروج وعودة.",
                            ChangeSource = "ExcelImport"
                        });
                        emp.Status = item.NewStatus;
                    }

                    emp.UpdatedAt = DateTime.UtcNow.AddHours(3);
                    directlyUpdatedInfos.Add(new DirectUpdateInfo(item.IqamaNo, emp.NameEN, changedFields));
                }

                await dbcontext.SaveChangesAsync();
            }

            // Save only the updates (changes to existing employees)
            if (tempUpdates.Count > 0)
            {
                await dbcontext.TempEmployeeUpdates.AddRangeAsync(tempUpdates);
                await dbcontext.SaveChangesAsync();
            }


            string countNote = excelValidRowCount == totalInDB
                ? $"عدد الموظفين في Excel وقاعدة البيانات متطابق: {totalInDB} موظف لدى الكفيل 'الخدمة السريعة'."
                : excelValidRowCount > totalInDB
                    ? $"Excel يحتوي على {excelValidRowCount} موظف بينما قاعدة البيانات تحتوي على {totalInDB} — تم العثور على {excelValidRowCount - totalInDB} موظف جديد في Excel غير موجود في النظام."
                    : $"قاعدة البيانات تحتوي على {totalInDB} موظف بينما Excel يحتوي على {excelValidRowCount} — يبدو أن {totalInDB - excelValidRowCount} موظف قد غادر أو سقط من النظام (راجع قائمة الموظفين الغائبين عن Excel).";

            var result = new BulkUploadResult(
                TotalInDB: totalInDB,
                TotalInExcel: excelValidRowCount,
                TotalPendingApproval: tempUpdates.Count,
                DirectlyUpdated: directlyUpdatedInfos,
                SkippedRows: skippedCount,
                UploadedAt: DateTime.UtcNow.AddHours(3),
                Message: $"{countNote} تم تحديث {directlyUpdatedInfos.Count} سجل مباشرةً. {tempUpdates.Count} تغيير بانتظار الموافقة. تم تجاهل {skippedCount} صف (لا يوجد تغيير).",
                EmployeesInExcelNotInDB: newEmployeesFromExcel,
                EmployeesInDBNotInExcel: missingFromExcel,
                ExitReturnNotes: exitReturnNotes
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
                                if (update.NewIqamaEndH.HasValue) { employee.IqamaEndH = update.NewIqamaEndH.Value; changedFields++; }
                                if (update.NewIqamaEndM.HasValue) { employee.IqamaEndM = update.NewIqamaEndM.Value; changedFields++; }
                                if (update.NewPassportNo != null) { employee.PassportNo = update.NewPassportNo; changedFields++; }
                                if (update.NewPassportEnd.HasValue) { employee.PassportEnd = update.NewPassportEnd; changedFields++; }
                                if (update.NewSponsorNo.HasValue) { employee.sponsorNo = update.NewSponsorNo.Value; changedFields++; }
                                if (update.NewJobTitle != null) { employee.JobTitle = update.NewJobTitle; changedFields++; }

                                if (changedFields > 0)
                                {
                                    dbcontext.Employees.Update(employee);
                                    details.Add($"تم تحديث الموظف: {employee.IqamaNo} - {employee.NameEN} ({changedFields} حقول)");
                                }
                            }
                        }
                    }
                    else
                    {
                        details.Add($"تم رفض التحديث لرقم الإقامة: {update.IqamaNo}");
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
                    details.Add($"فشلت العملية لرقم الإقامة {update.IqamaNo}: {ex.Message}");
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

    private static DateOnly HijriToGregorian(DateOnly hijriDate)
    {
        var hijriCalendar = new System.Globalization.UmAlQuraCalendar();
        var gregorianDateTime = hijriCalendar.ToDateTime(
            hijriDate.Year, hijriDate.Month, hijriDate.Day, 0, 0, 0, 0);
        return DateOnly.FromDateTime(gregorianDateTime);
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
            { "IqamaNo",    new[] { "رقم الاقامة" } },
            { "IqamaEndH",  new[] { "تاريخ انتهاء الاقامة بالهجري" } },
            { "PassportNo", new[] { "رقم الجواز" } },
            { "PassportEnd",new[] { "تاريخ انتهاء الجواز" } },
            { "SponsorNo",  new[] { "رقم صاحب العمل" } },
            { "JobTitle",   new[] { "المهنة" } },
            { "ExitReturn", new[] { "خارج المملكه"} },

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
        if (update.NewIqamaEndH.HasValue)
            changes.Add(new FieldChangeInfo("تاريخ انتهاء الاقامة بالهجري", update.OldIqamaEndH?.ToString(), update.NewIqamaEndH?.ToString()));

        if (update.NewIqamaEndM.HasValue)
            changes.Add(new FieldChangeInfo("تاريخ انتهاء الاقامة ميلادي", update.OldIqamaEndM?.ToString(), update.NewIqamaEndM?.ToString()));

        if (update.NewPassportNo != null)
            changes.Add(new FieldChangeInfo("رقم الجواز", update.OldPassportNo, update.NewPassportNo));

        if (update.NewPassportEnd.HasValue)
            changes.Add(new FieldChangeInfo("تاريخ انتهاء الجواز", update.OldPassportEnd?.ToString(), update.NewPassportEnd?.ToString()));

        if (update.NewSponsorNo.HasValue)
            changes.Add(new FieldChangeInfo("رقم صاحب العمل", update.OldSponsorNo?.ToString(), update.NewSponsorNo?.ToString()));

        if (update.NewJobTitle != null)
            changes.Add(new FieldChangeInfo("المهنة", update.OldJobTitle, update.NewJobTitle));

        return new TempEmployeeUpdateResponse(
            Id: update.Id,
            IqamaNo: update.IqamaNo,
            EmployeeNameAR: update.Employee?.NameAR ?? "N/A",
            EmployeeNameEN: update.Employee?.NameEN ?? "N/A",
            IsNewEmployee: update.IsNewEmployee,
            Changes: changes,
            UploadedAt: update.UploadedAt,
            UploadedBy: update.UploadedBy ?? string.Empty,
            IsResolved: update.IsResolved
        );
    }
}

// Updated BulkUploadResult with new properties
// NEW
public record BulkUploadResult(
    int TotalInDB,                                      // Employees in DB with sponsor الخدمة السريعة
    int TotalInExcel,                                   // Valid rows found in Excel
    int TotalPendingApproval,                           // Temp records needing review (PassportNo/End)
    List<DirectUpdateInfo> DirectlyUpdated,             // IqamaEndH / SponsorNo / JobTitle / Status applied immediately
    int SkippedRows,
    DateTime UploadedAt,
    string Message,
    List<EmployeeRowInfo> EmployeesInExcelNotInDB,
    List<EmployeeRowInfo> EmployeesInDBNotInExcel,
    List<string> ExitReturnNotes                        // Arabic notes for status updates caused by خروج وعودة
);

public record DirectUpdateInfo(
    long IqamaNo,
    string EmployeeNameEN,
    List<string> ChangedFields                          // e.g. ["IqamaEndH: 1446/01/01 → 1447/01/01"]
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
