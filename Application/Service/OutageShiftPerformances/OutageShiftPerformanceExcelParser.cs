using Application.Contracts.OutageShiftPerformances;
using ClosedXML.Excel;

namespace Application.Service.OutageShiftPerformances;

public static class OutageShiftPerformanceExcelParser
{
    public static (ImportOutageShiftPerformanceRequest Request, List<string> Warnings)
        Parse(Stream excelStream, DateOnly shiftDate)
    {
        using var workbook = new XLWorkbook(excelStream);
        var sheet = workbook.Worksheet(1)
            ?? throw new InvalidOperationException("The Excel file has no worksheets.");

        var mapping = FindColumnIndices(sheet);
        if (!mapping.IsValid)
            throw new InvalidOperationException(mapping.ErrorMessage);

        var rows = new List<ImportOutageShiftPerformanceRow>();
        var warnings = new List<string>();
        var rowNumber = 1;

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            rowNumber++;

            var riderId = row.Cell(mapping.RiderIdColumn).Value.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(riderId))
            {
                warnings.Add($"Row {rowNumber}: missing rider ID. Row skipped.");
                continue;
            }

            if (!TryGetInt(row.Cell(mapping.AcceptedOrdersColumn), out var acceptedOrders) || acceptedOrders < 0)
            {
                warnings.Add($"Row {rowNumber}: invalid accepted orders for rider ID '{riderId}'. Row skipped.");
                continue;
            }

            if (!TryGetInt(row.Cell(mapping.RejectedOrdersColumn), out var rejectedOrders) || rejectedOrders < 0)
            {
                warnings.Add($"Row {rowNumber}: invalid rejected orders for rider ID '{riderId}'. Row skipped.");
                continue;
            }

            if (!TryGetInt(row.Cell(mapping.StackedDeliveriesColumn), out var stackedDeliveries) || stackedDeliveries < 0)
            {
                warnings.Add($"Row {rowNumber}: invalid stacked deliveries for rider ID '{riderId}'. Row skipped.");
                continue;
            }

            if (!TryGetFloat(row.Cell(mapping.WorkingHoursColumn), out var workingHours) || workingHours < 0 || workingHours > 24)
            {
                warnings.Add($"Row {rowNumber}: invalid working hours for rider ID '{riderId}'. Row skipped.");
                continue;
            }

            if (acceptedOrders == 0 && rejectedOrders == 0 && stackedDeliveries == 0 && workingHours <= 1)
            {
                warnings.Add($"Row {rowNumber}: accepted orders, rejected orders, and stacked deliveries are all zero. Row skipped.");
                continue;
            }

            rows.Add(new ImportOutageShiftPerformanceRow(
                riderId,
                acceptedOrders,
                rejectedOrders,
                workingHours));
        }

        return (new ImportOutageShiftPerformanceRequest(rows, shiftDate), warnings);
    }

    private static OutageShiftColumnMapping FindColumnIndices(IXLWorksheet worksheet)
    {
        var headerRow = worksheet.FirstRowUsed();
        if (headerRow is null)
        {
            return new OutageShiftColumnMapping
            {
                IsValid = false,
                ErrorMessage = "Excel file is empty or has no header row."
            };
        }

        var headerCells = headerRow.CellsUsed().ToList();
        var mapping = new OutageShiftColumnMapping
        {
            RiderIdColumn = FindColumn(headerCells, ShiftImportColumns.RiderIdColumns),
            AcceptedOrdersColumn = FindColumn(headerCells, ShiftImportColumns.AcceptedOrdersColumns),
            RejectedOrdersColumn = FindColumn(headerCells, ShiftImportColumns.RejectedOrdersColumns),
            StackedDeliveriesColumn = FindColumn(headerCells, ShiftImportColumns.StackedDeliveriesColumns),
            WorkingHoursColumn = FindColumn(headerCells, ShiftImportColumns.WorkingHoursColumns)
        };

        var missingColumns = new List<string>();

        if (mapping.RiderIdColumn == 0)
            missingColumns.Add($"RiderId (tried: {string.Join(", ", ShiftImportColumns.RiderIdColumns)})");
        if (mapping.AcceptedOrdersColumn == 0)
            missingColumns.Add($"AcceptedOrders (tried: {string.Join(", ", ShiftImportColumns.AcceptedOrdersColumns)})");
        if (mapping.RejectedOrdersColumn == 0)
            missingColumns.Add($"RejectedOrders (tried: {string.Join(", ", ShiftImportColumns.RejectedOrdersColumns)})");
        if (mapping.StackedDeliveriesColumn == 0)
            missingColumns.Add($"StackedDeliveries (tried: {string.Join(", ", ShiftImportColumns.StackedDeliveriesColumns)})");
        if (mapping.WorkingHoursColumn == 0)
            missingColumns.Add($"WorkingHours (tried: {string.Join(", ", ShiftImportColumns.WorkingHoursColumns)})");

        if (missingColumns.Count > 0)
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Missing required columns: {string.Join(", ", missingColumns)}";
            return mapping;
        }

        mapping.IsValid = true;
        return mapping;
    }

    private static int FindColumn(List<IXLCell> headerCells, string[] possibleNames)
    {
        foreach (var cell in headerCells)
        {
            var headerValue = cell.Value.ToString().Trim();
            foreach (var possibleName in possibleNames)
            {
                if (headerValue.Equals(possibleName, StringComparison.OrdinalIgnoreCase))
                    return cell.Address.ColumnNumber;
            }
        }

        return 0;
    }

    private static bool TryGetInt(IXLCell cell, out int value)
        => int.TryParse(cell.Value.ToString()?.Trim(), out value);

    private static bool TryGetFloat(IXLCell cell, out float value)
        => float.TryParse(cell.Value.ToString()?.Trim(), out value);

    private class OutageShiftColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int RiderIdColumn { get; set; }
        public int AcceptedOrdersColumn { get; set; }
        public int RejectedOrdersColumn { get; set; }
        public int StackedDeliveriesColumn { get; set; }
        public int WorkingHoursColumn { get; set; }
    }

    private static class ShiftImportColumns
    {
        public static readonly string[] RiderIdColumns =
            { "Rider Id", "Working_ID", "معرّف السائق", "ID", "RiderID", "Rider_ID", "EmployeeID" };

        public static readonly string[] AcceptedOrdersColumns =
            { "Completed Deliveries", "Accepted_Orders", "Accepted Orders", "المهام التي تم تسليمها", "AcceptedDaily", "Accepted_Daily" };

        public static readonly string[] RejectedOrdersColumns =
            { "Declined Deliveries", "Rejected_Orders", "المهام المرفوضة", "Rejected", "RejectedDaily", "Rejected_Daily" };

        public static readonly string[] StackedDeliveriesColumns =
            { "Stacked Deliveries", "Stacked_Deliveries", "StackedDeliveries" };

        public static readonly string[] WorkingHoursColumns =
            { "Actual Working Hours", "Working_Hours", "Working Hours", "وقت اتصال السائقين عبر تطبيق السائق.", "وقت اتصال السائقين عبر تطبيق السائق", "Total_Hours" };
    }
}
