using Application.Contracts.TransporterShifts;
using Application.Service.TransporterShifts;
using ClosedXML.Excel;

namespace Application.Service.Orders;

/// <summary>
/// Parses the weekly/monthly transporter schedule Excel file into a list of
/// <see cref="ImportScheduleCell"/> objects ready for <see cref="ITransporterShiftService.ImportScheduleAsync"/>.
///
/// Expected sheet layout (1-indexed columns):
///   Col A  – Associate Name   (display only; carried through for warnings)
///   Col B  – Transporter ID   (maps to RiderDetails.WorkingId)
///   Col C+ – One column per calendar day, header like "Sun, 03/May"
///
/// Each data cell may contain:
///   • A single shift line  : "Driver • 6 PM • 5h"
///   • Two shift lines      : "Driver • 6 PM • 5h\nDriver • 12 PM • 5h"
///   • Empty / whitespace   : break day
/// </summary>
public static class TransporterScheduleExcelParser
{
    // ── Column indices (1-based, matching the real sheet layout) ─────────
    private const int AssociateNameCol = 1;   // Column A
    private const int TransporterIdCol = 2;   // Column B
    private const int FirstDateCol = 3;   // Column C onward

    // ── Header row index ─────────────────────────────────────────────────
    private const int HeaderRow = 1;

    /// <summary>
    /// Parse the first worksheet of <paramref name="excelStream"/> and return
    /// the raw cell list plus any parse warnings.
    /// </summary>
    /// <param name="excelStream">Readable stream of the .xlsx file.</param>
    /// <param name="overrideYear">
    /// Optional year override forwarded to <see cref="ScheduleHeaderParser"/>.
    /// Pass null to use current Saudi-time year (UTC+3).
    /// </param>
    /// <returns>
    /// A tuple containing:
    ///   <list type="bullet">
    ///     <item><see cref="ImportTransporterScheduleRequest"/> ready to pass directly to the service.</item>
    ///     <item>A list of non-fatal warning strings (unrecognised headers, blank IDs, …).</item>
    ///   </list>
    /// If the sheet structure is invalid a <see cref="InvalidOperationException"/> is thrown.
    /// </returns>
    public static (ImportTransporterScheduleRequest Request, List<string> Warnings)
        Parse(Stream excelStream, int? overrideYear = null)
    {
        using var workbook = new XLWorkbook(excelStream);
        var sheet = workbook.Worksheet(1)
            ?? throw new InvalidOperationException("The Excel file has no worksheets.");

        // ── Collect date-column headers ───────────────────────────────────
        var dateColumns = BuildDateColumnMap(sheet, overrideYear, out var headerWarnings);

        if (dateColumns.Count == 0)
            throw new InvalidOperationException(
                "No valid date column headers found. " +
                "Expected format: \"Sun, 03/May\" starting at column C.");

        // ── Walk data rows ────────────────────────────────────────────────
        var cells = new List<ImportScheduleCell>();
        var warnings = new List<string>(headerWarnings);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? HeaderRow;

        for (int row = HeaderRow + 1; row <= lastRow; row++)
        {
            var associateName = sheet.Cell(row, AssociateNameCol).GetString().Trim();
            var transporterId = sheet.Cell(row, TransporterIdCol).GetString().Trim();

            // Skip fully blank rows
            if (string.IsNullOrWhiteSpace(associateName) && string.IsNullOrWhiteSpace(transporterId))
                continue;

            if (string.IsNullOrWhiteSpace(transporterId))
            {
                warnings.Add($"Row {row}: missing Transporter ID (Associate: '{associateName}'). Row skipped.");
                continue;
            }

            foreach (var (colIndex, columnHeader) in dateColumns)
            {
                var rawCell = ReadCellText(sheet.Cell(row, colIndex));

                cells.Add(new ImportScheduleCell(
                    TransporterId: transporterId,
                    AssociateName: associateName,
                    ColumnHeader: columnHeader,
                    CellContent: rawCell        // null/empty = break day (service handles it)
                ));
            }
        }

        var request = new ImportTransporterScheduleRequest(
            Cells: cells,
            OverrideYear: overrideYear
        );

        return (request, warnings);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Scans the header row from column C onward and returns a dictionary of
    /// (columnIndex → rawHeaderText) for every column whose header looks like
    /// a date ("Sun, 03/May", "Sat, 09/May", …).
    ///
    /// Columns whose headers cannot be parsed as dates emit a warning and are
    /// silently skipped so that extra administrative columns (e.g. "Total Hours",
    /// "Notes") do not break the import.
    /// </summary>
    private static Dictionary<int, string> BuildDateColumnMap(
        IXLWorksheet sheet,
        int? overrideYear,
        out List<string> warnings)
    {
        warnings = [];
        var map = new Dictionary<int, string>();
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? FirstDateCol - 1;

        for (int col = FirstDateCol; col <= lastCol; col++)
        {
            var header = sheet.Cell(HeaderRow, col).GetString().Trim();

            if (string.IsNullOrWhiteSpace(header))
                continue;

            // Try to parse; if it fails treat the column as non-date (skip it)
            var parsed = ScheduleHeaderParser.Parse(header, overrideYear);
            if (parsed is null)
            {
                warnings.Add(
                    $"Column {col} header \"{header}\" could not be parsed as a date – column skipped.");
                continue;
            }

            map[col] = header;
        }

        return map;
    }

    /// <summary>
    /// Extracts the raw text from a cell, preserving embedded newlines so that
    /// multi-shift cells ("Driver • 6 PM • 5h\nDriver • 1 AM • 3h") survive intact.
    /// </summary>
    private static string? ReadCellText(IXLCell cell)
    {
        // RichText cells may contain line breaks that GetString() strips – use Value instead
        var raw = cell.Value.IsText
            ? cell.Value.GetText()
            : cell.GetString();

        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}