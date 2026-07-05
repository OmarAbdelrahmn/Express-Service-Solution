using Application.Contracts.SystemIdPhoneStatuses;
using ClosedXML.Excel;

namespace Application.Service.SystemIdPhoneStatuses;

public static class SystemIdPhoneStatusExcelParser
{
    private const int PhoneNumberCol = 1;
    private const int SystemIdCol = 2;
    private const int StatusCol = 3;
    private const int HeaderRow = 1;

    public static (ImportSystemIdPhoneStatusRequest Request, List<string> Warnings)
        Parse(Stream excelStream, DateOnly statusDate)
    {
        using var workbook = new XLWorkbook(excelStream);
        var sheet = workbook.Worksheet(1)
            ?? throw new InvalidOperationException("The Excel file has no worksheets.");

        var cells = new List<ImportSystemIdPhoneStatusCell>();
        var warnings = new List<string>();
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

            cells.Add(new ImportSystemIdPhoneStatusCell(
                SystemId: systemId,
                PhoneNumber: phoneNumber,
                Status: ReadCellText(sheet.Cell(row, StatusCol))));
        }

        return (new ImportSystemIdPhoneStatusRequest(cells, statusDate), warnings);
    }

    private static string? ReadCellText(IXLCell cell)
    {
        var raw = cell.Value.IsText ? cell.Value.GetText() : cell.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
