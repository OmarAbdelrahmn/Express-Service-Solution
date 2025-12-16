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

public class ImportService : IImportService
{
    private readonly ApplicationDbcontext _dbcontext;

    public ImportService(ApplicationDbcontext dbcontext)
    {
        _dbcontext = dbcontext;
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
                ProcessedAt: DateTime.Now
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<DirectImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }


    // Replace your BuildColumnMapping method with this version that has better error reporting
    private ColumnMapping BuildColumnMapping(IXLRow headerRow)
    {
        var mapping = new ColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        // Collect all actual headers for debugging
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

                // Handle merged cells
                if (cell.IsMerged())
                {
                    headerValue = cell.MergedRange().FirstCell().GetString().Trim();
                }
                else
                {
                    // Get cell value based on type
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
            // Parse Iqama Number (REQUIRED)
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
            data.Sponsor = GetCellValue(row, map.SponsorCol) ?? "Default Sponsor";

            var sponsorNoStr = GetCellValue(row, map.SponsorNoCol);
            data.SponsorNo = long.TryParse(sponsorNoStr?.Replace(" ", ""), out long sNo) ? sNo : 0;

            data.JobTitle = GetCellValue(row, map.JobTitleCol) ?? "Employee";
            data.Country = GetCellValue(row, map.CountryCol) ?? "Unknown";
            data.Phone = GetCellValue(row, map.PhoneCol) ?? "";
            data.Status = GetCellValue(row, map.StatusCol) ?? "enable";
            data.IBAN = GetCellValue(row, map.IBANCol);

            // Parse INKSA
            var inksaStr = GetCellValue(row, map.INKSACol);
            data.INKSA = string.IsNullOrWhiteSpace(inksaStr) ||
                         inksaStr.ToLower() == "yes" ||
                         inksaStr == "1" ||
                         inksaStr.ToLower() == "true";

            // Rider fields
            data.WorkingId = GetCellValue(row, map.WorkingIdCol);
            data.TshirtSize = GetCellValue(row, map.TshirtSizeCol);
            data.LicenseNumber = GetCellValue(row, map.LicenseNumberCol);
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

    // Replace your FindHeaderRow method with this improved version
    private IXLRow FindHeaderRow(IXLWorksheet worksheet)
    {
        // Known required column names to look for
        var knownColumns = new[]
        {
        "NameEN", "Name EN", "NameAR", "Name AR",
        "IqamaNumber", "Iqama Number", "رقم الإقامة", "رقم الاقامة",
        "Phone", "Sponsor", "Country"
    };

        // Check first 10 rows to find the one that contains our known columns
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

            // Check if this row contains any of our known column names
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

            // If we found at least 3 matching column names, this is likely the header row
            if (matchCount >= 3)
            {
                Console.WriteLine($"Found header row at row {i} with {matchCount} matching columns");
                return row;
            }
        }

        // Fallback: return the row with the most non-empty cells
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

            // Handle merged cells
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

            // Handle different cell types
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

            // Fallback: try to get string representation
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

        // Try multiple formats
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

        // Try general parsing
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
            // Expected format: DD/MM/YYYY (Hijri)
            var parts = hijriDateStr.Split('/', '-');
            if (parts.Length != 3)
                return null;

            if (!int.TryParse(parts[0], out int day) ||
                !int.TryParse(parts[1], out int month) ||
                !int.TryParse(parts[2], out int year))
                return null;

            // Validate Hijri date ranges
            if (year < 1300 || year > 1500 ||
                month < 1 || month > 12 ||
                day < 1 || day > 30)
                return null;

            var hijriCalendar = new HijriCalendar();

            // Ensure the day is valid for the month
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
                // Create new employee
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
                    CreatedAt = DateTime.Now
                };

                await _dbcontext.Employees.AddAsync(employee);
                await _dbcontext.SaveChangesAsync();

                return (true, false, null);
            }
            else
            {
                // Update existing employee
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
                // Create new rider only if we have at least WorkingId or CompanyId
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
                    CreatedAt = DateTime.Now
                };

                await _dbcontext.RiderDetails.AddAsync(rider);
                await _dbcontext.SaveChangesAsync();

                return (true, false, null);
            }
            else
            {
                // Update existing rider
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
}

// Helper classes
internal class ColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    // Employee columns
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

    // Rider columns
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

    // Employee data
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

    // Rider data
    public string? WorkingId { get; set; }
    public string? TshirtSize { get; set; }
    public string? LicenseNumber { get; set; }
    public string? CompanyName { get; set; }
    public int? CompanyId { get; set; }
}

