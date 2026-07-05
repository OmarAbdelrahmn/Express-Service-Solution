using Application.Contracts.SystemIdPhoneStatuses;
using Application.Service.TransporterShifts;
using ClosedXML.Excel;

namespace Application.Service.SystemIdPhoneStatuses;

public static class SystemIdPhoneStatusExcelParser
{
    private const int PhoneNumberCol = 1;
    private const int SystemIdCol = 2;
    private const int FirstDateCol = 3;
    private const int HeaderRow = 1;

    public static (ImportSystemIdPhoneStatusRequest Request, List<string> Warnings)
        Parse(Stream excelStream, int? overrideYear = null)
    {
        using var workbook = new XLWorkbook(excelStream);
        var sheet = workbook.Worksheet(1)
            ?? throw new InvalidOperationException("The Excel file has no worksheets.");

        var dateColumns = BuildDateColumnMap(sheet, overrideYear, out var headerWarnings);
        if (dateColumns.Count == 0)
            throw new InvalidOperationException(
                "No valid date column headers found. Expected format: \"Sun, 03/May\" starting at column C.");

        var cells = new List<ImportSystemIdPhoneStatusCell>();
        var warnings = new List<string>(headerWarnings);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? HeaderRow;

        for (var row = HeaderRow + 1; row <= lastRow; row++)
        {
            var phoneNumber = sheet.Cell(row, PhoneNumberCol).GetString().Trim();
            var systemId = sheet.Cell(row, SystemIdCol).GetString().Trim();

            if (string.IsNullOrWhiteSpace(phoneNumber) && string.IsNullOrWhiteSpace(systemId))
                continue;

            if (string.IsNullOrWhiteSpace(systemId))
            {
                warnings.Add($"Row {row}: missing system ID. Row skipped.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                warnings.Add($"Row {row}: missing phone number for system ID '{systemId}'. Row skipped.");
                continue;
            }

            foreach (var (colIndex, columnHeader) in dateColumns)
            {
                cells.Add(new ImportSystemIdPhoneStatusCell(
                    SystemId: systemId,
                    PhoneNumber: phoneNumber,
                    ColumnHeader: columnHeader,
                    CellContent: ReadCellText(sheet.Cell(row, colIndex))));
            }
        }

        return (new ImportSystemIdPhoneStatusRequest(cells, overrideYear), warnings);
    }

    private static Dictionary<int, string> BuildDateColumnMap(
        IXLWorksheet sheet,
        int? overrideYear,
        out List<string> warnings)
    {
        warnings = [];
        var map = new Dictionary<int, string>();
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? FirstDateCol - 1;

        for (var col = FirstDateCol; col <= lastCol; col++)
        {
            var header = sheet.Cell(HeaderRow, col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(header))
                continue;

            if (ScheduleHeaderParser.Parse(header, overrideYear) is null)
            {
                warnings.Add($"Column {col} header \"{header}\" could not be parsed as a date - column skipped.");
                continue;
            }

            map[col] = header;
        }

        return map;
    }

    private static string? ReadCellText(IXLCell cell)
    {
        var raw = cell.Value.IsText ? cell.Value.GetText() : cell.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
