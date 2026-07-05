using Application.Contracts.OutageShiftPerformances;
using ClosedXML.Excel;

namespace Application.Service.OutageShiftPerformances;

public static class OutageShiftPerformanceExcelParser
{
    private const int PhoneNumberCol = 1;
    private const int SystemIdCol = 2;
    private const int AcceptedOrdersCol = 3;
    private const int RejectedOrdersCol = 4;
    private const int WorkingHoursCol = 5;
    private const int HeaderRow = 1;

    public static (ImportOutageShiftPerformanceRequest Request, List<string> Warnings)
        Parse(Stream excelStream, DateOnly shiftDate)
    {
        using var workbook = new XLWorkbook(excelStream);
        var sheet = workbook.Worksheet(1)
            ?? throw new InvalidOperationException("The Excel file has no worksheets.");

        var rows = new List<ImportOutageShiftPerformanceRow>();
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

            if (!TryGetInt(sheet.Cell(row, AcceptedOrdersCol), out var acceptedOrders) || acceptedOrders < 0)
            {
                warnings.Add($"Row {row}: invalid accepted orders for system ID '{systemId}'. Row skipped.");
                continue;
            }

            if (!TryGetInt(sheet.Cell(row, RejectedOrdersCol), out var rejectedOrders) || rejectedOrders < 0)
            {
                warnings.Add($"Row {row}: invalid rejected orders for system ID '{systemId}'. Row skipped.");
                continue;
            }

            if (!TryGetFloat(sheet.Cell(row, WorkingHoursCol), out var workingHours) || workingHours < 0 || workingHours > 24)
            {
                warnings.Add($"Row {row}: invalid working hours for system ID '{systemId}'. Row skipped.");
                continue;
            }

            rows.Add(new ImportOutageShiftPerformanceRow(
                systemId,
                phoneNumber,
                acceptedOrders,
                rejectedOrders,
                workingHours));
        }

        return (new ImportOutageShiftPerformanceRequest(rows, shiftDate), warnings);
    }

    private static bool TryGetInt(IXLCell cell, out int value)
        => int.TryParse(cell.Value.ToString()?.Trim(), out value);

    private static bool TryGetFloat(IXLCell cell, out float value)
        => float.TryParse(cell.Value.ToString()?.Trim(), out value);
}
