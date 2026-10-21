using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Application.Abstraction;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

namespace Application.Service.Empolyee;

internal static class VehicleDocumentExpirySheetParser
{
    internal const int MaxRows = 5000;
    internal const long MaxFileBytes = 10 * 1024 * 1024;
    private const long MaxExpandedBytes = 100 * 1024 * 1024;
    private static readonly string[] SerialHeaders = ["الرقم التسلسلي", "الرقم التسلسلى", "SerialNumber", "Serial Number", "Serial No"];
    private static readonly string[] LicenseHeaders =
        ["تاريخ انتهاء رخصة السير", "تاريخ انتهاء الاستمارة", "تاريخ انتهاء الرخصة", "LicenseExpiryDate", "License Expiry Date", "Registration Expiry Date"];
    private static readonly string[] InspectionHeaders =
        ["تاريخ انتهاء الفحص", "تاريخ انتهاء الفحص الدوري", "InspectionExpiryDate", "Inspection Expiry Date"];

    internal static Result<ParsedSheet> Parse(Stream input, string? worksheetName, CancellationToken ct)
    {
        try
        {
            if (!input.CanSeek || input.Length == 0 || input.Length > MaxFileBytes)
                return Fail("Provide a non-empty .xlsx file up to 10 MB.");

            input.Position = 0;
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (archive.Entries.Count > 5000 || archive.Entries.Sum(e => e.Length) > MaxExpandedBytes)
                    return Fail("The workbook exceeds the supported expanded size.");
            }
            input.Position = 0;
            ct.ThrowIfCancellationRequested();
            using var workbook = new XLWorkbook(input);
            var sheets = workbook.Worksheets.Where(s => worksheetName is null ||
                string.Equals(s.Name, worksheetName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sheets.Count == 0)
                return Fail("The requested worksheet was not found.");

            var candidates = new List<(IXLWorksheet Sheet, Header Header)>();
            foreach (var sheet in sheets)
            {
                ct.ThrowIfCancellationRequested();
                for (var row = 1; row <= Math.Min(20, sheet.LastRowUsed()?.RowNumber() ?? 0); row++)
                {
                    var cells = sheet.Row(row).CellsUsed().ToList();
                    var serials = FindColumns(cells, SerialHeaders);
                    var licenses = FindColumns(cells, LicenseHeaders);
                    var inspections = FindColumns(cells, InspectionHeaders);
                    if (serials.Count == 0 || licenses.Count + inspections.Count == 0) continue;
                    if (serials.Count > 1 || licenses.Count > 1 || inspections.Count > 1)
                        return Fail($"Worksheet '{sheet.Name}' has duplicate document/serial headers on row {row}.");
                    candidates.Add((sheet, new Header(row, serials[0], licenses.SingleOrDefault(), inspections.SingleOrDefault())));
                    break;
                }
            }

            if (candidates.Count == 0)
                return Fail("A header row within the first 20 rows must contain SerialNumber / الرقم التسلسلي and a registration or inspection expiry column.");
            if (candidates.Count > 1)
                return Fail("Multiple matching worksheets were found. Select one using worksheetName.");

            var (selected, header) = candidates[0];
            var lastRow = selected.LastRowUsed()?.RowNumber() ?? header.Row;
            if (lastRow - header.Row > MaxRows)
                return Fail($"The worksheet may contain at most {MaxRows} data rows.");

            var rows = new List<ParsedRow>();
            for (var number = header.Row + 1; number <= lastRow; number++)
            {
                ct.ThrowIfCancellationRequested();
                var serialCell = selected.Cell(number, header.Serial);
                var licenseCell = header.License == 0 ? null : selected.Cell(number, header.License);
                var inspectionCell = header.Inspection == 0 ? null : selected.Cell(number, header.Inspection);
                if (serialCell.IsEmpty() && (licenseCell?.IsEmpty() ?? true) && (inspectionCell?.IsEmpty() ?? true)) continue;

                var errors = new List<string>();
                var serial = ReadSerial(serialCell);
                if (serial is null) errors.Add("SerialNumber must be a positive whole number within the supported integer range.");
                var license = ReadDate(licenseCell, workbook.Use1904DateSystem, "registration", errors);
                var inspection = ReadDate(inspectionCell, workbook.Use1904DateSystem, "inspection", errors);
                rows.Add(new ParsedRow(number, serial, license, inspection, errors));
            }
            if (rows.Count == 0) return Fail("The worksheet contains no document data rows.");

            // Reject every occurrence rather than choosing a winner based on row order.
            foreach (var group in rows.Where(r => r.Serial.HasValue).GroupBy(r => r.Serial).Where(g => g.Count() > 1))
                foreach (var row in group)
                    row.Errors.Add("SerialNumber is repeated in this worksheet; all occurrences were skipped.");

            return Result.Success(new ParsedSheet(selected.Name, rows));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or
            FormatException or InvalidOperationException or OpenXmlPackageException or System.Xml.XmlException)
        {
            return Fail("The file is not a readable, unencrypted .xlsx workbook.");
        }
    }

    private static List<int> FindColumns(List<IXLCell> cells, string[] names)
        => cells.Where(c => !c.HasFormula && names.Any(n => NormalizeHeader(n) == NormalizeHeader(c.GetString())))
            .Select(c => c.Address.ColumnNumber).ToList();

    private static string NormalizeHeader(string value) => new string(value.Normalize(NormalizationForm.FormKC)
        .Where(c => !char.IsWhiteSpace(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format && c != '_').ToArray()).ToUpperInvariant();

    private static string NormalizeDigits(string value) => new(value.Trim()
        .Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.Format)
        .Select(c => char.GetUnicodeCategory(c) == UnicodeCategory.DecimalDigitNumber
            ? (char)('0' + (int)char.GetNumericValue(c)) : c).ToArray());

    private static int? ReadSerial(IXLCell cell)
    {
        if (cell.HasFormula) return null;
        if (cell.DataType == XLDataType.Number)
        {
            var number = cell.GetDouble();
            return number > 0 && number <= int.MaxValue && number == Math.Truncate(number) ? (int)number : null;
        }
        var text = NormalizeDigits(cell.GetString());
        return text.Length > 0 && text.All(char.IsAsciiDigit) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) && serial > 0 ? serial : null;
    }

    private static ParsedDate? ReadDate(IXLCell? cell, bool use1904, string label, List<string> errors)
    {
        if (cell is null || cell.IsEmpty()) return null;
        if (cell.HasFormula)
        {
            errors.Add($"The {label} date must be a value, not a formula.");
            return null;
        }
        var source = cell.GetString().Trim();
        if (source is "" or "-" or "—" or "–") return null;
        try
        {
            if (cell.DataType == XLDataType.DateTime)
                return new ParsedDate(DateOnly.FromDateTime(cell.GetDateTime()), source, "Gregorian");
            if (cell.DataType == XLDataType.Number)
            {
                var value = cell.GetDouble();
                var date = DateOnly.FromDateTime(DateTime.FromOADate(value + (use1904 ? 1462 : 0)));
                if (value <= 0 || date.Year < 1900) throw new FormatException();
                return new ParsedDate(date, source, "Gregorian");
            }
            var text = NormalizeDigits(source);
            var match = Regex.Match(text, @"^(\d{1,4})[-/](\d{1,2})[-/](\d{1,4})$", RegexOptions.CultureInvariant);
            if (!match.Success) throw new FormatException();
            var first = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var last = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var yearFirst = match.Groups[1].Value.Length == 4;
            var year = yearFirst ? first : last;
            var day = yearFirst ? last : first;
            if (year is >= 1300 and <= 1600)
                return new ParsedDate(DateOnly.FromDateTime(new UmAlQuraCalendar().ToDateTime(year, month, day, 0, 0, 0, 0)), source, "Hijri");
            if (year < 1900) throw new FormatException();
            return new ParsedDate(new DateOnly(year, month, day), source, "Gregorian");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            errors.Add($"Invalid {label} date. Use a valid yyyy-MM-dd or dd/MM/yyyy Gregorian date, a supported Umm al-Qura Hijri date, or an Excel date cell.");
            return null;
        }
    }

    private static Result<ParsedSheet> Fail(string message)
        => Result.Failure<ParsedSheet>(new Error("VehicleDocuments.InvalidSheet", message, 400));

    private sealed record Header(int Row, int Serial, int License, int Inspection);
    internal sealed record ParsedDate(DateOnly Value, string SourceValue, string Calendar);
    internal sealed record ParsedRow(int RowNumber, int? Serial, ParsedDate? License, ParsedDate? Inspection, List<string> Errors);
    internal sealed record ParsedSheet(string Name, List<ParsedRow> Rows);
}
