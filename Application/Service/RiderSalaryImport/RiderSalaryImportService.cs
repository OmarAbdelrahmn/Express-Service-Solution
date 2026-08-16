using Application.Abstraction;
using Application.Contracts.RiderSalaryImport;
using ClosedXML.Excel;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Application.Service.RiderSalaryImport;

public class RiderSalaryImportService(ApplicationDbcontext dbcontext) : IRiderSalaryImportService
{
    private const string CompanySponsor = "الخدمة السريعة";

    public async Task<Result<RiderSalaryImportResponse>> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();

            if (worksheet is null || !worksheet.RowsUsed().Any())
            {
                return Result.Failure<RiderSalaryImportResponse>(
                    new Error("RiderSalaryImport.EmptyFile", "The Excel file does not contain any rows.", StatusCodes.Status400BadRequest));
            }

            var headerRow = FindHeaderRow(worksheet);
            if (headerRow is null)
            {
                return Result.Failure<RiderSalaryImportResponse>(
                    new Error("RiderSalaryImport.InvalidColumns", "Required columns IqamaNo and Salary were not found.", StatusCodes.Status400BadRequest));
            }

            var iqamaColumn = FindColumn(headerRow, "iqamano", "iqama", "iqamano", "رقمالاقامة", "الإقامة", "رقمالإقامة");
            var salaryColumn = FindColumn(headerRow, "salary", "salarymoney", "amount", "الراتب", "الراتبالمستحق");

            if (iqamaColumn == 0 || salaryColumn == 0)
            {
                return Result.Failure<RiderSalaryImportResponse>(
                    new Error("RiderSalaryImport.InvalidColumns", "Required columns IqamaNo and Salary were not found.", StatusCodes.Status400BadRequest));
            }

            var rows = new List<ParsedRow>();
            foreach (var row in worksheet.RowsUsed().Where(x => x.RowNumber() > headerRow.RowNumber()))
            {
                var iqamaText = row.Cell(iqamaColumn).GetFormattedString().Trim();
                var salaryText = row.Cell(salaryColumn).GetFormattedString().Trim();

                if (string.IsNullOrWhiteSpace(iqamaText) && string.IsNullOrWhiteSpace(salaryText))
                    continue;

                if (!TryParseIqama(iqamaText, out var iqamaNo))
                {
                    rows.Add(new ParsedRow(row.RowNumber(), null, null, "IqamaNo must be a valid number."));
                    continue;
                }

                if (!TryParseSalary(salaryText, out var salary))
                {
                    rows.Add(new ParsedRow(row.RowNumber(), iqamaNo, null, "Salary must be a valid non-negative amount."));
                    continue;
                }

                rows.Add(new ParsedRow(row.RowNumber(), iqamaNo, salary, null));
            }

            if (rows.Count == 0)
            {
                return Result.Failure<RiderSalaryImportResponse>(
                    new Error("RiderSalaryImport.EmptyFile", "The Excel file does not contain any data rows.", StatusCodes.Status400BadRequest));
            }

            var iqamaNumbers = rows
                .Where(x => x.ErrorMessage is null && x.IqamaNo.HasValue)
                .Select(x => x.IqamaNo!.Value)
                .Distinct()
                .ToList();

            var riders = await dbcontext.Employees
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.RiderDetails != null && iqamaNumbers.Contains(x.IqamaNo))
                .Select(x => new
                {
                    x.IqamaNo,
                    x.NameAR,
                    x.NameEN,
                    x.Sponsor,
                    HousingName = x.Housing != null ? x.Housing.Name : null,
                    x.RiderDetails!.WorkingId,
                    CompanyName = x.RiderDetails.Company.Name
                })
                .ToDictionaryAsync(x => x.IqamaNo, cancellationToken);

            var responseRows = new List<RiderSalaryImportRowResponse>(rows.Count);
            var matchedRiders = 0;
            var ridersNotFound = 0;
            var invalidRows = 0;

            foreach (var row in rows)
            {
                if (row.ErrorMessage is not null)
                {
                    invalidRows++;
                    responseRows.Add(new RiderSalaryImportRowResponse(row.RowNumber, row.IqamaNo, row.Salary, null, row.ErrorMessage));
                    continue;
                }

                if (!riders.TryGetValue(row.IqamaNo!.Value, out var rider))
                {
                    ridersNotFound++;
                    responseRows.Add(new RiderSalaryImportRowResponse(
                        row.RowNumber,
                        row.IqamaNo,
                        row.Salary,
                        null,
                        "Active rider was not found for this IqamaNo."));
                    continue;
                }

                matchedRiders++;
                responseRows.Add(new RiderSalaryImportRowResponse(
                    row.RowNumber,
                    row.IqamaNo,
                    row.Salary,
                    new RiderSalaryRiderResponse(
                        rider.IqamaNo,
                        rider.NameAR,
                        rider.NameEN,
                        rider.Sponsor,
                        string.Equals(rider.Sponsor?.Trim(), CompanySponsor, StringComparison.Ordinal),
                        rider.HousingName,
                        rider.WorkingId,
                        rider.CompanyName),
                    null));
            }

            return Result.Success(new RiderSalaryImportResponse(
                rows.Count,
                matchedRiders,
                ridersNotFound,
                invalidRows,
                responseRows
                    .OrderBy(x => x.Rider is null)
                    .ThenBy(x => x.Rider?.HousingName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Rider?.NameAR ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.RowNumber)
                    .ToList()));
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderSalaryImportResponse>(
                new Error("RiderSalaryImport.ProcessingError", $"Could not read the Excel file: {ex.Message}", StatusCodes.Status400BadRequest));
        }
    }

    private static IXLRow? FindHeaderRow(IXLWorksheet worksheet)
    {
        return worksheet.RowsUsed()
            .Take(10)
            .FirstOrDefault(row =>
                FindColumn(row, "iqamano", "iqama", "iqamano", "رقمالاقامة", "الإقامة", "رقمالإقامة") > 0 &&
                FindColumn(row, "salary", "salarymoney", "amount", "الراتب", "الراتبالمستحق") > 0);
    }

    private static int FindColumn(IXLRow row, params string[] acceptedHeaders)
    {
        foreach (var cell in row.CellsUsed())
        {
            var header = NormalizeHeader(cell.GetFormattedString());
            if (acceptedHeaders.Contains(header, StringComparer.Ordinal))
                return cell.Address.ColumnNumber;
        }

        return 0;
    }

    private static string NormalizeHeader(string value) => new string(value
        .Where(char.IsLetterOrDigit)
        .ToArray())
        .ToLowerInvariant();

    private static bool TryParseIqama(string value, out long iqamaNo)
    {
        var normalized = value.Replace(",", string.Empty).Trim();
        return long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out iqamaNo) && iqamaNo > 0;
    }

    private static bool TryParseSalary(string value, out decimal salary)
    {
        var normalized = value.Replace(",", string.Empty).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out salary) && salary >= 0;
    }

    private sealed record ParsedRow(int RowNumber, long? IqamaNo, decimal? Salary, string? ErrorMessage);
}
