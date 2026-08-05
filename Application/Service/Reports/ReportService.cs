using Application.Abstraction;
using Application.Contracts.ReportCo;
using Application.Service.Member;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Keeta;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static Application.Service.Reports.IReportService;

namespace Application.Service.Reports;

public class ReportService(ApplicationDbcontext dbcontext) : IReportService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    // Add these implementations to ReportService.cs class

    /// <summary>
    /// Compare orders between two time periods (e.g., previous month vs current month)
    /// Period 1 is automatically calculated as the previous month of Period 2
    /// </summary>
    /// 
    private const float TARGET_HOURS_PER_DAY = 9f;
    private const float TARGET_HOURS_PER_DAY2 = 10.5f;
    private const int TARGET_ORDERS_PER_DAY = 14;
    private const int TARGET_ORDERS_PER_DAY2 = 13;


    // Add this method to the ReportService class
    // Replace the following methods in ReportService.cs

    private const float MIN_WORKING_HOURS_PER_DAY = 10f;
    private const int MAX_ALLOWED_MISSING_DAYS = 4;
    private const int FULL_MONTH_TARGET_ORDERS = 300;
    private const int FIRST_CRITICAL_DAYS = 3;
    private const int LAST_CRITICAL_DAYS = 4;


    public async Task<Result<RiderRecentMonthsResult>> GetRecentMonthsFromExcelAsync(
        Stream excelInputStream,
        CancellationToken cancellationToken = default)
    {
        List<long> iqamaNumbers;
        try
        {
            iqamaNumbers = ReadIqamaNumbersFromStream1(excelInputStream);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderRecentMonthsResult>(
                new Error($"Error reading Excel file: {ex.Message}", "invalid_input", 400));
        }

        if (!iqamaNumbers.Any())
            return Result.Failure<RiderRecentMonthsResult>(
                new Error(
                    "No valid Iqama numbers found in the Excel file. " +
                    "Ensure column A contains numeric Iqama numbers.",
                    "invalid_input", 400));

        return await GetRecentMonthsAsync(iqamaNumbers, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC: from list of iqama numbers
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Result<RiderRecentMonthsResult>> GetRecentMonthsAsync(
        List<long> iqamaNumbers,
        CancellationToken cancellationToken = default)
    {
        if (iqamaNumbers == null || !iqamaNumbers.Any())
            return Result.Failure<RiderRecentMonthsResult>(
                new Error("IqamaNumbers list cannot be empty", "invalid_input", 400));

        try
        {
            // ── Build the 4 month slots (3 months ago → current) ─────────
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var currentMonth = new DateOnly(today.Year, today.Month, 1);
            var monthSlots = BuildMonthSlots(currentMonth);

            var rangeStart = monthSlots.First().Start;   // 3 months ago, day 1
            var rangeEnd = monthSlots.Last().End;       // current month, last day

            // ── Single query: all matching riders ─────────────────────────
            var riders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Where(r => iqamaNumbers.Contains(r.EmployeeIqamaNo))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // ── Single query: all shifts in the 4-month window ────────────
            var riderIds = riders.Select(r => r.Id).ToList();

            var allShifts = await dbcontext.RiderShifts
                .Where(s => riderIds.Contains(s.RiderId)
                         &&( s.CompanyId == 1 || s.CompanyId == 2)
                         && s.ShiftDate >= rangeStart
                         && s.ShiftDate <= rangeEnd)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Index for fast lookup
            var shiftsByRider = allShifts
                .GroupBy(s => s.RiderId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var riderByIqama = riders
                .ToDictionary(r => r.EmployeeIqamaNo);

            // ── Build result per requested iqama ──────────────────────────
            var entries = new List<RiderRecentMonthsEntry>();
            var notFound = new List<long>();

            foreach (var iqamaNo in iqamaNumbers)
            {
                if (!riderByIqama.TryGetValue(iqamaNo, out var rider))
                {
                    notFound.Add(iqamaNo);
                    entries.Add(BuildNotFoundEntry(iqamaNo, monthSlots));
                    continue;
                }

                shiftsByRider.TryGetValue(rider.Id, out var riderShifts);
                entries.Add(BuildRiderEntry(rider, riderShifts ?? [], monthSlots));
            }

            // ── Assemble final result ─────────────────────────────────────
            var monthLabels = monthSlots
                .Select(s => new MonthLabel(s.Year, s.Month, s.MonthName, s.Label))
                .ToList();

            return Result.Success(new RiderRecentMonthsResult(
                TotalRequested: iqamaNumbers.Count,
                TotalFound: entries.Count(e => e.Found),
                NotFound: notFound,
                CurrentMonth: currentMonth,
                MonthsQueried: monthLabels,
                Riders: entries
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderRecentMonthsResult>(
                new Error($"Error generating recent months data: {ex.Message}", "server_error", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE: build month slot descriptors
    // ─────────────────────────────────────────────────────────────────────────
    private record MonthSlot(
        int Year, int Month, string MonthName, string Label,
        DateOnly Start, DateOnly End);

    private static List<MonthSlot> BuildMonthSlots(DateOnly currentMonth)
    {
        var slots = new List<MonthSlot>();
        string[] labels = { "3 Months Ago", "2 Months Ago", "1 Month Ago", "Current Month" };

        for (int offset = -3; offset <= 0; offset++)
        {
            var start = currentMonth.AddMonths(offset);
            var end = start.AddMonths(1).AddDays(-1);
            var label = labels[offset + 3];
            var dt = new DateTime(start.Year, start.Month, 1);

            slots.Add(new MonthSlot(
                Year: start.Year,
                Month: start.Month,
                MonthName: dt.ToString("MMMM"),
                Label: label,
                Start: start,
                End: end
            ));
        }
        return slots;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE: build a single rider entry
    // ─────────────────────────────────────────────────────────────────────────
    private static RiderRecentMonthsEntry BuildRiderEntry(
        RiderDetails rider,
        List<RiderShift> shifts,
        List<MonthSlot> monthSlots)
    {
        // Pre-index shifts by (Year, Month) for fast lookup
        var shiftsByMonth = shifts
            .GroupBy(s => (s.ShiftDate.Year, s.ShiftDate.Month))
            .ToDictionary(g => g.Key, g => g.ToList());

        var monthlyOrders = new List<RiderMonthOrders>();

        foreach (var slot in monthSlots)
        {
            var key = (slot.Year, slot.Month);
            if (shiftsByMonth.TryGetValue(key, out var ms) && ms.Any())
            {
                var total = ms.Count;
                var completed = ms.Count(s => s.ShiftStatus == "Completed");
                var accepted = ms.Sum(s => s.AcceptedDailyOrders);

                monthlyOrders.Add(new RiderMonthOrders(
                    Year: slot.Year,
                    Month: slot.Month,
                    MonthName: slot.MonthName,
                    Label: slot.Label,
                    HasData: true,
                    AcceptedOrders: accepted,
                    TotalShifts: total,
                    RejectedOrders: ms.Sum(s => s.RejectedDailyOrders),
                    RealRejectedOrders: ms.Sum(s => s.RealRejectedDailyOrders),
                    WorkingHours: ms.Sum(s => s.WorkingHours),
                    CompletedShifts: completed,
                    IncompleteShifts: ms.Count(s => s.ShiftStatus == "Incomplete"),
                    FailedShifts: ms.Count(s => s.ShiftStatus == "Failed"),
                    CompletionRate: total > 0 ? Math.Round((decimal)completed / total * 100, 2) : 0,
                    AverageOrdersPerShift: total > 0 ? Math.Round((decimal)accepted / total, 2) : 0
                ));
            }
            else
            {
                monthlyOrders.Add(new RiderMonthOrders(
                    Year: slot.Year,
                    Month: slot.Month,
                    MonthName: slot.MonthName,
                    Label: slot.Label,
                    HasData: false,
                    AcceptedOrders: 0,
                    TotalShifts: 0,
                    RejectedOrders: 0,
                    RealRejectedOrders: 0,
                    WorkingHours: 0,
                    CompletedShifts: 0,
                    IncompleteShifts: 0,
                    FailedShifts: 0,
                    CompletionRate: 0,
                    AverageOrdersPerShift: 0
                ));
            }
        }

        // ── Totals & trend ────────────────────────────────────────────────
        var totalOrders = monthlyOrders.Sum(m => m.AcceptedOrders);
        var totalShifts = monthlyOrders.Sum(m => m.TotalShifts);
        var activeMonths = monthlyOrders.Where(m => m.HasData).ToList();

        var avgPerActiveMonth = activeMonths.Count > 0
            ? Math.Round((decimal)activeMonths.Sum(m => m.AcceptedOrders) / activeMonths.Count, 2)
            : 0;

        // Trend: current month orders vs average of the previous 3 months
        var currentMonthOrders = monthlyOrders.Last().AcceptedOrders;
        var prev3 = monthlyOrders.Take(3).ToList();
        var prev3WithData = prev3.Where(m => m.HasData).ToList();
        decimal trendVsPrev3Avg = 0;

        if (prev3WithData.Any())
        {
            var prev3Avg = (decimal)prev3WithData.Sum(m => m.AcceptedOrders) / prev3WithData.Count;
            trendVsPrev3Avg = prev3Avg > 0
                ? Math.Round((currentMonthOrders - prev3Avg) / prev3Avg * 100, 2)
                : currentMonthOrders > 0 ? 100 : 0;
        }

        return new RiderRecentMonthsEntry(
            IqamaNo: rider.EmployeeIqamaNo,
            RiderNameAR: rider.Employee.NameAR,
            RiderNameEN: rider.Employee.NameEN,
            WorkingId: rider.WorkingId ?? "0",
            Found: true,
            MonthlyOrders: monthlyOrders,
            TotalOrders: totalOrders,
            TotalShifts: totalShifts,
            AverageOrdersPerActiveMonth: avgPerActiveMonth,
            TrendVsPrevious3Avg: trendVsPrev3Avg
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE: not-found placeholder entry
    // ─────────────────────────────────────────────────────────────────────────
    private static RiderRecentMonthsEntry BuildNotFoundEntry(
        long iqamaNo, List<MonthSlot> monthSlots)
    {
        var emptyMonths = monthSlots.Select(slot => new RiderMonthOrders(
            Year: slot.Year, Month: slot.Month,
            MonthName: slot.MonthName, Label: slot.Label,
            HasData: false, AcceptedOrders: 0, TotalShifts: 0,
            RejectedOrders: 0, RealRejectedOrders: 0, WorkingHours: 0,
            CompletedShifts: 0, IncompleteShifts: 0, FailedShifts: 0,
            CompletionRate: 0, AverageOrdersPerShift: 0
        )).ToList();

        return new RiderRecentMonthsEntry(
            IqamaNo: iqamaNo,
            RiderNameAR: string.Empty,
            RiderNameEN: string.Empty,
            WorkingId: string.Empty,
            Found: false,
            MonthlyOrders: emptyMonths,
            TotalOrders: 0,
            TotalShifts: 0,
            AverageOrdersPerActiveMonth: 0,
            TrendVsPrevious3Avg: 0
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE: read iqama numbers from Excel stream
    // ─────────────────────────────────────────────────────────────────────────
    private static List<long> ReadIqamaNumbersFromStream1(Stream stream)
    {
        var numbers = new List<long>();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (int row = 1; row <= lastRow; row++)
        {
            var cell = ws.Cell(row, 1);
            if (cell.IsEmpty()) continue;
            var raw = cell.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // Skip non-numeric header if present
            if (row == 1 && !long.TryParse(raw, out _)) continue;
            if (long.TryParse(raw, out var iqama))
                numbers.Add(iqama);
        }
        return numbers;
    }

 

    private const int COL_IQAMA = 1;
    private const int COL_NAME = 2;
    private const int COL_WORKING_ID = 3;
    private const int COL_YEAR = 4;
    private const int COL_MONTH_NUM = 5;
    private const int COL_MONTH_NAME = 6;
    private const int COL_SHIFTS = 7;
    private const int COL_ACCEPTED = 8;
    private const int COL_REJECTED = 9;
    private const int COL_REAL_REJ = 10;
    private const int COL_HOURS = 11;
    private const int COL_COMPLETED = 12;
    private const int COL_INCOMPLETE = 13;
    private const int COL_FAILED = 14;
    private const int COL_COMP_RATE = 15;
    private const int TOTAL_COLS = 15;

    // ── Color palette ─────────────────────────────────────────────────────────
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#1F3864"); // dark navy
    private static readonly XLColor RiderHeaderBg = XLColor.FromHtml("#2E75B6"); // blue
    private static readonly XLColor TotalRowBg = XLColor.FromHtml("#D6E4F0"); // light blue
    private static readonly XLColor ActiveRowBg = XLColor.FromHtml("#FFFFFF"); // white
    private static readonly XLColor EmptyRowBg = XLColor.FromHtml("#F5F5F5"); // light grey
    private static readonly XLColor NotFoundBg = XLColor.FromHtml("#FCE4D6"); // light red
    private static readonly XLColor GoodRate = XLColor.FromHtml("#E2EFDA"); // light green
    private static readonly XLColor BadRate = XLColor.FromHtml("#FCE4D6"); // light red

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC: get raw data
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Result<BulkRiderHistoryResult>> GetBulkRiderMonthlyHistoryAsync(
        List<long> iqamaNumbers,
        CancellationToken cancellationToken = default)
    {
        if (iqamaNumbers == null || !iqamaNumbers.Any())
            return Result.Failure<BulkRiderHistoryResult>(
                new Error("IqamaNumbers list cannot be empty", "invalid_input", 400));

        try
        {
            // Single query: all matching riders
            var riders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Where(r => iqamaNumbers.Contains(r.EmployeeIqamaNo))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Single query: all shifts for those riders (Company 1 only)
            var riderIds = riders.Select(r => r.Id).ToList();
            var allShifts = await dbcontext.RiderShifts
                .Where(s => riderIds.Contains(s.RiderId) && (s.CompanyId == 1 || s.CompanyId == 2))
                .OrderBy(s => s.ShiftDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var shiftsByRider = allShifts.GroupBy(s => s.RiderId)
                                          .ToDictionary(g => g.Key, g => g.ToList());
            var riderByIqama = riders.ToDictionary(r => r.EmployeeIqamaNo);
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

            var results = new List<RiderHistoryEntry>();
            var notFound = new List<long>();

            foreach (var iqamaNo in iqamaNumbers)
            {
                if (!riderByIqama.TryGetValue(iqamaNo, out var rider))
                {
                    notFound.Add(iqamaNo);
                    results.Add(new RiderHistoryEntry(iqamaNo, string.Empty, string.Empty, null, false));
                    continue;
                }

                if (!shiftsByRider.TryGetValue(rider.Id, out var shifts) || !shifts.Any())
                {
                    results.Add(new RiderHistoryEntry(
                        iqamaNo, rider.Employee.NameAR, rider.WorkingId ?? "0", null, true));
                    continue;
                }

                var firstDate = shifts.First().ShiftDate;
                var lastDate = shifts.Last().ShiftDate;
                var endDate = lastDate > today ? lastDate : today;
                var monthly = GenerateMonthlyShiftSummaries1(shifts, firstDate, endDate);
                var active = monthly.Where(m => m.TotalAcceptedOrders > 0).ToList();

                var history = new RiderMonthlyHistorys(
                    IqamaNo: iqamaNo,
                    RiderName: rider.Employee.NameAR,
                    WorkingId: rider.WorkingId ?? "0",
                    FirstShiftDate: firstDate,
                    LastShiftDate: lastDate,
                    TotalMonths: monthly.Count,
                    ActiveMonthsCount: active.Count,
                    AverageOrdersPerActiveMonth:
                        active.Count > 0
                            ? (decimal)active.Sum(m => m.TotalAcceptedOrders) / active.Count
                            : 0,
                    ActiveMonthNumbers: active.Select(m => m.Month).ToList(),
                    MonthlyData: monthly
                );

                results.Add(new RiderHistoryEntry(
                    iqamaNo, rider.Employee.NameAR, rider.WorkingId ?? "0", history, true));
            }

            return Result.Success(new BulkRiderHistoryResult(
                Results: results,
                NotFound: notFound,
                TotalRequested: iqamaNumbers.Count,
                TotalFound: results.Count(r => r.Found)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkRiderHistoryResult>(
                new Error($"Error generating bulk rider history: {ex.Message}", "server_error", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC: export from list of iqama numbers
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Result<byte[]>> ExportBulkRiderHistoryToExcelAsync(
        List<long> iqamaNumbers,
        CancellationToken cancellationToken = default)
    {
        var dataResult = await GetBulkRiderMonthlyHistoryAsync(iqamaNumbers, cancellationToken);
        if (!dataResult.IsSuccess)
            return Result.Failure<byte[]>(dataResult.Error);

        return Result.Success(BuildExcelFile(dataResult.Value));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC: read iqama numbers from uploaded Excel, then export result
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Result<byte[]>> ExportBulkRiderHistoryFromExcelAsync(
        Stream excelInputStream,
        CancellationToken cancellationToken = default)
    {
        List<long> iqamaNumbers;
        try
        {
            iqamaNumbers = ReadIqamaNumbersFromStream(excelInputStream);
        }
        catch (Exception ex)
        {
            return Result.Failure<byte[]>(
                new Error($"Error reading Excel file: {ex.Message}", "invalid_input", 400));
        }

        if (!iqamaNumbers.Any())
            return Result.Failure<byte[]>(
                new Error(
                    "No valid Iqama numbers found in the Excel file. " +
                    "Ensure column A contains numeric Iqama numbers.",
                    "invalid_input", 400));

        return await ExportBulkRiderHistoryToExcelAsync(iqamaNumbers, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE: read iqama numbers from an Excel stream
    // ─────────────────────────────────────────────────────────────────────────
    private static List<long> ReadIqamaNumbersFromStream(Stream stream)
    {
        var numbers = new List<long>();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (int row = 1; row <= lastRow; row++)
        {
            var cell = ws.Cell(row, 1);
            if (cell.IsEmpty()) continue;
            var raw = cell.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // Skip header row if it's not a number
            if (row == 1 && !long.TryParse(raw, out _)) continue;
            if (long.TryParse(raw, out var iqama))
                numbers.Add(iqama);
        }
        return numbers;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE: build the Excel workbook bytes
    // ─────────────────────────────────────────────────────────────────────────
    private static byte[] BuildExcelFile(BulkRiderHistoryResult data)
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: Monthly Detail ───────────────────────────────────────
        var wsDetail = wb.Worksheets.Add("Monthly Detail");
        WriteDetailSheet(wsDetail, data);

        // ── Sheet 2: Summary (one row per rider) ──────────────────────────
        var wsSummary = wb.Worksheets.Add("Summary");
        WriteSummarySheet(wsSummary, data);

        // ── Sheet 3: Not Found ────────────────────────────────────────────
        if (data.NotFound.Any())
        {
            var wsNF = wb.Worksheets.Add("Not Found");
            WriteNotFoundSheet(wsNF, data.NotFound);
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SHEET 1 — Monthly Detail
    // Each rider gets a blue header row, then one row per month,
    // then a totals row.  Riders are separated by a blank row.
    // ─────────────────────────────────────────────────────────────────────────
    private static void WriteDetailSheet(IXLWorksheet ws, BulkRiderHistoryResult data)
    {
        // ── Global column header (row 1) ──────────────────────────────────
        int headerRow = 1;
        WriteGlobalHeader(ws, headerRow);

        int currentRow = 2;

        foreach (var entry in data.Results)
        {
            // ── Rider header row ──────────────────────────────────────────
            WriteRiderHeaderRow(ws, currentRow, entry);
            int riderHeaderRow = currentRow;
            currentRow++;

            if (!entry.Found)
            {
                // Not-found notice
                var notFoundRange = ws.Range(currentRow, COL_IQAMA, currentRow, TOTAL_COLS);
                notFoundRange.Merge();
                notFoundRange.Value = "⚠ Rider not found in the system";
                notFoundRange.Style.Fill.BackgroundColor = NotFoundBg;
                notFoundRange.Style.Font.Italic = true;
                notFoundRange.Style.Font.FontColor = XLColor.DarkRed;
                notFoundRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                currentRow++;
                AddBlankRow(ws, currentRow++);
                continue;
            }

            if (entry.History == null || !entry.History.MonthlyData.Any())
            {
                var noDataRange = ws.Range(currentRow, COL_IQAMA, currentRow, TOTAL_COLS);
                noDataRange.Merge();
                noDataRange.Value = "No shift history recorded";
                noDataRange.Style.Fill.BackgroundColor = EmptyRowBg;
                noDataRange.Style.Font.Italic = true;
                noDataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                currentRow++;
                AddBlankRow(ws, currentRow++);
                continue;
            }

            // ── Data rows (one per month) ─────────────────────────────────
            int dataStartRow = currentRow;
            foreach (var month in entry.History.MonthlyData)
            {
                bool hasData = month.TotalAcceptedOrders > 0 || month.TotalShifts > 0;
                WriteMonthRow(ws, currentRow, entry, month, hasData);
                currentRow++;
            }

            // ── Totals row ────────────────────────────────────────────────
            WriteTotalsRow(ws, currentRow, dataStartRow, currentRow - 1);
            currentRow++;

            // ── Blank separator ───────────────────────────────────────────
            AddBlankRow(ws, currentRow++);
        }

        // ── Format columns ────────────────────────────────────────────────
        SetDetailColumnWidths(ws);
        ws.SheetView.FreezeRows(1);
        ws.TabColor = XLColor.FromHtml("#2E75B6");
    }

    private static void WriteGlobalHeader(IXLWorksheet ws, int row)
    {
        string[] headers =
        {
            "Iqama No", "Rider Name (AR)", "Working ID",
            "Year", "Month #", "Month",
            "Shifts", "Accepted Orders", "Rejected Orders", "Real Rejected",
            "Working Hours", "Completed", "Incomplete", "Failed",
            "Completion Rate"
        };

        for (int c = 1; c <= headers.Length; c++)
        {
            var cell = ws.Cell(row, c);
            cell.Value = headers[c - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.White;
        }
        ws.Row(row).Height = 22;
    }

    private static void WriteRiderHeaderRow(IXLWorksheet ws, int row, RiderHistoryEntry entry)
    {
        // Merge cols 1-3 for rider identity info
        var identityRange = ws.Range(row, COL_IQAMA, row, COL_WORKING_ID);
        identityRange.Merge();
        identityRange.Value = entry.Found
            ? $"  {entry.IqamaNo}  |  {entry.RiderName}  |  WID: {entry.WorkingId}"
            : $"  {entry.IqamaNo}  |  NOT FOUND";

        // Fill rest of rider header
        var fullRange = ws.Range(row, COL_IQAMA, row, TOTAL_COLS);
        fullRange.Style.Fill.BackgroundColor = entry.Found ? RiderHeaderBg : NotFoundBg;
        fullRange.Style.Font.Bold = true;
        fullRange.Style.Font.FontColor = XLColor.White;
        fullRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        if (entry.Found && entry.History != null)
        {
            ws.Cell(row, COL_YEAR).Value = $"Active months: {entry.History.ActiveMonthsCount}";
            ws.Cell(row, COL_MONTH_NUM).Value = $"Total months: {entry.History.TotalMonths}";
            ws.Cell(row, COL_MONTH_NAME).Value = $"Avg orders/active month: {entry.History.AverageOrdersPerActiveMonth:F1}";
            ws.Cell(row, COL_SHIFTS).Value = $"First shift: {entry.History.FirstShiftDate:yyyy-MM-dd}";
            ws.Cell(row, COL_ACCEPTED).Value = $"Last shift: {entry.History.LastShiftDate:yyyy-MM-dd}";
        }

        ws.Row(row).Height = 20;
        // Bottom border
        fullRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        fullRange.Style.Border.BottomBorderColor = XLColor.White;
    }

    private static void WriteMonthRow(
        IXLWorksheet ws, int row, RiderHistoryEntry entry,
        MonthlyShiftSummary month, bool hasData)
    {
        var bg = hasData ? ActiveRowBg : EmptyRowBg;

        ws.Cell(row, COL_IQAMA).Value = entry.IqamaNo;
        ws.Cell(row, COL_NAME).Value = entry.RiderName;
        ws.Cell(row, COL_WORKING_ID).Value = entry.WorkingId;
        ws.Cell(row, COL_YEAR).Value = month.Year;
        ws.Cell(row, COL_MONTH_NUM).Value = month.Month;
        ws.Cell(row, COL_MONTH_NAME).Value = month.MonthName;
        ws.Cell(row, COL_SHIFTS).Value = month.TotalShifts;
        ws.Cell(row, COL_ACCEPTED).Value = month.TotalAcceptedOrders;
        ws.Cell(row, COL_REJECTED).Value = month.TotalRejectedOrders;
        ws.Cell(row, COL_REAL_REJ).Value = month.TotalRealRejectedOrders;
        ws.Cell(row, COL_HOURS).Value = Math.Round(month.TotalWorkingHours, 1);
        ws.Cell(row, COL_COMPLETED).Value = month.CompletedShifts;
        ws.Cell(row, COL_INCOMPLETE).Value = month.IncompleteShifts;
        ws.Cell(row, COL_FAILED).Value = month.FailedShifts;

        // Completion rate as actual percentage value
        var compRateCell = ws.Cell(row, COL_COMP_RATE);
        compRateCell.Value = month.CompletionRate / 100m; // store as 0.xx fraction
        compRateCell.Style.NumberFormat.Format = "0.0%";

        // Color code completion rate
        if (hasData)
        {
            compRateCell.Style.Fill.BackgroundColor =
                month.CompletionRate >= 80 ? GoodRate : BadRate;
        }

        // Row background
        var rowRange = ws.Range(row, COL_IQAMA, row, TOTAL_COLS);
        rowRange.Style.Fill.BackgroundColor = bg;

        if (!hasData)
        {
            rowRange.Style.Font.FontColor = XLColor.Gray;
            rowRange.Style.Font.Italic = true;
        }

        // Subtle left border to indicate same rider group
        ws.Cell(row, COL_IQAMA).Style.Border.LeftBorder = XLBorderStyleValues.Medium;
        ws.Cell(row, COL_IQAMA).Style.Border.LeftBorderColor = RiderHeaderBg;
        ws.Cell(row, TOTAL_COLS).Style.Border.RightBorder = XLBorderStyleValues.Medium;
        ws.Cell(row, TOTAL_COLS).Style.Border.RightBorderColor = RiderHeaderBg;

        // Light bottom border between months
        rowRange.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        rowRange.Style.Border.BottomBorderColor = XLColor.LightGray;

        // Center numeric columns
        for (int c = COL_YEAR; c <= TOTAL_COLS; c++)
            ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Row(row).Height = 17;
    }

    private static void WriteTotalsRow(IXLWorksheet ws, int row, int dataStart, int dataEnd)
    {
        var range = ws.Range(row, COL_IQAMA, row, TOTAL_COLS);
        range.Style.Fill.BackgroundColor = TotalRowBg;
        range.Style.Font.Bold = true;
        range.Style.Border.TopBorder = XLBorderStyleValues.Medium;
        range.Style.Border.TopBorderColor = RiderHeaderBg;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        range.Style.Border.BottomBorderColor = RiderHeaderBg;

        // Merge first 3 cols for label
        ws.Range(row, COL_IQAMA, row, COL_WORKING_ID).Merge();
        ws.Cell(row, COL_IQAMA).Value = "TOTAL";
        ws.Cell(row, COL_IQAMA).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        // SUM formulas for numeric cols
        var sumCols = new[]
        {
            COL_SHIFTS, COL_ACCEPTED, COL_REJECTED, COL_REAL_REJ,
            COL_HOURS, COL_COMPLETED, COL_INCOMPLETE, COL_FAILED
        };

        foreach (var col in sumCols)
        {
            var colLetter = ColumnLetter(col);
            ws.Cell(row, col).FormulaA1 = $"=SUM({colLetter}{dataStart}:{colLetter}{dataEnd})";
            ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Average completion rate
        var compLetter = ColumnLetter(COL_COMP_RATE);
        var compCell = ws.Cell(row, COL_COMP_RATE);
        compCell.FormulaA1 =
            $"=IFERROR(AVERAGEIF({ColumnLetter(COL_SHIFTS)}{dataStart}:{ColumnLetter(COL_SHIFTS)}{dataEnd},\">0\",{compLetter}{dataStart}:{compLetter}{dataEnd}),0)";
        compCell.Style.NumberFormat.Format = "0.0%";
        compCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Row(row).Height = 18;
    }

    private static void AddBlankRow(IXLWorksheet ws, int row)
    {
        ws.Row(row).Height = 8;
        var r = ws.Range(row, COL_IQAMA, row, TOTAL_COLS);
        r.Style.Fill.BackgroundColor = XLColor.White;
        r.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        r.Style.Border.BottomBorderColor = XLColor.LightGray;
    }

    private static void SetDetailColumnWidths(IXLWorksheet ws)
    {
        ws.Column(COL_IQAMA).Width = 16;
        ws.Column(COL_NAME).Width = 28;
        ws.Column(COL_WORKING_ID).Width = 13;
        ws.Column(COL_YEAR).Width = 10;
        ws.Column(COL_MONTH_NUM).Width = 10;
        ws.Column(COL_MONTH_NAME).Width = 14;
        ws.Column(COL_SHIFTS).Width = 10;
        ws.Column(COL_ACCEPTED).Width = 17;
        ws.Column(COL_REJECTED).Width = 16;
        ws.Column(COL_REAL_REJ).Width = 15;
        ws.Column(COL_HOURS).Width = 14;
        ws.Column(COL_COMPLETED).Width = 13;
        ws.Column(COL_INCOMPLETE).Width = 13;
        ws.Column(COL_FAILED).Width = 10;
        ws.Column(COL_COMP_RATE).Width = 16;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SHEET 2 — Summary (one row per rider)
    // ─────────────────────────────────────────────────────────────────────────
    private static void WriteSummarySheet(IXLWorksheet ws, BulkRiderHistoryResult data)
    {
        string[] headers =
        {
            "Iqama No", "Rider Name (AR)", "Working ID",
            "Status", "First Shift", "Last Shift",
            "Total Months", "Active Months",
            "Total Shifts", "Total Accepted", "Total Rejected",
            "Total Hours", "Avg Orders / Active Month"
        };

        // Header row
        for (int c = 1; c <= headers.Length; c++)
        {
            var cell = ws.Cell(1, c);
            cell.Value = headers[c - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.White;
        }
        ws.Row(1).Height = 22;

        int row = 2;
        foreach (var entry in data.Results)
        {
            var bg = !entry.Found ? NotFoundBg :
                     entry.History == null ? EmptyRowBg :
                     XLColor.White;

            ws.Cell(row, 1).Value = entry.IqamaNo;
            ws.Cell(row, 2).Value = entry.RiderName;
            ws.Cell(row, 3).Value = entry.WorkingId;
            ws.Cell(row, 4).Value = !entry.Found ? "Not Found" :
                                    entry.History == null ? "No Data" : "Found";

            if (entry.History != null)
            {
                ws.Cell(row, 5).Value = entry.History.FirstShiftDate.ToString("yyyy-MM-dd");
                ws.Cell(row, 6).Value = entry.History.LastShiftDate.ToString("yyyy-MM-dd");
                ws.Cell(row, 7).Value = entry.History.TotalMonths;
                ws.Cell(row, 8).Value = entry.History.ActiveMonthsCount;

                var active = entry.History.MonthlyData.Where(m => m.TotalAcceptedOrders > 0).ToList();
                ws.Cell(row, 9).Value = active.Sum(m => m.TotalShifts);
                ws.Cell(row, 10).Value = active.Sum(m => m.TotalAcceptedOrders);
                ws.Cell(row, 11).Value = active.Sum(m => m.TotalRejectedOrders);
                ws.Cell(row, 12).Value = Math.Round((double)active.Sum(m => m.TotalWorkingHours), 1);
                ws.Cell(row, 13).Value = Math.Round(entry.History.AverageOrdersPerActiveMonth, 1);
            }

            var rowRange = ws.Range(row, 1, row, headers.Length);
            rowRange.Style.Fill.BackgroundColor = bg;
            rowRange.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            rowRange.Style.Border.BottomBorderColor = XLColor.LightGray;

            for (int c = 5; c <= headers.Length; c++)
                ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Row(row).Height = 17;
            row++;
        }

        // Auto-fit
        int[] summaryColWidths = { 16, 28, 12, 11, 13, 13, 14, 14, 13, 16, 15, 14, 24 };
        for (int c = 1; c <= summaryColWidths.Length; c++)
            ws.Column(c).Width = summaryColWidths[c - 1];

        ws.SheetView.FreezeRows(1);
        ws.TabColor = XLColor.FromHtml("#1F3864");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SHEET 3 — Not Found
    // ─────────────────────────────────────────────────────────────────────────
    private static void WriteNotFoundSheet(IXLWorksheet ws, List<long> notFound)
    {
        ws.Cell(1, 1).Value = "Iqama No";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = NotFoundBg;

        for (int i = 0; i < notFound.Count; i++)
            ws.Cell(i + 2, 1).Value = notFound[i];

        ws.Column(1).Width = 18;
        ws.TabColor = XLColor.Red;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPER: reuse from ReportService
    // ─────────────────────────────────────────────────────────────────────────
    private static List<MonthlyShiftSummary> GenerateMonthlyShiftSummaries1(
        List<RiderShift> shifts, DateOnly startDate, DateOnly endDate)
    {
        var result = new List<MonthlyShiftSummary>();
        var current = new DateOnly(startDate.Year, startDate.Month, 1);
        var final = new DateOnly(endDate.Year, endDate.Month, 1);

        var byMonth = shifts
            .GroupBy(s => (s.ShiftDate.Year, s.ShiftDate.Month))
            .ToDictionary(g => g.Key, g => g.ToList());

        while (current <= final)
        {
            var key = (current.Year, current.Month);
            if (byMonth.TryGetValue(key, out var ms))
            {
                var total = ms.Count;
                var completed = ms.Count(s => s.ShiftStatus == "Completed");
                result.Add(new MonthlyShiftSummary(
                    Year: current.Year,
                    Month: current.Month,
                    MonthName: new DateTime(current.Year, current.Month, 1).ToString("MMMM"),
                    TotalShifts: total,
                    TotalAcceptedOrders: ms.Sum(s => s.AcceptedDailyOrders),
                    TotalRejectedOrders: ms.Sum(s => s.RejectedDailyOrders),
                    TotalRealRejectedOrders: ms.Sum(s => s.RealRejectedDailyOrders),
                    TotalWorkingHours: ms.Sum(s => s.WorkingHours),
                    CompletedShifts: completed,
                    IncompleteShifts: ms.Count(s => s.ShiftStatus == "Incomplete"),
                    FailedShifts: ms.Count(s => s.ShiftStatus == "Failed"),
                    CompletionRate: total > 0 ? (decimal)completed / total * 100 : 0
                ));
            }
            else
            {
                result.Add(new MonthlyShiftSummary(
                    current.Year, current.Month,
                    new DateTime(current.Year, current.Month, 1).ToString("MMMM"),
                    0, 0, 0, 0, 0, 0, 0, 0, 0));
            }
            current = current.AddMonths(1);
        }
        return result;
    }

    private static string ColumnLetter(int col)
    {
        string result = "";
        while (col > 0)
        {
            col--;
            result = (char)('A' + col % 26) + result;
            col /= 26;
        }
        return result;
    }




    public async Task<Result<HousingPeriodSummaryReport>> GetHousingPeriodSummaryForCompanyAsync(
    int companyId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<HousingPeriodSummaryReport>(
                new Error("End date must be after or equal to start date", "invalid_input", 400));

        try
        {
            var shifts = await _dbcontext.RiderShifts
                .Include(m => m.Housing)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate &&
                           s.CompanyId == companyId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
                return Result.Failure<HousingPeriodSummaryReport>(
                    new Error($"No shifts found for company {companyId} between {startDate:yyyy-MM-dd} and {endDate:yyyy-MM-dd}", "no_data", 404));

            var validShifts = shifts
                .Where(s => s.Housing != null)
                .ToList();

            if (!validShifts.Any())
                return Result.Failure<HousingPeriodSummaryReport>(
                    new Error($"No shifts with housing information found for the specified period", "no_data", 404));

            var totalOrders = validShifts.Sum(s => s.AcceptedDailyOrders);
            var totalRiders = validShifts.Select(s => s.RiderId).Distinct().Count();

            var housingGroups = validShifts
                .GroupBy(s => new
                {
                    HousingId = s.HousingId ?? 0,
                    HousingName = s.Housing?.Name ?? "Unknown"
                });

            var housingSummaries = new List<HousingDailySummary>();

            foreach (var group in housingGroups)
            {
                var housingShifts = group.ToList();
                var housingOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var activeRiders = housingShifts.Select(s => s.RiderId).Distinct().Count();
                var avgOrdersPerRider = activeRiders > 0
                    ? Math.Round((decimal)housingOrders / activeRiders, 2)
                    : 0;
                var percentageOfTotal = totalOrders > 0
                    ? Math.Round((decimal)housingOrders / totalOrders * 100, 2)
                    : 0;

                housingSummaries.Add(new HousingDailySummary(
                    HousingId: group.Key.HousingId,
                    HousingName: group.Key.HousingName,
                    TotalOrders: housingOrders,
                    ActiveRiders: activeRiders,
                    AverageOrdersPerRider: avgOrdersPerRider,
                    PercentageOfTotalOrders: percentageOfTotal
                ));
            }

            housingSummaries = housingSummaries
                .OrderByDescending(h => h.TotalOrders)
                .ToList();

            var avgOrdersPerRiderOverall = totalRiders > 0
                ? Math.Round((decimal)totalOrders / totalRiders, 2)
                : 0;

            var report = new HousingPeriodSummaryReport(
                StartDate: startDate,
                EndDate: endDate,
                HousingSummaries: housingSummaries,
                TotalOrders: totalOrders,
                TotalRiders: totalRiders,
                AverageOrdersPerRider: avgOrdersPerRiderOverall
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingPeriodSummaryReport>(
                new Error($"Error generating housing period summary: {ex.Message}", "server_error", 500));
        }
    }

    public async Task<Result<IEnumerable<DailyCompanyShiftSummary>>> GetDailyShiftSummaryByCompaniesAsync(
    List<int> companyIds,
    CancellationToken cancellationToken = default)
    {
        try
        {
            if (companyIds == null || !companyIds.Any())
            {
                return Result.Failure<IEnumerable<DailyCompanyShiftSummary>>(
                    new Error("InvalidInput", "Company IDs list cannot be empty", 400));
            }

            // Get all shifts for the specified companies
            var shifts = await dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => companyIds.Contains(s.CompanyId))
                .OrderBy(s => s.ShiftDate)
                .ThenBy(s => s.CompanyId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<IEnumerable<DailyCompanyShiftSummary>>(
                    new Error("NotFound", "No shifts found for the specified companies", 404));
            }

            // Group by date and company, then aggregate
            var dailySummaries = shifts
                .GroupBy(s => new { s.ShiftDate, s.CompanyId })
                .Select(g => new DailyCompanyShiftSummary(
                    g.Key.ShiftDate,
                    g.Key.CompanyId,
                    g.Sum(s => s.AcceptedDailyOrders),
                    g.Sum(s => s.RealRejectedDailyOrders),
                    g.Count(),
                    g.Count()
                ))
                .OrderBy(s => s.ShiftDate)
                .ThenBy(s => s.CompanyId)
                .ToList();

            return Result.Success<IEnumerable<DailyCompanyShiftSummary>>(dailySummaries);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<DailyCompanyShiftSummary>>(
                new Error("ServerError", $"Error retrieving daily shift summaries: {ex.Message}", 500));
        }
    }
    public record RiderWorkHistorySummary(
    long IqamaNo,
    string RiderName,
    string WorkingId,
    int TotalMonthsWorked,
    int TotalShifts,
    int TotalOrders,
    string HousingName,
    string Status,
    decimal AverageOrdersPerMonth,
    DateOnly FirstWorkDate,
    DateOnly LastWorkDate,
    List<MonthlyShiftSummary> ActiveMonths,
    string? CompanyName
);

    public async Task<Result<Company2StackedDeliveriesReport>> GetCompany2StackedDeliveriesReportAsync(
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<Company2StackedDeliveriesReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

            // Get all shifts for Company 2 (Keta) in the date range
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                .Where(s => s.CompanyId == 2 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<Company2StackedDeliveriesReport>(
                    new Error($"No shifts found for Company 2 (Keta) between {startDate:yyyy-MM-dd} and {endDate:yyyy-MM-dd}",
                        "no_data", 404));
            }

            // Calculate totals
            var totalShifts = shifts.Count;
            var totalStackedDeliveries = shifts.Sum(s => s.StackedDeliveries);
            var totalAcceptedOrders = shifts.Sum(s => s.AcceptedDailyOrders);
            var totalRiders = shifts.Select(s => s.RiderId).Distinct().Count();

            var stackedDeliveryRate = totalAcceptedOrders > 0
                ? (decimal)totalStackedDeliveries / totalAcceptedOrders * 100
                : 0;

            var avgStackedPerRider = totalRiders > 0
                ? (decimal)totalStackedDeliveries / totalRiders
                : 0;

            var avgStackedPerShift = totalShifts > 0
                ? (decimal)totalStackedDeliveries / totalShifts
                : 0;

            var avgStackedPerDay = totalDays > 0
                ? (decimal)totalStackedDeliveries / totalDays
                : 0;

            // Group by rider and calculate individual statistics
            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var riderDetails = new List<Company2RiderStackedDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var riderStackedTotal = riderShifts.Sum(s => s.StackedDeliveries);
                var riderAcceptedTotal = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var riderShiftCount = riderShifts.Count;

                var maxStackedShift = riderShifts.OrderByDescending(s => s.StackedDeliveries).First();
                var maxStacked = maxStackedShift.StackedDeliveries;
                var maxStackedDate = maxStackedShift.ShiftDate;

                var stackedPercentage = riderAcceptedTotal > 0
                    ? (decimal)riderStackedTotal / riderAcceptedTotal * 100
                    : 0;

                var avgStackedPerShiftRider = riderShiftCount > 0
                    ? (decimal)riderStackedTotal / riderShiftCount
                    : 0;

                riderDetails.Add(new Company2RiderStackedDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: rider.WorkingId ?? "0",
                    HousingName: rider.Employee.Housing?.Name ?? "غير محدد",
                    TotalShifts: riderShiftCount,
                    TotalStackedDeliveries: riderStackedTotal,
                    TotalAcceptedOrders: riderAcceptedTotal,
                    MaxStackedInDay: maxStacked,
                    MaxStackedDate: maxStackedDate,
                    StackedPercentage: stackedPercentage,
                    AverageStackedPerShift: avgStackedPerShiftRider,
                    Rank: 0 // Will be assigned after sorting
                ));
            }

            // Sort by total stacked deliveries and assign ranks
            riderDetails = riderDetails
                .OrderByDescending(r => r.TotalStackedDeliveries)
                .ToList();

            for (int i = 0; i < riderDetails.Count; i++)
            {
                riderDetails[i] = riderDetails[i] with { Rank = i + 1 };
            }

            // Calculate housing breakdowns
            var housingGroups = shifts
                .Where(s => s.Rider?.Employee?.Housing != null)
                .GroupBy(s => s.Rider.Employee.Housing.Name);

            var housingBreakdowns = new List<HousingStackedBreakdown>();

            foreach (var housingGroup in housingGroups)
            {
                var housingShifts = housingGroup.ToList();
                var housingStackedTotal = housingShifts.Sum(s => s.StackedDeliveries);
                var housingAcceptedTotal = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var housingRiderCount = housingShifts.Select(s => s.RiderId).Distinct().Count();

                var housingStackedRate = housingAcceptedTotal > 0
                    ? (decimal)housingStackedTotal / housingAcceptedTotal * 100
                    : 0;

                var avgStackedPerRiderHousing = housingRiderCount > 0
                    ? (decimal)housingStackedTotal / housingRiderCount
                    : 0;

                housingBreakdowns.Add(new HousingStackedBreakdown(
                    HousingName: housingGroup.Key,
                    TotalRiders: housingRiderCount,
                    TotalStackedDeliveries: housingStackedTotal,
                    TotalAcceptedOrders: housingAcceptedTotal,
                    StackedRate: housingStackedRate,
                    AverageStackedPerRider: avgStackedPerRiderHousing
                ));
            }

            // Sort housing by stacked rate
            housingBreakdowns = housingBreakdowns
                .OrderByDescending(h => h.StackedRate)
                .ToList();

            // Create summary
            var topPerformer = riderDetails.FirstOrDefault();
            var summary = new Company2StackedSummary(
                TopStackedDeliveries: topPerformer?.TotalStackedDeliveries ?? 0,
                TopPerformerName: topPerformer?.RiderNameEN ?? "N/A",
                TopPerformerWorkingId: topPerformer?.WorkingId ?? "0",
                CompanyStackedRate: stackedDeliveryRate,
                TotalWorkingDays: shifts.Select(s => s.ShiftDate).Distinct().Count(),
                HousingBreakdowns: housingBreakdowns
            );

            var report = new Company2StackedDeliveriesReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalDays: totalDays,
                CompanyName: "Keta",
                TotalRiders: totalRiders,
                TotalShifts: totalShifts,
                TotalStackedDeliveries: totalStackedDeliveries,
                TotalAcceptedOrders: totalAcceptedOrders,
                StackedDeliveryRate: stackedDeliveryRate,
                AverageStackedPerRider: avgStackedPerRider,
                AverageStackedPerShift: avgStackedPerShift,
                AverageStackedPerDay: avgStackedPerDay,
                RiderDetails: riderDetails,
                Summary: summary
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<Company2StackedDeliveriesReport>(
                new Error($"Error generating Company 2 stacked deliveries report: {ex.Message}",
                    "server_error", 500));
        }
    }

    //public async Task<Result<List<RiderWorkHistorySummary>>> GetAllRidersWorkHistoryAsync(
    //  DateOnly? startDate = null,
    //  DateOnly? endDate = null,
    //  int? companyId = null,          // ★ NEW
    //  CancellationToken cancellationToken = default)
    //{
    //    try
    //    {
    //        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    //        var effectiveEndDate = endDate ?? today;

    //        // ── Base shift query (apply companyId filter here, not on riders) ──
    //        IQueryable<RiderShift> shiftsQuery = _dbcontext.RiderShifts
    //            .Include(s => s.Rider)
    //                .ThenInclude(r => r.Employee)
    //                    .ThenInclude(e => e.Housing)
    //            .Include(s => s.Company);   // ★ include Company on the shift

    //        if (companyId.HasValue)
    //            shiftsQuery = shiftsQuery.Where(s => s.CompanyId == companyId.Value);

    //        if (startDate.HasValue)
    //            shiftsQuery = shiftsQuery.Where(s => s.ShiftDate >= startDate.Value && s.ShiftDate <= effectiveEndDate);
    //        else
    //            shiftsQuery = shiftsQuery.Where(s => s.ShiftDate <= effectiveEndDate);

    //        // Only riders who are not employees
    //        shiftsQuery = shiftsQuery.Where(s => !s.Rider.Employee.IsEmployee);

    //        var allShifts = await shiftsQuery
    //            .AsNoTracking()
    //            .ToListAsync(cancellationToken);

    //        if (!allShifts.Any())
    //            return Result.Success(new List<RiderWorkHistorySummary>());

    //        // ── Group by rider ────────────────────────────────────────────────
    //        var riderGroups = allShifts.GroupBy(s => s.RiderId);
    //        var summaries = new List<RiderWorkHistorySummary>();

    //        foreach (var group in riderGroups)
    //        {
    //            var shifts = group.OrderBy(s => s.ShiftDate).ToList();
    //            var rider = shifts.First().Rider;
    //            if (rider?.Employee == null) continue;

    //            var firstShiftDate = shifts.First().ShiftDate;
    //            var lastShiftDate = shifts.Last().ShiftDate;

    //            var actualStartDate = startDate ?? firstShiftDate;
    //            var actualEndDate = effectiveEndDate > lastShiftDate ? effectiveEndDate : lastShiftDate;

    //            var monthlyData = GenerateMonthlyShiftSummaries(shifts, actualStartDate, actualEndDate);

    //            var activeMonths = monthlyData
    //                .Where(m => m.TotalAcceptedOrders > 0)
    //                .ToList();

    //            var totalMonthsWorked = activeMonths.Count;
    //            var totalShiftsCount = activeMonths.Sum(m => m.TotalShifts);
    //            var totalOrders = activeMonths.Sum(m => m.TotalAcceptedOrders);

    //            var avgOrdersPerMonth = totalMonthsWorked > 0
    //                ? (decimal)totalOrders / totalMonthsWorked
    //                : 0;

    //            // ★ Company name comes from the shift, not from rider.Company
    //            var companyName = shifts.First().Company?.Name ?? "Unknown";

    //            summaries.Add(new RiderWorkHistorySummary(
    //                IqamaNo: rider.EmployeeIqamaNo,
    //                RiderName: rider.Employee.NameAR,
    //                WorkingId: rider.WorkingId ?? "0",
    //                TotalMonthsWorked: totalMonthsWorked,
    //                TotalShifts: totalShiftsCount,
    //                TotalOrders: totalOrders,
    //                HousingName: rider.Employee.Housing?.Name ?? "non",
    //                Status: rider.Employee.Status,
    //                AverageOrdersPerMonth: avgOrdersPerMonth,
    //                FirstWorkDate: firstShiftDate,
    //                LastWorkDate: lastShiftDate,
    //                ActiveMonths: activeMonths,
    //                CompanyName: companyName
    //            ));
    //        }

    //        return Result.Success(
    //            summaries.OrderByDescending(s => s.TotalOrders).ToList());
    //    }
    //    catch (Exception ex)
    //    {
    //        return Result.Failure<List<RiderWorkHistorySummary>>(
    //            new Error($"Error generating riders work history: {ex.Message}", "server_error", 500));
    //    }
    //}

    public async Task<Result<List<RiderWorkHistorySummary>>> GetAllRidersWorkHistoryAsync(
    DateOnly? startDate = null,
    DateOnly? endDate = null,
    int? companyId = null,
    CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var effectiveEndDate = endDate ?? today;

            // ── Single DB query, project only the columns we actually need ──
            //var query = _dbcontext.RiderShifts
            //    .Where(s => !s.Rider.Employee.IsEmployee &&
            //                s.ShiftDate <= effectiveEndDate);

            var query = _dbcontext.RiderShifts
                .Where(s => s.ShiftDate <= effectiveEndDate);

            if (startDate.HasValue)
                query = query.Where(s => s.ShiftDate >= startDate.Value);

            if (companyId.HasValue)
                query = query.Where(s => s.CompanyId == companyId.Value);

            var rawShifts = await query
                .Select(s => new
                {
                    s.RiderId,
                    s.ShiftDate,
                    s.AcceptedDailyOrders,
                    s.RejectedDailyOrders,
                    s.RealRejectedDailyOrders,
                    s.WorkingHours,
                    s.ShiftStatus,
                    CompanyName = s.Company.Name,
                    IqamaNo = s.Rider.EmployeeIqamaNo,
                    WorkingId = s.Rider.WorkingId,
                    RiderNameAR = s.Rider.Employee.NameAR,
                    HousingName = s.Rider.Employee.Housing != null
                                         ? s.Rider.Employee.Housing.Name
                                         : null,
                    EmployeeStatus = s.Rider.Employee.Status
                })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!rawShifts.Any())
                return Result.Success(new List<RiderWorkHistorySummary>());

            // ── Everything below is in-memory — no more DB calls ─────────────
            var summaries = new List<RiderWorkHistorySummary>();

            foreach (var group in rawShifts.GroupBy(s => s.RiderId))
            {
                var orderedShifts = group.OrderBy(s => s.ShiftDate).ToList();
                var first = orderedShifts.First();

                var firstShiftDate = orderedShifts.First().ShiftDate;
                var lastShiftDate = orderedShifts.Last().ShiftDate;
                var actualStartDate = startDate ?? firstShiftDate;
                var actualEndDate = effectiveEndDate > lastShiftDate
                                          ? effectiveEndDate
                                          : lastShiftDate;

                // ── Replicate GenerateMonthlyShiftSummaries logic ─────────────
                var monthlyData = new List<MonthlyShiftSummary>();
                var currentMonth = new DateOnly(actualStartDate.Year, actualStartDate.Month, 1);
                var finalMonth = new DateOnly(actualEndDate.Year, actualEndDate.Month, 1);

                var shiftsByMonth = orderedShifts
                    .GroupBy(s => (s.ShiftDate.Year, s.ShiftDate.Month))
                    .ToDictionary(g => g.Key, g => g.ToList());

                while (currentMonth <= finalMonth)
                {
                    var key = (currentMonth.Year, currentMonth.Month);

                    if (shiftsByMonth.TryGetValue(key, out var monthShifts))
                    {
                        var totalShifts = monthShifts.Count;
                        var completed = monthShifts.Count(s => s.ShiftStatus == "Completed");

                        monthlyData.Add(new MonthlyShiftSummary(
                            Year: currentMonth.Year,
                            Month: currentMonth.Month,
                            MonthName: new DateTime(currentMonth.Year, currentMonth.Month, 1)
                                                         .ToString("MMMM"),
                            TotalShifts: totalShifts,
                            TotalAcceptedOrders: monthShifts.Sum(s => s.AcceptedDailyOrders),
                            TotalRejectedOrders: monthShifts.Sum(s => s.RejectedDailyOrders),
                            TotalRealRejectedOrders: monthShifts.Sum(s => s.RealRejectedDailyOrders),
                            TotalWorkingHours: monthShifts.Sum(s => s.WorkingHours),
                            CompletedShifts: completed,
                            IncompleteShifts: monthShifts.Count(s => s.ShiftStatus == "Incomplete"),
                            FailedShifts: monthShifts.Count(s => s.ShiftStatus == "Failed"),
                            CompletionRate: totalShifts > 0
                                                         ? (decimal)completed / totalShifts * 100
                                                         : 0
                        ));
                    }
                    else
                    {
                        monthlyData.Add(new MonthlyShiftSummary(
                            Year: currentMonth.Year, Month: currentMonth.Month,
                            MonthName: new DateTime(currentMonth.Year, currentMonth.Month, 1).ToString("MMMM"),
                            TotalShifts: 0, TotalAcceptedOrders: 0, TotalRejectedOrders: 0,
                            TotalRealRejectedOrders: 0, TotalWorkingHours: 0,
                            CompletedShifts: 0, IncompleteShifts: 0, FailedShifts: 0,
                            CompletionRate: 0
                        ));
                    }

                    currentMonth = currentMonth.AddMonths(1);
                }

                var activeMonths = monthlyData.Where(m => m.TotalAcceptedOrders > 0).ToList();
                var totalMonthsWorked = activeMonths.Count;
                var totalOrders = activeMonths.Sum(m => m.TotalAcceptedOrders);

                summaries.Add(new RiderWorkHistorySummary(
                    IqamaNo: first.IqamaNo,
                    RiderName: first.RiderNameAR,
                    WorkingId: first.WorkingId ?? "0",
                    TotalMonthsWorked: totalMonthsWorked,
                    TotalShifts: activeMonths.Sum(m => m.TotalShifts),
                    TotalOrders: totalOrders,
                    HousingName: first.HousingName ?? "non",
                    Status: first.EmployeeStatus,
                    AverageOrdersPerMonth: totalMonthsWorked > 0
                                               ? (decimal)totalOrders / totalMonthsWorked
                                               : 0,
                    FirstWorkDate: firstShiftDate,
                    LastWorkDate: lastShiftDate,
                    ActiveMonths: activeMonths,
                    CompanyName: first.CompanyName
                ));
            }

            return Result.Success(
                summaries.OrderByDescending(s => s.TotalOrders).ToList());
        }
        catch (Exception ex)
        {
            return Result.Failure<List<RiderWorkHistorySummary>>(
                new Error($"Error generating riders work history: {ex.Message}", "server_error", 500));
        }
    }
    public async Task<Result<PeriodOrdersComparison>> ComparePeriodOrdersForCompanyAsync(
    int companyId,
    DateOnly period2Start,
    DateOnly period2End,
    CancellationToken cancellationToken = default)
    {
        // Validate period 2 dates
        if (period2End < period2Start)
            return Result.Failure<PeriodOrdersComparison>(
                new Error("Period 2: End date must be after or equal to start date", "invalid_input", 400));

        // Automatically calculate Period 1 (previous month of Period 2)
        var period1Start = period2Start.AddMonths(-1);
        var period1End = period2End.AddMonths(-1);

        try
        {
            // Get shifts for period 1 filtered by company
            var period1Shifts = await _dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= period1Start &&
                           s.ShiftDate <= period1End &&
                           s.CompanyId == companyId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Get shifts for period 2 filtered by company
            var period2Shifts = await _dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= period2Start &&
                           s.ShiftDate <= period2End &&
                           s.CompanyId == companyId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Calculate total orders for each period
            var period1TotalOrders = period1Shifts.Sum(s => s.AcceptedDailyOrders);
            var period2TotalOrders = period2Shifts.Sum(s => s.AcceptedDailyOrders);

            // Calculate difference and percentage
            var ordersDifference = period2TotalOrders - period1TotalOrders;
            var changePercentage = period1TotalOrders > 0
                ? Math.Round(((decimal)ordersDifference / period1TotalOrders) * 100, 2)
                : (period2TotalOrders > 0 ? 100m : 0m);

            // Generate trend description
            var trendDescription = GenerateTrendDescription(
                ordersDifference, changePercentage, period1TotalOrders, period2TotalOrders);

            var comparison = new PeriodOrdersComparison(
                Period1Start: period1Start,
                Period1End: period1End,
                Period2Start: period2Start,
                Period2End: period2End,
                Period1TotalOrders: period1TotalOrders,
                Period2TotalOrders: period2TotalOrders,
                OrdersDifference: ordersDifference,
                ChangePercentage: changePercentage,
                TrendDescription: trendDescription
            );

            return Result.Success(comparison);
        }
        catch (Exception ex)
        {
            return Result.Failure<PeriodOrdersComparison>(
                new Error($"Error comparing periods: {ex.Message}", "server_error", 500));
        }
    }

    public async Task<Result<HousingDailySummaryReport>> GetHousingDailySummaryForCompanyAsync(
        int companyId,
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts for the specified date and company with housing information
            var shifts = await _dbcontext.RiderShifts
                .Include(m => m.Housing)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == reportDate && s.CompanyId == companyId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDailySummaryReport>(
                    new Error($"No shifts found for company {companyId} on {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Filter shifts with valid housing
            var validShifts = shifts
                .Where(s => s.Housing != null)
                .ToList();

            if (!validShifts.Any())
            {
                return Result.Failure<HousingDailySummaryReport>(
                    new Error($"No shifts with housing information found for {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Calculate totals
            var totalOrders = validShifts.Sum(s => s.AcceptedDailyOrders);
            var totalRiders = validShifts.Select(s => s.RiderId).Distinct().Count();

            // Group by housing
            var housingGroups = validShifts
                .GroupBy(s => new
                {
                    HousingId = s.HousingId ?? 0,
                    HousingName = s.Housing?.Name ?? "Unknown"
                });

            var housingSummaries = new List<HousingDailySummary>();

            foreach (var group in housingGroups)
            {
                var housingShifts = group.ToList();
                var housingOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var activeRiders = housingShifts.Select(s => s.RiderId).Distinct().Count();
                var avgOrdersPerRider = activeRiders > 0
                    ? Math.Round((decimal)housingOrders / activeRiders, 2)
                    : 0;
                var percentageOfTotal = totalOrders > 0
                    ? Math.Round((decimal)housingOrders / totalOrders * 100, 2)
                    : 0;

                housingSummaries.Add(new HousingDailySummary(
                    HousingId: group.Key.HousingId,
                    HousingName: group.Key.HousingName,
                    TotalOrders: housingOrders,
                    ActiveRiders: activeRiders,
                    AverageOrdersPerRider: avgOrdersPerRider,
                    PercentageOfTotalOrders: percentageOfTotal
                ));
            }

            // Sort by total orders descending
            housingSummaries = housingSummaries
                .OrderByDescending(h => h.TotalOrders)
                .ToList();

            var avgOrdersPerRiderOverall = totalRiders > 0
                ? Math.Round((decimal)totalOrders / totalRiders, 2)
                : 0;

            var report = new HousingDailySummaryReport(
                ReportDate: reportDate,
                HousingSummaries: housingSummaries,
                TotalOrders: totalOrders,
                TotalRiders: totalRiders,
                AverageOrdersPerRider: avgOrdersPerRiderOverall
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDailySummaryReport>(
                new Error($"Error generating housing daily summary: {ex.Message}", "server_error", 500));
        }
    }

    public async Task<Result<HousingDailyDetailedReport>> GetHousingDailyDetailedReportForCompanyAsync(
        int companyId,
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts for the specified date and company with full details
            var shifts = await _dbcontext.RiderShifts
                .Include(m => m.Housing)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == reportDate && s.CompanyId == companyId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDailyDetailedReport>(
                    new Error($"No shifts found for company {companyId} on {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Filter shifts with valid housing
            var validShifts = shifts
                .Where(s => s.Housing != null)
                .ToList();

            if (!validShifts.Any())
            {
                return Result.Failure<HousingDailyDetailedReport>(
                    new Error($"No shifts with housing information found for {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Calculate grand totals
            var grandTotalOrders = validShifts.Sum(s => s.AcceptedDailyOrders);
            var grandTotalRiders = validShifts.Select(s => s.RiderId).Distinct().Count();

            // Group by housing
            var housingGroups = validShifts
                .GroupBy(s => new
                {
                    HousingId = s.HousingId ?? 0,
                    HousingName = s.Housing?.Name ?? "Unknown"
                });

            var housingDetails = new List<HousingDailyDetails>();

            foreach (var group in housingGroups)
            {
                var housingShifts = group.ToList();
                var housingTotalOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var housingRiderCount = housingShifts.Select(s => s.RiderId).Distinct().Count();
                var percentageOfCompany = grandTotalOrders > 0
                    ? Math.Round((decimal)housingTotalOrders / grandTotalOrders * 100, 2)
                    : 0;

                // Get individual rider performances
                var riderPerformances = housingShifts
                    .Select(s => new RiderDailyPerformance(
                        RiderId: s.RiderId,
                        RiderName: s.Rider?.Employee.NameAR ?? "Unknown",
                        RiderNameE: s.Rider?.Employee.NameEN ?? "Unknown",
                        PhoneNumber: s.Rider?.Employee.Phone ?? "050",
                        WorkingId: s.WorkingId ?? "0",
                        AcceptedOrders: s.AcceptedDailyOrders,
                        WorkingHours: s.WorkingHours,
                        ShiftDate: s.ShiftDate
                    ))
                    .OrderByDescending(r => r.AcceptedOrders)
                    .ToList();

                housingDetails.Add(new HousingDailyDetails(
                    HousingId: group.Key.HousingId,
                    HousingName: group.Key.HousingName,
                    Riders: riderPerformances,
                    HousingTotalOrders: housingTotalOrders,
                    HousingRiderCount: housingRiderCount,
                    PercentageOfCompanyTotal: percentageOfCompany
                ));
            }

            // Sort by total orders descending
            housingDetails = housingDetails
                .OrderByDescending(h => h.HousingTotalOrders)
                .ToList();

            var report = new HousingDailyDetailedReport(
                ReportDate: reportDate,
                HousingDetails: housingDetails,
                GrandTotalOrders: grandTotalOrders,
                GrandTotalRiders: grandTotalRiders
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDailyDetailedReport>(
                new Error($"Error generating housing daily detailed report: {ex.Message}", "server_error", 500));
        }
    }


    public async Task<Result<Company2MonthlyPerformanceDistribution>> GetCompany2MonthlyPerformanceDistributionAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<Company2MonthlyPerformanceDistribution>(
                new Error("End date must be after or equal to start date", "invalid_input", 400));

        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

            // Use the provided end date, or yesterday if end date is in the future
            var currentDate = endDate > today ? today.AddDays(-2) : endDate;

            var totalExpectedDays = (currentDate.DayNumber - startDate.DayNumber) + 1;
            var targetOrdersToDate = totalExpectedDays * TARGET_ORDERS_PER_DAY; // 14 orders per day for Company 2

            // Get all shifts for company 2 in this period
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(e => e.Housing)
                .Where(s => s.CompanyId == 1 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= currentDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<Company2MonthlyPerformanceDistribution>(
                    new Error($"No shifts found for Company 1 between {startDate:yyyy-MM-dd} and {currentDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Group by rider and calculate individual performance
            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var riderPerformanceDetails = new List<RiderPerformanceDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var totalWorkingDays = riderShifts.Count;
                var averageOrdersPerDay = totalWorkingDays > 0 ? (decimal)totalOrders / totalWorkingDays : 0;
                var ordersDifference = totalOrders - targetOrdersToDate;

                // Determine performance tier based on total orders and period length
                var tier = DeterminePerformanceTier(totalOrders, totalExpectedDays);
                var tierDescription = GetTierDescription(tier, totalOrders, totalExpectedDays);

                riderPerformanceDetails.Add(new RiderPerformanceDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: group.OrderByDescending(s => s.ShiftDate).First().WorkingId ?? "0",
                    HousingName: group.OrderByDescending(s => s.ShiftDate).First().Housing?.Name ?? "غير محدد",
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrdersToDate,
                    OrdersDifference: ordersDifference,
                    AverageOrdersPerDay: averageOrdersPerDay,
                    TotalWorkingDays: totalWorkingDays,
                    Tier: tier,
                    TierDescription: tierDescription
                ));
            }

            // Calculate company-wide distribution
            var companySummary = CalculateCompanyDistribution(riderPerformanceDetails);

            // Calculate housing-wise distributions
            var housingDistributions = CalculateHousingDistributions(riderPerformanceDetails);

            // Sort riders by total orders descending
            riderPerformanceDetails = riderPerformanceDetails
                .OrderByDescending(r => r.TotalOrders)
                .ToList();

            var report = new Company2MonthlyPerformanceDistribution(
                Year: startDate.Year,
                Month: startDate.Month,
                StartDate: startDate,
                CurrentDate: currentDate,
                TotalExpectedDays: totalExpectedDays,
                CurrentDayOfMonth: currentDate.Day,
                TargetOrdersToDate: targetOrdersToDate,
                CompanySummary: companySummary,
                HousingDistributions: housingDistributions,
                RiderDetails: riderPerformanceDetails
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<Company2MonthlyPerformanceDistribution>(
                new Error($"Error generating performance distribution: {ex.Message}", "server_error", 500));
        }
    }

    public async Task<Result<RidersBelowMonthlyTargetReport>> GetRidersBelowMonthlyTargetAsync(
        int? year = null,
        int? month = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));
        var reportYear = year ?? today.Year;
        var reportMonth = month ?? today.Month;

        if (reportMonth is < 1 or > 12)
            return Result.Failure<RidersBelowMonthlyTargetReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        if (reportYear < 1)
            return Result.Failure<RidersBelowMonthlyTargetReport>(
                new Error("Year must be greater than 0", "invalid_input", 400));

        try
        {
            var startDate = new DateOnly(reportYear, reportMonth, 1);
            var daysInMonth = DateTime.DaysInMonth(reportYear, reportMonth);
            var monthEndDate = startDate.AddDays(daysInMonth - 1);
            var isCurrentMonth = reportYear == today.Year && reportMonth == today.Month;
            var endDate = isCurrentMonth ? today : monthEndDate;
            var elapsedDays = endDate.Day;

            var companyTargets = new[]
            {
                new { CompanyId = 1, CompanyName = "Hunger", MonthlyTarget = 450 },
                new { CompanyId = 2, CompanyName = "Keeta", MonthlyTarget = 500 }
            };

            var targetByCompany = companyTargets.ToDictionary(t => t.CompanyId);
            var companyIds = targetByCompany.Keys.ToList();

            var targetSummaries = companyTargets
                .Select(t => new CompanyMonthlyTargetSummary(
                    t.CompanyId,
                    t.CompanyName,
                    t.MonthlyTarget,
                    CalculateTargetToDate(t.MonthlyTarget, elapsedDays, daysInMonth)))
                .ToList();

            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Company)
                .Include(s => s.Housing)
                .Where(s => companyIds.Contains(s.CompanyId) &&
                            s.ShiftDate >= startDate &&
                            s.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var workedRiders = shifts
                .Where(s => s.Rider?.Employee != null)
                .GroupBy(s => new { s.RiderId, s.CompanyId })
                .ToList();

            var ridersBelowTarget = new List<RiderBelowMonthlyTargetDetail>();

            foreach (var group in workedRiders)
            {
                if (!targetByCompany.TryGetValue(group.Key.CompanyId, out var companyTarget))
                    continue;

                var riderShifts = group.ToList();
                var latestShift = riderShifts
                    .OrderByDescending(s => s.ShiftDate)
                    .First();
                var rider = latestShift.Rider;
                var totalAcceptedOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var targetToDate = CalculateTargetToDate(
                    companyTarget.MonthlyTarget,
                    elapsedDays,
                    daysInMonth);

                if (totalAcceptedOrders >= targetToDate)
                    continue;

                var totalShifts = riderShifts.Count;
                var companyName = latestShift.Company?.Name;

                ridersBelowTarget.Add(new RiderBelowMonthlyTargetDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: latestShift.WorkingId ?? rider.WorkingId ?? "0",
                    CompanyId: group.Key.CompanyId,
                    CompanyName: string.IsNullOrWhiteSpace(companyName)
                        ? companyTarget.CompanyName
                        : companyName,
                    HousingName: latestShift.Housing?.Name ?? "غير محدد",
                    MonthlyTarget: companyTarget.MonthlyTarget,
                    TargetToDate: targetToDate,
                    TotalAcceptedOrders: totalAcceptedOrders,
                    RemainingToTargetToDate: targetToDate - totalAcceptedOrders,
                    RemainingToMonthlyTarget: Math.Max(companyTarget.MonthlyTarget - totalAcceptedOrders, 0),
                    TotalShifts: totalShifts,
                    AverageOrdersPerShift: totalShifts > 0
                        ? Math.Round((decimal)totalAcceptedOrders / totalShifts, 2)
                        : 0
                ));
            }

            var report = new RidersBelowMonthlyTargetReport(
                Year: reportYear,
                Month: reportMonth,
                StartDate: startDate,
                EndDate: endDate,
                IsCurrentMonth: isCurrentMonth,
                DaysInMonth: daysInMonth,
                ElapsedDays: elapsedDays,
                CompanyTargets: targetSummaries,
                TotalRidersWorked: workedRiders.Count,
                TotalRidersBelowTarget: ridersBelowTarget.Count,
                Riders: ridersBelowTarget
                    .OrderByDescending(r => r.RemainingToTargetToDate)
                    .ThenBy(r => r.CompanyId)
                    .ThenBy(r => r.RiderNameEN)
                    .ToList()
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RidersBelowMonthlyTargetReport>(
                new Error($"Error generating riders below target report: {ex.Message}", "server_error", 500));
        }
    }

    private static int CalculateTargetToDate(int monthlyTarget, int elapsedDays, int daysInMonth)
    {
        return (int)Math.Ceiling((decimal)monthlyTarget * elapsedDays / daysInMonth);
    }

    private (DateOnly startDate, bool isNewRider) GetRiderStartInfo(
        List<RiderShift> riderShifts,
        DateOnly monthStart,
        DateOnly currentEndDate,
        bool workedPreviousMonth)
    {
        if (!riderShifts.Any())
            return (monthStart, true);

        var firstShiftDate = riderShifts.Min(s => s.ShiftDate);

        // If rider worked previous month, they're continuing, not starting
        if (workedPreviousMonth)
        {
            return (monthStart, false); // Expected to work from day 1
        }

        // If rider has shifts before this month in this company, they started from beginning
        if (firstShiftDate <= monthStart)
            return (monthStart, false);

        // Otherwise, they're genuinely starting mid-month
        return (firstShiftDate, true);
    }

    //private PerformanceTier DeterminePerformanceTier(int totalOrders, int totalExpectedDays)
    //{
    //    // Dynamic thresholds based on period length
    //    // Excellent: 14+ orders per day
    //    // Good: 10-13 orders per day
    //    // Poor: below 10 orders per day

    //    var excellentThreshold = 14 * totalExpectedDays;
    //    var goodThreshold = 10 * totalExpectedDays;

    //    if (totalOrders >= excellentThreshold)
    //        return PerformanceTier.Excellent;
    //    else if (totalOrders >= goodThreshold)
    //        return PerformanceTier.Good;
    //    else
    //        return PerformanceTier.Poor;
    //}

    private PerformanceTier DeterminePerformanceTier(int totalOrders, int totalExpectedDays)
    {
        // Scale based on 30-day month targets (450 excellent, 400 good)
        var excellentThreshold = (int)Math.Ceiling(450m / 30 * totalExpectedDays);
        var goodThreshold = (int)Math.Ceiling(400m / 30 * totalExpectedDays);

        if (totalOrders >= excellentThreshold)
            return PerformanceTier.Excellent;
        else if (totalOrders >= goodThreshold)
            return PerformanceTier.Good;
        else
            return PerformanceTier.Poor;
    }

    private string GetTierDescription(PerformanceTier tier, int totalOrders, int totalExpectedDays)
    {
        var excellentThreshold = (int)Math.Ceiling(450m / 30 * totalExpectedDays);
        var goodThreshold = (int)Math.Ceiling(400m / 30 * totalExpectedDays);

        return tier switch
        {
            PerformanceTier.Excellent => $"🌟 ممتاز - {totalOrders} طلب (الهدف: {excellentThreshold} طلب فأكثر)",
            PerformanceTier.Good => $"✅ جيد - {totalOrders} طلب (الهدف: من {goodThreshold} إلى {excellentThreshold - 1} طلب)",
            PerformanceTier.Poor => $"⚠️ يحتاج تحسين - {totalOrders} طلب (أقل من {goodThreshold} طلب)",
            _ => "غير معروف"
        };
    }

    private CompanyPerformanceSummary CalculateCompanyDistribution(
        List<RiderPerformanceDetail> riders)
    {
        var totalRiders = riders.Count;
        var totalOrders = riders.Sum(r => r.TotalOrders);

        var excellentCount = riders.Count(r => r.Tier == PerformanceTier.Excellent);
        var goodCount = riders.Count(r => r.Tier == PerformanceTier.Good);
        var poorCount = riders.Count(r => r.Tier == PerformanceTier.Poor);

        var excellentPercentage = totalRiders > 0 ? (decimal)excellentCount / totalRiders * 100 : 0;
        var goodPercentage = totalRiders > 0 ? (decimal)goodCount / totalRiders * 100 : 0;
        var poorPercentage = totalRiders > 0 ? (decimal)poorCount / totalRiders * 100 : 0;

        var summary = GenerateDistributionSummary(excellentCount, goodCount, poorCount, totalRiders);

        var tierDistribution = new PerformanceTierDistribution(
            ExcellentCount: excellentCount,
            ExcellentPercentage: excellentPercentage,
            GoodCount: goodCount,
            GoodPercentage: goodPercentage,
            PoorCount: poorCount,
            PoorPercentage: poorPercentage,
            Summary: summary
        );

        return new CompanyPerformanceSummary(
            TotalRiders: totalRiders,
            TotalOrders: totalOrders,
            TierDistribution: tierDistribution
        );
    }

    private List<HousingPerformanceDistribution> CalculateHousingDistributions(
        List<RiderPerformanceDetail> allRiders)
    {
        var housingGroups = allRiders.GroupBy(r => r.HousingName);
        var distributions = new List<HousingPerformanceDistribution>();

        foreach (var group in housingGroups)
        {
            var housingName = group.Key;
            var housingRiders = group.ToList();
            var totalRiders = housingRiders.Count;
            var totalOrders = housingRiders.Sum(r => r.TotalOrders);

            var excellentCount = housingRiders.Count(r => r.Tier == PerformanceTier.Excellent);
            var goodCount = housingRiders.Count(r => r.Tier == PerformanceTier.Good);
            var poorCount = housingRiders.Count(r => r.Tier == PerformanceTier.Poor);

            var excellentPercentage = totalRiders > 0 ? (decimal)excellentCount / totalRiders * 100 : 0;
            var goodPercentage = totalRiders > 0 ? (decimal)goodCount / totalRiders * 100 : 0;
            var poorPercentage = totalRiders > 0 ? (decimal)poorCount / totalRiders * 100 : 0;

            var summary = GenerateDistributionSummary(excellentCount, goodCount, poorCount, totalRiders);

            var tierDistribution = new PerformanceTierDistribution(
                ExcellentCount: excellentCount,
                ExcellentPercentage: excellentPercentage,
                GoodCount: goodCount,
                GoodPercentage: goodPercentage,
                PoorCount: poorCount,
                PoorPercentage: poorPercentage,
                Summary: summary
            );

            // Get housing ID from first rider (they all have the same housing)
            var housing = _dbcontext.Housings
                .FirstOrDefault(h => h.Name == housingName);

            distributions.Add(new HousingPerformanceDistribution(
                HousingId: housing?.Id ?? 0,
                HousingName: housingName,
                TotalRiders: totalRiders,
                TotalOrders: totalOrders,
                TierDistribution: tierDistribution,
                Riders: housingRiders.OrderByDescending(r => r.TotalOrders).ToList()
            ));
        }

        return distributions.OrderByDescending(h => h.TotalOrders).ToList();
    }

    private string GenerateDistributionSummary(
        int excellentCount,
        int goodCount,
        int poorCount,
        int totalRiders)
    {
        if (totalRiders == 0)
            return "No riders data available";

        var excellentPercent = (decimal)excellentCount / totalRiders * 100;
        var goodPercent = (decimal)goodCount / totalRiders * 100;
        var poorPercent = (decimal)poorCount / totalRiders * 100;

        if (excellentPercent >= 50)
            return $"🌟 Outstanding: {excellentPercent:F1}% excellent performers";
        else if (excellentPercent + goodPercent >= 70)
            return $"✅ Strong: {excellentPercent:F1}% excellent, {goodPercent:F1}% good";
        else if (poorPercent >= 50)
            return $"⚠️ Attention Needed: {poorPercent:F1}% need improvement";
        else
            return $"📊 Mixed: {excellentPercent:F1}% excellent, {goodPercent:F1}% good, {poorPercent:F1}% improving";
    }


    /// <summary>
    /// Returns the singleton config, seeding a default row when absent.
    /// </summary>
    public async Task<Result<Company2ValidationConfigDto>> GetCompany2ValidationConfigAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cfg = await GetOrCreateConfigAsync(cancellationToken);
            return Result.Success(MapToDto(cfg));
        }
        catch (Exception ex)
        {
            return Result.Failure<Company2ValidationConfigDto>(
                new Error($"Error reading validation config: {ex.Message}", "server_error", 500));
        }
    }

    /// <summary>
    /// Creates the config row if missing, otherwise patches only the provided fields.
    /// </summary>
    public async Task<Result<Company2ValidationConfigDto>> UpsertCompany2ValidationConfigAsync(
        UpsertCompany2ValidationConfigRequest req,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cfg = await GetOrCreateConfigAsync(cancellationToken);

            // Patch only non-null fields (PATCH semantics)
            if (req.TargetOrdersPerDay.HasValue) cfg.TargetOrdersPerDay = req.TargetOrdersPerDay.Value;
            if (req.TargetHoursPerDay.HasValue) cfg.TargetHoursPerDay = req.TargetHoursPerDay.Value;
            if (req.MinWorkingHoursPerDay.HasValue) cfg.MinWorkingHoursPerDay = req.MinWorkingHoursPerDay.Value;
            if (req.FullMonthTargetOrders.HasValue) cfg.FullMonthTargetOrders = req.FullMonthTargetOrders.Value;
            if (req.FirstCriticalDaysCount.HasValue) cfg.FirstCriticalDaysCount = req.FirstCriticalDaysCount.Value;
            if (req.LastCriticalDaysCount.HasValue) cfg.LastCriticalDaysCount = req.LastCriticalDaysCount.Value;
            if (req.MaxStartDayForExistingRiders.HasValue) cfg.MaxStartDayForExistingRiders = req.MaxStartDayForExistingRiders.Value;
            if (req.AllowedMissingDays28.HasValue) cfg.AllowedMissingDays28 = req.AllowedMissingDays28.Value;
            if (req.AllowedMissingDays29.HasValue) cfg.AllowedMissingDays29 = req.AllowedMissingDays29.Value;
            if (req.AllowedMissingDays30.HasValue) cfg.AllowedMissingDays30 = req.AllowedMissingDays30.Value;
            if (req.AllowedMissingDays31.HasValue) cfg.AllowedMissingDays31 = req.AllowedMissingDays31.Value;
            if (req.UpdatedBy is not null) cfg.UpdatedBy = req.UpdatedBy;
            if (req.IsFridayCritical.HasValue) cfg.IsFridayCritical = req.IsFridayCritical.Value;   // ★ NEW
            if (req.IsSaturdayCritical.HasValue) cfg.IsSaturdayCritical = req.IsSaturdayCritical.Value; // ★ NEW

            if (req.IsThursdayCritical.HasValue) cfg.IsThursdayCritical = req.IsThursdayCritical.Value;  // ★ NEW
            if (req.IsFridayCritical.HasValue) cfg.IsFridayCritical = req.IsFridayCritical.Value;
            if (req.IsSaturdayCritical.HasValue) cfg.IsSaturdayCritical = req.IsSaturdayCritical.Value;
            if (req.CriticalDaysOfMonth != null) cfg.CriticalDaysOfMonth = req.CriticalDaysOfMonth;        // ★ NEW

            cfg.UpdatedAt = DateTime.UtcNow.AddHours(3);

            await _dbcontext.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(cfg));
        }
        catch (Exception ex)
        {
            return Result.Failure<Company2ValidationConfigDto>(
                new Error($"Error saving validation config: {ex.Message}", "server_error", 500));
        }
    }

    private static bool IsCriticalDay(
    DateOnly date,
    int startDay,        // rider's effective start day in the month
    int currentDayOfMonth,
    int lastDayOfMonth,
    Company2ValidationConfig cfg)
    {
        var dayNum = date.Day;

        // First critical window: from riderStartDay to (riderStartDay + FirstCriticalDaysCount - 1)
        var firstWindowEnd = Math.Min(startDay + cfg.FirstCriticalDaysCount - 1, currentDayOfMonth);
        if (dayNum >= startDay && dayNum <= firstWindowEnd)
            return true;

        // Last critical window
        var lastWindowCount = lastDayOfMonth == 31
            ? cfg.LastCriticalDaysCount + 1
            : cfg.LastCriticalDaysCount;
        var lastWindowStart = lastDayOfMonth - lastWindowCount + 1;
        if (dayNum >= lastWindowStart && dayNum <= lastDayOfMonth && dayNum <= currentDayOfMonth)
            return true;

        // Critical weekdays
        if (date.DayOfWeek == DayOfWeek.Thursday && cfg.IsThursdayCritical) return true;  // ★ NEW
        if (date.DayOfWeek == DayOfWeek.Friday && cfg.IsFridayCritical) return true;
        if (date.DayOfWeek == DayOfWeek.Saturday && cfg.IsSaturdayCritical) return true;

        // Explicit critical days of month                                                  // ★ NEW
        if (cfg.GetCriticalDaysOfMonthSet().Contains(dayNum)) return true;                 // ★ NEW

        return false;
    }


    // =========================================================================
    // MAIN VALIDATION ENTRY POINT  (replaces the old GetCompany2MonthlyRiderValidationAsync)
    // =========================================================================

    public async Task<Result<MonthlyRiderValidationReport>> GetCompany2MonthlyRiderValidationAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return Result.Failure<MonthlyRiderValidationReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        try
        {
            // ── load dynamic config ──────────────────────────────────────
            var cfg = await GetOrCreateConfigAsync(cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var yesterday = today.AddDays(-1);
            var startDate = new DateOnly(year, month, 1);

            var isCurrentMonth = year == today.Year && month == today.Month;
            var endDate = isCurrentMonth ? yesterday : startDate.AddMonths(1).AddDays(-1);

            var totalExpectedDays = CountExpectedWorkingDays(startDate, endDate, cfg);
            var currentDayOfMonth = endDate.Day;
            var lastDayOfMonth = startDate.AddMonths(1).AddDays(-1).Day;

            // Target orders proportionally scaled to days elapsed
            var targetOrders = isCurrentMonth
                ? (int)Math.Ceiling((decimal)currentDayOfMonth / lastDayOfMonth * cfg.FullMonthTargetOrders)
                : cfg.FullMonthTargetOrders;

            // ── fetch freelancer riders for Company 2 ─────────────────────
            var riders = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                    .ThenInclude(e => e.Housing)
                .Where(r => r.CompanyId == 2 && r.IsFreelancer != true && r.Employee.IsDeleted != true && !r.Employee.IsEmployee && r.Employee.Status.ToLower() == "enable")
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!riders.Any())
                return Result.Failure<MonthlyRiderValidationReport>(
                    new Error($"No freelancer riders found for Company 2", "no_data", 404));

            var riderIds = riders.Select(r => r.Id).ToList();

            // ── fetch their shifts within the period ──────────────────────
            var shifts = await _dbcontext.RiderShifts
                .Where(s => riderIds.Contains(s.RiderId) &&
                            s.CompanyId == 2 &&
                            s.ShiftDate >= startDate &&
                            s.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var shiftsByRider = shifts
                .GroupBy(s => s.RiderId)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.ShiftDate).ToList());

            // ── validate every freelancer rider, even ones with zero shifts ─
            var validationResults = new List<RiderMonthlyValidation>();

            foreach (var rider in riders)
            {
                if (rider.Employee == null) continue;

                var riderShifts = shiftsByRider.TryGetValue(rider.Id, out var list)
                    ? list
                    : new List<RiderShift>();

                var workedPreviousMonth = await DidRiderWorkInPreviousMonthAsync(
                    rider.Id, 2, startDate, cancellationToken);

                var validation = ValidateRider(
                    rider,
                    riderShifts,
                    year,
                    month,
                    currentDayOfMonth,
                    lastDayOfMonth,
                    targetOrders,
                    startDate,
                    endDate,
                    workedPreviousMonth,
                    cfg);

                validationResults.Add(validation);
            }

            var sortedResults = validationResults
                .OrderByDescending(r => r.IsValidForMonth)
                .ThenByDescending(r => r.TotalOrders)
                .ThenBy(r => r.MissingDays)
                .ToList();

            var report = new MonthlyRiderValidationReport(
                Year: year,
                Month: month,
                StartDate: startDate,
                EndDate: endDate,
                IsCurrentMonth: isCurrentMonth,
                CurrentDay: currentDayOfMonth,
                TotalExpectedDays: totalExpectedDays,
                TargetOrders: targetOrders,
                TotalRiders: validationResults.Count,
                ValidRiders: validationResults.Count(r => r.IsValidForMonth),
                InvalidRiders: validationResults.Count(r => !r.IsValidForMonth),
                RiderValidations: sortedResults
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<MonthlyRiderValidationReport>(
                new Error($"Error generating monthly validation report: {ex.Message}", "server_error", 500));
        }
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    /// <summary>Gets the singleton config row, seeding defaults when absent.</summary>
    private async Task<Company2ValidationConfig> GetOrCreateConfigAsync(
        CancellationToken cancellationToken)
    {
        var cfg = await _dbcontext.Set<Company2ValidationConfig>()
            .FirstOrDefaultAsync(cancellationToken);

        if (cfg is not null)
            return cfg;

        cfg = new Company2ValidationConfig();          // all defaults pre-set in entity
        _dbcontext.Set<Company2ValidationConfig>().Add(cfg);
        await _dbcontext.SaveChangesAsync(cancellationToken);
        return cfg;
    }

    private static Company2ValidationConfigDto MapToDto(Company2ValidationConfig cfg) =>
    new(
        cfg.TargetOrdersPerDay,
        cfg.TargetHoursPerDay,
        cfg.MinWorkingHoursPerDay,
        cfg.FullMonthTargetOrders,
        cfg.FirstCriticalDaysCount,
        cfg.LastCriticalDaysCount,
        cfg.MaxStartDayForExistingRiders,
        cfg.AllowedMissingDays28,
        cfg.AllowedMissingDays29,
        cfg.AllowedMissingDays30,
        cfg.AllowedMissingDays31,
        cfg.IsFridayCritical,    // ★ NEW
        cfg.IsSaturdayCritical,  // ★ NEW
        cfg.UpdatedAt,
        cfg.UpdatedBy,
        cfg.IsThursdayCritical,   // ★ NEW
        cfg.CriticalDaysOfMonth
    );


    /// <summary>Counts calendar days in [start, end] that are NOT special/off days.</summary>
    private static int CountExpectedWorkingDays(
        DateOnly start, DateOnly end, Company2ValidationConfig cfg)
    {
        return end.DayNumber - start.DayNumber + 1;
    }

    // ── allowed missing days from config ─────────────────────────────────────

    private static int GetBaseAllowedMissingDays(int lastDayOfMonth, Company2ValidationConfig cfg) =>
        lastDayOfMonth switch
        {
            31 => cfg.AllowedMissingDays31,
            30 => cfg.AllowedMissingDays30,
            29 => cfg.AllowedMissingDays29,
            _ => cfg.AllowedMissingDays28
        };

    // ── start-date / new-rider detection ─────────────────────────────────────

    private static (DateOnly startDate, bool isNewRider) GetRiderStartInfoWithConfig(
        List<RiderShift> riderShifts,
        DateOnly monthStart,
        bool workedPreviousMonth)
    {
        if (!riderShifts.Any())
            return (monthStart, true);

        var firstShiftDate = riderShifts.Min(s => s.ShiftDate);

        if (workedPreviousMonth)
            return (monthStart, false);

        if (firstShiftDate <= monthStart)
            return (monthStart, false);

        return (firstShiftDate, true);
    }

    // ── core per-rider validation ─────────────────────────────────────────────
    private RiderMonthlyValidation ValidateRider(
    RiderDetails rider,
    List<RiderShift> riderShifts,
    int year,
    int month,
    int currentDayOfMonth,
    int lastDayOfMonth,
    int targetOrders,
    DateOnly monthStart,
    DateOnly endDate,
    bool workedPreviousMonth,
    Company2ValidationConfig cfg)
    {
        var (riderStartDate, isNewRider) = GetRiderStartInfoWithConfig(
            riderShifts, monthStart, workedPreviousMonth);

        var actualStartDay = riderStartDate.Day;

        // Existing rider who skipped the first N days → force full-month accountability
        if (workedPreviousMonth && actualStartDay > cfg.MaxStartDayForExistingRiders)
        {
            actualStartDay = 1;
            isNewRider = false;
        }

        // Days the rider is expected to cover (calendar days, special days excluded)
        var checkStartDate = (workedPreviousMonth || !isNewRider)
            ? monthStart
            : new DateOnly(year, month, actualStartDay);

        var expectedWorkingDays = CountExpectedWorkingDays(checkStartDate, endDate, cfg);

        // Adjust target orders
        int adjustedTargetOrders;
        if (workedPreviousMonth || !isNewRider)
        {
            adjustedTargetOrders = targetOrders;
        }
        else
        {
            var fullMonthWorkingDays = CountExpectedWorkingDays(monthStart, endDate, cfg);
            adjustedTargetOrders = fullMonthWorkingDays > 0
                ? (int)Math.Ceiling((decimal)expectedWorkingDays / fullMonthWorkingDays * cfg.FullMonthTargetOrders)
                : 0;
        }

        // Allowed missing days (proportional for new riders)
        var baseAllowed = GetBaseAllowedMissingDays(lastDayOfMonth, cfg);
        int allowedMissingDays;
        if (workedPreviousMonth || !isNewRider)
        {
            allowedMissingDays = baseAllowed;
        }
        else
        {
            var fullMonthWorkingDays = CountExpectedWorkingDays(monthStart, endDate, cfg);
            allowedMissingDays = fullMonthWorkingDays > 0
                ? Math.Max(1, (int)Math.Floor((decimal)expectedWorkingDays / fullMonthWorkingDays * baseAllowed))
                : 1;
        }

        // ── day-by-day loop ───────────────────────────────────────────────
        var shiftsByDate = riderShifts.ToDictionary(s => s.ShiftDate);
        var goodDays = 0;
        var missingDays = new List<int>();
        var lowHoursDays = new List<int>();
        var lowOrdersOnCriticalDays = new List<int>();
        var dailyDetails = new List<DailyValidationDetail>();

        for (var d = checkStartDate; d <= endDate; d = d.AddDays(1))
        {
            var dayNum = d.Day;


            // Determine whether today is a critical day
            var isDayCritical = IsCriticalDay(d, actualStartDay, currentDayOfMonth, lastDayOfMonth, cfg);

            if (shiftsByDate.TryGetValue(d, out var shift))
            {
                if (shift.WorkingHours < cfg.MinWorkingHoursPerDay)
                {
                    // Worked but hours too low → counts as missing
                    lowHoursDays.Add(dayNum);
                    missingDays.Add(dayNum);
                    dailyDetails.Add(new DailyValidationDetail(
                        Day: dayNum,
                        Date: d,
                        HasShift: true,
                        WorkingHours: shift.WorkingHours,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        IsValid: false,
                        Reason: $"ساعات العمل ({shift.WorkingHours:F1}h) أقل من {cfg.MinWorkingHoursPerDay}h"
                    ));
                }
                else if (isDayCritical && shift.AcceptedDailyOrders < cfg.TargetOrdersPerDay)
                {
                    // Worked a critical day but orders below daily target → counts as missing
                    lowOrdersOnCriticalDays.Add(dayNum);
                    missingDays.Add(dayNum);
                    dailyDetails.Add(new DailyValidationDetail(
                        Day: dayNum,
                        Date: d,
                        HasShift: true,
                        WorkingHours: shift.WorkingHours,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        IsValid: false,
                        Reason: $"⚠️ يوم حرج: الطلبات ({shift.AcceptedDailyOrders}) أقل من الهدف ({cfg.TargetOrdersPerDay})"
                    ));
                }
                else
                {
                    goodDays++;
                    dailyDetails.Add(new DailyValidationDetail(
                        Day: dayNum,
                        Date: d,
                        HasShift: true,
                        WorkingHours: shift.WorkingHours,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        IsValid: true,
                        Reason: "✓ صالح"
                    ));
                }
            }
            else
            {
                missingDays.Add(dayNum);
                dailyDetails.Add(new DailyValidationDetail(
                    Day: dayNum,
                    Date: d,
                    HasShift: false,
                    WorkingHours: 0,
                    AcceptedOrders: 0,
                    IsValid: false,
                    Reason: "No shift"
                ));
            }
        }

        var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
        var totalHours = riderShifts.Sum(s => s.WorkingHours);

        var validationResult = PerformValidationWithConfig(
            totalMissingDays: missingDays.Count,
            missingDays: missingDays,
            daysWithLowHours: lowHoursDays,
            daysWithLowOrdersOnCriticalDay: lowOrdersOnCriticalDays,
            totalOrders: totalOrders,
            targetOrders: adjustedTargetOrders,
            currentDayOfMonth: currentDayOfMonth,
            lastDayOfMonth: lastDayOfMonth,
            allowedMissingDays: allowedMissingDays,
            checkStartDate: checkStartDate,
            expectedWorkingDays: expectedWorkingDays,
            workedPreviousMonth: workedPreviousMonth,
            isNewRider: isNewRider,
            cfg: cfg
        );

        return new RiderMonthlyValidation(
            rider.Employee.Housing?.Name ?? "unknown",
            RiderId: rider.Id,
            IqamaNo: rider.EmployeeIqamaNo,
            RiderNameAR: rider.Employee.NameAR,
            RiderNameEN: rider.Employee.NameEN,
            WorkingId: rider.WorkingId ?? "0",
            TotalExpectedDays: expectedWorkingDays,
            TotalWorkingDays: goodDays,
            GoodDays: goodDays,
            MissingDays: missingDays.Count,
            MissingDaysList: missingDays,
            DaysWithLessThan10Hours: lowHoursDays,
            DaysWithLowOrdersOnCriticalDay: lowOrdersOnCriticalDays,
            TotalOrders: totalOrders,
            TargetOrders: adjustedTargetOrders,
            TotalWorkingHours: totalHours,
            AverageHoursPerDay: goodDays > 0 ? totalHours / goodDays : 0,
            IsValidForMonth: validationResult.IsValid,
            ValidationErrors: validationResult.Errors,
            DailyDetails: dailyDetails
        );
    }
    // ── validation rule engine ────────────────────────────────────────────────


    // REPLACE the method signature:
    private ValidationResult PerformValidationWithConfig(
        int totalMissingDays,
        List<int> missingDays,
        List<int> daysWithLowHours,
        List<int> daysWithLowOrdersOnCriticalDay,   // ★ NEW parameter
        int totalOrders,
        int targetOrders,
        int currentDayOfMonth,
        int lastDayOfMonth,
        int allowedMissingDays,
        DateOnly checkStartDate,
        int expectedWorkingDays,
        bool workedPreviousMonth,
        bool isNewRider,
        Company2ValidationConfig cfg)
    {
        var isValid = true;
        var errors = new List<string>();
        var startDay = checkStartDate.Day;

        // ── Rule: existing rider missed opening days ───────────────────────
        if (workedPreviousMonth && startDay > cfg.MaxStartDayForExistingRiders)
        {
            isValid = false;
            errors.Add($"❌ الموظف عمل الشهر الماضي ولكن غاب أول {startDay - 1} أيام - غير صالح");
        }

        // ── Rule: total missing days ──────────────────────────────────────
        if (totalMissingDays > allowedMissingDays)
        {
            isValid = false;
            errors.Add($"❌ عدد أيام الغياب كبير جدًا: {totalMissingDays} (الحد الأقصى: {allowedMissingDays})");
        }

        // ── Rider-type info line ──────────────────────────────────────────
        if (!isNewRider || workedPreviousMonth)
            errors.Add($"ℹ️ موظف مستمر من الشهر الماضي (متوقع العمل من يوم 1)");
        else if (startDay > 1)
            errors.Add($"ℹ️ موظف جديد بدأ العمل من يوم {startDay} (الأيام المتوقعة: {expectedWorkingDays})");

        // ── Rule: critical days (first window) ───────────────────────────
        var firstWindowEnd = Math.Min(startDay + cfg.FirstCriticalDaysCount - 1, currentDayOfMonth);
        var firstWindowDays = Enumerable.Range(startDay, firstWindowEnd - startDay + 1).ToList();
        var missingInFirst = firstWindowDays.Intersect(missingDays).ToList();

        // ── Rule: critical days (last window) ────────────────────────────
        var lastWindowCount = lastDayOfMonth == 31
            ? cfg.LastCriticalDaysCount + 1
            : cfg.LastCriticalDaysCount;
        var lastWindowStart = Math.Max(startDay, lastDayOfMonth - lastWindowCount + 1);
        var lastWindowDays = Enumerable.Range(lastWindowStart, lastDayOfMonth - lastWindowStart + 1)
            .Where(d => d <= currentDayOfMonth && d >= startDay)
            .ToList();
        var missingInLast = lastWindowDays.Intersect(missingDays).ToList();

        var criticalViolation = false;

        if (missingInFirst.Count > 1)
        {
            isValid = criticalViolation = true ? (isValid = false) : false;
            errors.Add($"❌ غياب في أول {cfg.FirstCriticalDaysCount} أيام: {missingInFirst.Count} أيام ({string.Join(", ", missingInFirst)}) - المسموح يوم واحد فقط");
        }

        if (missingInFirst.Any() && missingInLast.Any())
        {
            isValid = false;
            criticalViolation = true;
            errors.Add($"❌ غياب في أول ({string.Join(", ", missingInFirst)}) وآخر ({string.Join(", ", missingInLast)}) الشهر - غير مسموح");
        }

        if (missingInLast.Count > 1)
        {
            isValid = false;
            criticalViolation = true;
            errors.Add($"❌ غياب في آخر {cfg.LastCriticalDaysCount} أيام: {missingInLast.Count} أيام ({string.Join(", ", missingInLast)}) - المسموح يوم واحد فقط");
        }

        if (!criticalViolation && (missingInFirst.Any() || missingInLast.Any()))
        {
            var parts = new List<string>();
            if (missingInFirst.Any()) parts.Add($"يوم {missingInFirst[0]} في أول {cfg.FirstCriticalDaysCount} أيام");
            if (missingInLast.Any()) parts.Add($"يوم {missingInLast[0]} في آخر {cfg.LastCriticalDaysCount} أيام");
            errors.Add($"ℹ️ غياب في الفترات الحرجة: {string.Join(" و ", parts)} (ضمن الحد المسموح)");
        }

        // ★ NEW ── Rule: low orders on critical weekdays (Friday/Saturday) ──
        //   Days that are critical weekdays (not inside first/last windows) and
        //   where the rider worked but fell short of TargetOrdersPerDay.
        var criticalWeekdayLowOrders = daysWithLowOrdersOnCriticalDay
            .Except(firstWindowDays)
            .Except(lastWindowDays)
            .ToList();

        if (criticalWeekdayLowOrders.Any())
        {
            isValid = false;
            errors.Add($"❌ أيام حرجة (أسبوعية) بطلبات أقل من الهدف ({cfg.TargetOrdersPerDay}): " +
                       $"الأيام {string.Join(", ", criticalWeekdayLowOrders)}");
        }

        // ★ NEW ── Info: critical window days with low orders (already inside ──
        //   first/last windows — they are already counted in missingDays and the
        //   window violation rules above, but we add an explanatory line).
        var criticalWindowLowOrders = daysWithLowOrdersOnCriticalDay
            .Intersect(firstWindowDays.Concat(lastWindowDays))
            .ToList();

        if (criticalWindowLowOrders.Any())
        {
            errors.Add($"⚠️ أيام حرجة (أول/آخر الشهر) بطلبات أقل من الهدف ({cfg.TargetOrdersPerDay}): " +
                       $"الأيام {string.Join(", ", criticalWindowLowOrders)}");
        }

        // ── Rule: order target ────────────────────────────────────────────
        if (totalOrders < targetOrders)
        {
            isValid = false;
            errors.Add($"❌ عدد الطلبات غير كافٍ: {totalOrders} (المطلوب: {targetOrders}، النقص: {targetOrders - totalOrders})");
        }

        // ── Info: low-hour days ───────────────────────────────────────────
        if (daysWithLowHours.Any())
            errors.Add($"⚠️ أيام عمل أقل من {cfg.MinWorkingHoursPerDay}h (تُحتسب غياباً): الأيام {string.Join(", ", daysWithLowHours)}");

        // ── Info: regular absence dates ───────────────────────────────────
        var regularMissing = missingDays
            .Except(daysWithLowHours)
            .Except(daysWithLowOrdersOnCriticalDay)  // ★ exclude new category from "regular" list
            .ToList();
        if (regularMissing.Any())
            errors.Add($"⚠️ أيام بدون دوام: {string.Join(", ", regularMissing)}");

        if (!errors.Any())
            errors.Add("✅ جميع شروط التحقق مستوفاة");

        return new ValidationResult(isValid, errors);
    }

    private record ValidationResult(bool IsValid, List<string> Errors);

    // Add these methods to ReportService.cs

    /// <summary>
    /// Get daily summary report for Company 2 (Keta) - Report 1: تقرير الى 13-01-2026
    /// </summary>
    public async Task<Result<Company2DailySummaryReport>> GetCompany2DailySummaryAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var monthStart = new DateOnly(reportDate.Year, reportDate.Month, 1);

            // Get all shifts for Company 2 from start of month to report date
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.CompanyId == 2 &&
                           s.ShiftDate >= monthStart &&
                           s.ShiftDate <= reportDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<Company2DailySummaryReport>(
                    new Error($"No shifts found for Company 2 up to {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
            var totalShifts = shifts.Sum(s => s.RejectedDailyOrders);
            var totalWorkingHours = shifts.Sum(s => s.WorkingHours);
            var avgWorkingHours = totalShifts > 0 ? totalWorkingHours / totalShifts : 0;

            // Calculate on-time delivery rate
            var totalOnTimeDeliveries = 0;

            var report = new Company2DailySummaryReport(
                ReportDate: reportDate,
                PeriodStart: monthStart,
                PeriodEnd: reportDate,
                TotalOrdersDelivered: totalOrders,
                AverageWorkingHours: avgWorkingHours,
                TotalShifts: totalShifts,
                TotalRiders: shifts.Select(s => s.RiderId).Distinct().Count()
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<Company2DailySummaryReport>(
                new Error($"Error generating Company 2 daily summary: {ex.Message}", "server_error", 500));
        }
    }
    // Updated method for GetCompany2CumulativeRiderStatsAsync
    // Replace the existing method in ReportService.cs with this implementation

    public async Task<Result<Company2CumulativeRiderReport>> GetCompany2CumulativeRiderStatsAsync(
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var monthStart = new DateOnly(endDate.Year, endDate.Month, 1);
            var totalExpectedDays = endDate.Day;

            // Get all shifts for Company 2 in this period
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Housing)
                .Where(s => s.CompanyId == 2 &&
                           s.ShiftDate >= monthStart &&
                           s.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<Company2CumulativeRiderReport>(
                    new Error($"No shifts found for Company 2 up to {endDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Group by rider
            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var riderStats = new List<Company2RiderCumulativeStats>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.OrderBy(s => s.ShiftDate).ToList();

                // Check if rider worked in previous month with this company
                var workedPreviousMonth = await DidRiderWorkInPreviousMonthAsync(
                    rider.Id,
                    2, // Company 2 (Keta)
                    monthStart,
                    cancellationToken);

                // Get rider's actual start date in this month
                var riderStartDate = riderShifts.First().ShiftDate;

                // Determine if this is a new rider (didn't work previous month and started after day 1)
                var isNewRider = !workedPreviousMonth && riderStartDate > monthStart;

                // Calculate expected days based on when they started
                int riderExpectedDays;
                int targetOrders;

                if (isNewRider)
                {
                    // For new riders, calculate from their start date to end date
                    riderExpectedDays = endDate.DayNumber - riderStartDate.DayNumber + 1;
                    targetOrders = riderExpectedDays * TARGET_ORDERS_PER_DAY2;
                }
                else
                {
                    // For existing riders (worked previous month), use full month expectation
                    riderExpectedDays = totalExpectedDays;
                    targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY2;
                }

                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var workingDays = riderShifts.Count;
                var avgOrdersPerDay = workingDays > 0 ? (float)totalOrders / workingDays : 0;

                // Calculate deficit/surplus (العجز) based on adjusted target
                var deficitOrSurplus = totalOrders - targetOrders;

                var housingName = riderShifts.FirstOrDefault()?.Housing?.Name ?? "غير محدد";

                riderStats.Add(new Company2RiderCumulativeStats(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    WorkingId: rider.WorkingId ?? "0",
                    TotalOrders: totalOrders,
                    AverageOrdersPerDay: avgOrdersPerDay,
                    DeficitOrSurplus: deficitOrSurplus,
                    HousingGroup: housingName,
                    ExpectedDays: riderExpectedDays,
                    TargetOrders: targetOrders,
                    IsNewRider: isNewRider,
                    StartDate: riderStartDate
                ));
            }

            // Sort by total orders descending
            riderStats = riderStats.OrderByDescending(r => r.TotalOrders).ToList();

            // Assign ranks
            for (int i = 0; i < riderStats.Count; i++)
            {
                riderStats[i] = riderStats[i] with { Rank = i + 1 };
            }

            var report = new Company2CumulativeRiderReport(
                PeriodStart: monthStart,
                PeriodEnd: endDate,
                TotalExpectedDays: totalExpectedDays,
                RiderStats: riderStats,
                TotalOrdersAllRiders: riderStats.Sum(r => r.TotalOrders)
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<Company2CumulativeRiderReport>(
                new Error($"Error generating Company 2 cumulative stats: {ex.Message}", "server_error", 500));
        }
    }


    private async Task<bool> DidRiderWorkInPreviousMonthAsync(
    int riderId,
    int companyId,
    DateOnly monthStart,
    CancellationToken cancellationToken)
    {
        // Calculate previous month range
        var previousMonthStart = monthStart.AddMonths(-1);
        var previousMonthEnd = monthStart.AddDays(-1);

        // Check if rider has any shifts in previous month for this company
        var hasShifts = await _dbcontext.RiderShifts
            .AnyAsync(s => s.RiderId == riderId &&
                          s.CompanyId == companyId &&
                          s.ShiftDate >= previousMonthStart &&
                          s.ShiftDate <= previousMonthEnd,
                          cancellationToken);

        return hasShifts;
    }

    /// <summary>
    /// Get daily rider details for Company 2 - Report 3: طلبات 13-01-2026
    /// </summary>
    public async Task<Result<Company2DailyRiderDetailsReport>> GetCompany2DailyRiderDetailsAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts for Company 2 on this specific date
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Housing)
                .Where(s => s.CompanyId == 2 && s.ShiftDate == reportDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<Company2DailyRiderDetailsReport>(
                    new Error($"No shifts found for Company 2 on {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            var riderDetails = shifts
                .Select(s => new Company2DailyRiderDetail(
                    RiderId: s.RiderId,
                    IqamaNo: s.Rider?.EmployeeIqamaNo ?? 0,
                    RiderNameAR: s.Rider?.Employee.NameAR ?? "Unknown",
                    WorkingId: s.WorkingId ?? "0",
                    OrderCount: s.AcceptedDailyOrders,
                    WorkingHours: s.WorkingHours,
                    HousingGroup: s.Housing?.Name ?? "غير محدد",
                    DriverAppConnectionTime: s.CreatedAt
                ))
                .OrderByDescending(r => r.OrderCount)
                .ToList();

            // Assign ranks
            for (int i = 0; i < riderDetails.Count; i++)
            {
                riderDetails[i] = riderDetails[i] with { Rank = i + 1 };
            }

            var report = new Company2DailyRiderDetailsReport(
                ReportDate: reportDate,
                RiderDetails: riderDetails,
                TotalOrders: riderDetails.Sum(r => r.OrderCount),
                TotalRiders: riderDetails.Count,
                AverageOrdersPerRider: riderDetails.Count > 0
                    ? (decimal)riderDetails.Sum(r => r.OrderCount) / riderDetails.Count
                    : 0
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<Company2DailyRiderDetailsReport>(
                new Error($"Error generating Company 2 daily rider details: {ex.Message}", "server_error", 500));
        }
    }

    // Record definitions - add to IReportService.cs

    public record Company2DailySummaryReport(
        DateOnly ReportDate,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int TotalOrdersDelivered,
        float AverageWorkingHours,
        int TotalShifts,
        int TotalRiders
    );

    public record Company2CumulativeRiderReport(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int TotalExpectedDays,
        List<Company2RiderCumulativeStats> RiderStats,
        int TotalOrdersAllRiders
    );


    public record Company2RiderCumulativeStats(
        int RiderId,
        long IqamaNo,
        string RiderNameAR,
        string WorkingId,
        int TotalOrders,
        float AverageOrdersPerDay,
        int DeficitOrSurplus,
        string HousingGroup,
        int ExpectedDays,         // NEW: Actual expected days for this rider
        int TargetOrders,         // NEW: Adjusted target based on expected days
        bool IsNewRider,          // NEW: Flag indicating if rider is new this month
        DateOnly StartDate,       // NEW: Date when rider started in this month
        int Rank = 0
    );


    public record Company2DailyRiderDetailsReport(
        DateOnly ReportDate,
        List<Company2DailyRiderDetail> RiderDetails,
        int TotalOrders,
        int TotalRiders,
        decimal AverageOrdersPerRider
    );

    public record Company2DailyRiderDetail(
        int RiderId,
        long IqamaNo,
        string RiderNameAR,
        string WorkingId,
        int OrderCount,
        float WorkingHours,
        string HousingGroup,
        DateTime DriverAppConnectionTime,
        int Rank = 0
    );
    // Records for Validation Results
    public record MonthlyRiderValidationReport(
    int Year,
    int Month,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsCurrentMonth,
    int CurrentDay,
    int TotalExpectedDays,
    int TargetOrders,
    int TotalRiders,
    int ValidRiders,
    int InvalidRiders,
    List<RiderMonthlyValidation> RiderValidations
);

    public record RiderMonthlyValidation(
        string HousingName,
        int RiderId,
        long IqamaNo,
        string RiderNameAR,
        string RiderNameEN,
        string WorkingId,
        int TotalExpectedDays,
        int TotalWorkingDays,
        int GoodDays,
        int MissingDays,
        List<int> MissingDaysList,
        List<int> DaysWithLessThan10Hours,
        List<int> DaysWithLowOrdersOnCriticalDay,  // ★ NEW
        int TotalOrders,
        int TargetOrders,
        float TotalWorkingHours,
        float AverageHoursPerDay,
        bool IsValidForMonth,
        List<string> ValidationErrors,
        List<DailyValidationDetail> DailyDetails
    );

    public record DailyValidationDetail(
        int Day,
        DateOnly Date,
        bool HasShift,
        float WorkingHours,
        int AcceptedOrders,
        bool IsValid,
        string Reason
    );

    public async Task<Result<HousingDetailedDailyPerformanceReport>> GetHousingDetailedDailyPerformanceAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<HousingDetailedDailyPerformanceReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

            // Get all shifts for company 1 with housing data
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(e => e.Housing)
                .Where(s => s.CompanyId == 1 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDetailedDailyPerformanceReport>(
                    new Error("No shifts found for the specified period", "no_data", 404));
            }

            var allShiftRiderIds = shifts.Select(s => s.RiderId).Distinct().ToList();

            var walletByRider = await _dbcontext.Wallets
                    .Where(w => allShiftRiderIds.Contains(w.WorkedRiderId)
                             && w.Date >= startDate
                             && w.Date <= endDate)
                    .ToDictionaryAsync(
                        w => w.WorkedRiderId,
                        w => w.Amount);

            // Group by housing
            var housingGroups = shifts
                .GroupBy(s => new
                {
                    HousingId = s.HousingId ?? 0,
                    HousingName = s.Housing?.Name ?? "غير محدد"
                });

            var housingDetails = new List<HousingPerformanceDetail>();

            // Track global metrics
            int globalTotalWorkingDays = 0;
            int globalTotalAbsentDays = 0;
            float globalTotalHours = 0;
            float globalTotalTargetHours = 0;
            int globalTotalOrders = 0;
            int globalTotalTargetOrders = 0;

            foreach (var housingGroup in housingGroups)
            {
                var housingShifts = housingGroup.ToList();
                var riderGroups = housingShifts.GroupBy(s => s.RiderId);
                var riderDetails = new List<RiderDailyPerformanceDetail>();

                // Housing-level metrics
                int housingTotalWorkingDays = 0;
                int housingTotalAbsentDays = 0;
                float housingTotalHours = 0;
                float housingTotalTargetHours = 0;
                int housingTotalOrders = 0;
                int housingTotalTargetOrders = 0;
                var attendanceRates = new List<decimal>();
                var hoursCompletionRates = new List<decimal>();
                var ordersCompletionRates = new List<decimal>();

                foreach (var riderGroup in riderGroups)
                {
                    var rider = riderGroup.First().Rider;
                    if (rider?.Employee == null) continue;

                    var riderShifts = riderGroup.ToList();
                    var shiftDictionary = riderShifts.ToDictionary(s => s.ShiftDate);

                    // Build daily entries
                    var dailyEntries = new List<DailyPerformanceEntry>();
                    var currentDate = startDate;
                    int workingDays = 0;
                    int absentDays = 0;
                    float totalHours = 0;
                    int totalOrders = 0;
                    int totalRejected = 0;

                    while (currentDate <= endDate)
                    {
                        if (shiftDictionary.TryGetValue(currentDate, out var shift))
                        {
                            workingDays++;
                            totalHours += shift.WorkingHours;
                            totalOrders += shift.AcceptedDailyOrders;
                            totalRejected += shift.RealRejectedDailyOrders;

                            var hoursDiff = shift.WorkingHours - TARGET_HOURS_PER_DAY;
                            var ordersDiff = shift.AcceptedDailyOrders - TARGET_ORDERS_PER_DAY;

                            dailyEntries.Add(new DailyPerformanceEntry(
                                Date: currentDate,
                                IsPresent: true,
                                WorkingHours: shift.WorkingHours,
                                TargetHours: TARGET_HOURS_PER_DAY,
                                HoursDifference: hoursDiff,
                                AcceptedOrders: shift.AcceptedDailyOrders,
                                RejectedOrders: shift.RejectedDailyOrders,
                                TargetOrders: TARGET_ORDERS_PER_DAY,
                                OrdersDifference: ordersDiff,
                                ShiftStatus: shift.ShiftStatus,
                                PerformanceLevel: DeterminePerformanceLevel(
                                    shift.WorkingHours,
                                    shift.AcceptedDailyOrders,
                                    TARGET_HOURS_PER_DAY,
                                    TARGET_ORDERS_PER_DAY)
                            ));
                        }
                        else
                        {
                            absentDays++;
                            dailyEntries.Add(new DailyPerformanceEntry(
                                Date: currentDate,
                                IsPresent: false,
                                WorkingHours: 0,
                                TargetHours: TARGET_HOURS_PER_DAY,
                                HoursDifference: -TARGET_HOURS_PER_DAY,
                                AcceptedOrders: 0,
                                RejectedOrders: 0,
                                TargetOrders: TARGET_ORDERS_PER_DAY,
                                OrdersDifference: -TARGET_ORDERS_PER_DAY,
                                ShiftStatus: "Absent",
                                PerformanceLevel: "Absent"
                            ));
                        }
                        currentDate = currentDate.AddDays(1);
                    }

                    // Calculate rider summary metrics
                    var targetHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
                    var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
                    var attendanceRate = (decimal)workingDays / totalExpectedDays * 100;
                    var hoursCompletionRate = targetHours > 0 ? (decimal)totalHours / (decimal)targetHours * 100 : 0;
                    var ordersCompletionRate = targetOrders > 0 ? (decimal)totalOrders / targetOrders * 100 : 0;
                    var overallScore = (attendanceRate + hoursCompletionRate + ordersCompletionRate) / 3;

                    var riderWalletAmount = walletByRider.TryGetValue(rider.Id, out var walletAmt)
                            ? walletAmt
                            : 0m;

                    var periodSummary = new RiderPeriodSummary(
                        TotalWorkingDays: workingDays,
                        TotalAbsentDays: absentDays,
                        TotalWorkingHours: totalHours,
                        TotalTargetHours: targetHours,
                        TotalHoursDifference: totalHours - targetHours,
                        TotalAcceptedOrders: totalOrders,
                        TotalRejectedOrders: totalRejected,
                        TotalTargetOrders: targetOrders,
                        TotalOrdersDifference: totalOrders - targetOrders,
                        AverageHoursPerDay: workingDays > 0 ? totalHours / workingDays : 0,
                        AverageOrdersPerDay: workingDays > 0 ? (decimal)totalOrders / workingDays : 0,
                        AttendanceRate: attendanceRate,
                        HoursCompletionRate: hoursCompletionRate,
                        OrdersCompletionRate: ordersCompletionRate,
                        OverallPerformanceScore: overallScore,
                        riderWalletAmount
                    );

                    riderDetails.Add(new RiderDailyPerformanceDetail(
                        RiderId: rider.Id,
                        IqamaNo: rider.EmployeeIqamaNo,
                        RiderNameAR: rider.Employee.NameAR,
                        RiderNameEN: rider.Employee.NameEN,
                        WorkingId: riderShifts.OrderByDescending(s => s.ShiftDate).First().WorkingId,
                        DailyEntries: dailyEntries,
                        PeriodSummary: periodSummary
                    ));

                    // Accumulate housing metrics
                    housingTotalWorkingDays += workingDays;
                    housingTotalAbsentDays += absentDays;
                    housingTotalHours += totalHours;
                    housingTotalTargetHours += targetHours;
                    housingTotalOrders += totalOrders;
                    housingTotalTargetOrders += targetOrders;
                    attendanceRates.Add(attendanceRate);
                    hoursCompletionRates.Add(hoursCompletionRate);
                    ordersCompletionRates.Add(ordersCompletionRate);
                }

                // Sort riders by overall performance score
                riderDetails = riderDetails
                    .OrderByDescending(r => r.PeriodSummary.OverallPerformanceScore)
                    .ToList();

                // Calculate housing summary
                var housingSummary = new HousingSummaryMetrics(
                    TotalRiders: riderDetails.Count,
                    TotalWorkingDays: housingTotalWorkingDays,
                    TotalAbsentDays: housingTotalAbsentDays,
                    TotalWorkingHours: housingTotalHours,
                    TotalTargetHours: housingTotalTargetHours,
                    TotalHoursDifference: housingTotalHours - housingTotalTargetHours,
                    TotalAcceptedOrders: housingTotalOrders,
                    TotalTargetOrders: housingTotalTargetOrders,
                    TotalOrdersDifference: housingTotalOrders - housingTotalTargetOrders,
                    AverageAttendanceRate: attendanceRates.Any() ? attendanceRates.Average() : 0,
                    AverageHoursCompletionRate: hoursCompletionRates.Any() ? hoursCompletionRates.Average() : 0,
                    AverageOrdersCompletionRate: ordersCompletionRates.Any() ? ordersCompletionRates.Average() : 0,
                    OverallHousingScore: attendanceRates.Any()
                        ? (attendanceRates.Average() + hoursCompletionRates.Average() + ordersCompletionRates.Average()) / 3
                        : 0
                );

                housingDetails.Add(new HousingPerformanceDetail(
                    HousingId: housingGroup.Key.HousingId,
                    HousingName: housingGroup.Key.HousingName,
                    Riders: riderDetails,
                    HousingSummary: housingSummary
                ));

                // Accumulate global metrics
                globalTotalWorkingDays += housingTotalWorkingDays;
                globalTotalAbsentDays += housingTotalAbsentDays;
                globalTotalHours += housingTotalHours;
                globalTotalTargetHours += housingTotalTargetHours;
                globalTotalOrders += housingTotalOrders;
                globalTotalTargetOrders += housingTotalTargetOrders;
            }

            // Sort housings by overall score
            housingDetails = housingDetails
                .OrderByDescending(h => h.HousingSummary.OverallHousingScore)
                .ToList();

            // Calculate report summary
            var totalRiders = housingDetails.Sum(h => h.Riders.Count);
            var companyAttendanceRate = globalTotalTargetHours > 0
                ? (decimal)(globalTotalWorkingDays) / (totalRiders * totalExpectedDays) * 100
                : 0;
            var companyHoursRate = globalTotalTargetHours > 0
                ? (decimal)globalTotalHours / (decimal)globalTotalTargetHours * 100
                : 0;
            var companyOrdersRate = globalTotalTargetOrders > 0
                ? (decimal)globalTotalOrders / globalTotalTargetOrders * 100
                : 0;

            var summary = new ReportSummary(
                TotalHousings: housingDetails.Count,
                TotalRiders: totalRiders,
                TotalWorkingDays: globalTotalWorkingDays,
                TotalAbsentDays: globalTotalAbsentDays,
                GrandTotalHours: globalTotalHours,
                GrandTotalTargetHours: globalTotalTargetHours,
                GrandTotalOrders: globalTotalOrders,
                GrandTotalTargetOrders: globalTotalTargetOrders,
                CompanyWideAttendanceRate: companyAttendanceRate,
                CompanyWideHoursCompletionRate: companyHoursRate,
                CompanyWideOrdersCompletionRate: companyOrdersRate
            );

            var report = new HousingDetailedDailyPerformanceReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                HousingDetails: housingDetails,
                Summary: summary
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDetailedDailyPerformanceReport>(
                new Error($"Error generating detailed performance report: {ex.Message}", "server_error", 500));
        }
    }

    // Helper method to determine performance level
    private string DeterminePerformanceLevel(
        float actualHours,
        int actualOrders,
        float targetHours,
        int targetOrders)
    {
        var hoursPercentage = actualHours / targetHours * 100;
        var ordersPercentage = (decimal)actualOrders / targetOrders * 100;
        var averagePercentage = (decimal)(hoursPercentage + (float)ordersPercentage) / 2;

        return averagePercentage switch
        {
            >= 110m => "Excellent",
            >= 90m => "Good",
            >= 70m => "Average",
            >= 50m => "Below Average",
            _ => "Poor"
        };
    }

    public async Task<Result<List<HousingRiderDailyDetailReport>>> GetAllHousingsRiderDailyDetailReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        // Get all shifts with rider and housing information
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       !string.IsNullOrWhiteSpace(s.Rider.WorkingId))
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingRiderDailyDetailReport>());

        // Group by housing (from shift data)
        var housingGroups = shifts
            .Where(s => s.Rider?.Employee?.Housing != null)
            .GroupBy(s => new
            {
                HousingId = s.Rider.Employee.Housing.Id,
                HousingName = s.Rider.Employee.Housing.Name
            });

        var reports = new List<HousingRiderDailyDetailReport>();

        foreach (var housingGroup in housingGroups)
        {
            var riderGroups = housingGroup.GroupBy(s => s.RiderId);

            foreach (var riderGroup in riderGroups)
            {
                var rider = riderGroup.First().Rider;
                if (rider?.WorkingId == null) continue;

                var reportResult = await GetRiderDailyDetailReportAsync(
                    rider.WorkingId, startDate, endDate, cancellationToken);

                if (reportResult.IsSuccess)
                {
                    reports.Add(new HousingRiderDailyDetailReport(
                        HousingName: housingGroup.Key.HousingName,
                        RiderReport: reportResult.Value
                    ));
                }
            }
        }

        return Result.Success(reports);
    }

    public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

        // Get all shifts for company 1 with housing data from shift
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(e => e.Housing)
            .Where(s => s.CompanyId == 1 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingAllRidersSummaryReport>());

        // Group by housing from shift data
        var housingGroups = shifts.GroupBy(s => new
        {
            HousingId = s.HousingId ?? 0,
            HousingName = s.Housing?.Name ?? "غير محدد"
        });

        var reports = new List<HousingAllRidersSummaryReport>();

        foreach (var housingGroup in housingGroups)
        {
            var housingShifts = housingGroup.ToList();
            var riderGroups = housingShifts.GroupBy(s => s.RiderId);
            var riderSummaries = new List<RiderSummaryDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var actualWorkingDays = riderShifts.Count;
                var missingDays = totalExpectedDays - actualWorkingDays;

                var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
                var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
                var hoursDifference = totalWorkingHours - targetWorkingHours;

                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
                var ordersDifference = totalOrders - targetOrders;

                riderSummaries.Add(new RiderSummaryDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.OrderByDescending(s => s.ShiftDate).First().WorkingId,
                    ActualWorkingDays: actualWorkingDays,
                    MissingDays: missingDays > 0 ? -missingDays : 0,
                    TotalWorkingHours: totalWorkingHours,
                    TargetWorkingHours: targetWorkingHours,
                    HoursDifference: hoursDifference,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    OrdersDifference: ordersDifference
                ));
            }

            riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

            var totals = new SummaryTotals(
                TotalRiders: riderSummaries.Count,
                TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
                TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
                TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
                TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
                HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
                TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
                TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
                OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
            );

            var summaryReport = new AllRidersSummaryReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                RiderSummaries: riderSummaries,
                Totals: totals
            );

            reports.Add(new HousingAllRidersSummaryReport(
                HousingName: housingGroup.Key.HousingName,
                SummaryReport: summaryReport
            ));
        }

        return Result.Success(reports);
    }

    public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync2(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

        // Get all shifts for company 2 with housing data from shift
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.CompanyId == 2 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       s.Rider.Employee.Housing != null)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingAllRidersSummaryReport>());

        // Group by housing from shift data
        var housingGroups = shifts.GroupBy(s => new
        {
            HousingId = s.Rider.Employee.Housing.Id,
            HousingName = s.Rider.Employee.Housing.Name
        });

        var reports = new List<HousingAllRidersSummaryReport>();

        foreach (var housingGroup in housingGroups)
        {
            var housingShifts = housingGroup.ToList();
            var riderGroups = housingShifts.GroupBy(s => s.RiderId);
            var riderSummaries = new List<RiderSummaryDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var actualWorkingDays = riderShifts.Count;

                // Count days with less than 10 working hours
                var daysWithLessThan10Hours = riderShifts.Count(s => s.WorkingHours < 10);
                var missingDays = (totalExpectedDays - actualWorkingDays) + daysWithLessThan10Hours;

                var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
                var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY2;
                var hoursDifference = totalWorkingHours - targetWorkingHours;

                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY2;
                var ordersDifference = totalOrders - targetOrders;

                riderSummaries.Add(new RiderSummaryDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    ActualWorkingDays: actualWorkingDays,
                    MissingDays: missingDays > 0 ? -missingDays : 0,
                    TotalWorkingHours: totalWorkingHours,
                    TargetWorkingHours: targetWorkingHours,
                    HoursDifference: hoursDifference,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    OrdersDifference: ordersDifference
                ));
            }

            riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

            var totals = new SummaryTotals(
                TotalRiders: riderSummaries.Count,
                TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
                TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
                TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
                TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
                HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
                TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
                TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
                OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
            );

            var summaryReport = new AllRidersSummaryReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                RiderSummaries: riderSummaries,
                Totals: totals
            );

            reports.Add(new HousingAllRidersSummaryReport(
                HousingName: housingGroup.Key.HousingName,
                SummaryReport: summaryReport
            ));
        }

        return Result.Success(reports);
    }

    public async Task<Result<List<HousingRejectionReport>>> GetAllHousingsRejectionReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

        // Get all shifts with housing data from shift
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.Rider.CompanyId == 1 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       s.Rider.Employee.Housing != null)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingRejectionReport>());

        // Group by housing from shift data
        var housingGroups = shifts.GroupBy(s => new
        {
            HousingId = s.Rider.Employee.Housing.Id,
            HousingName = s.Rider.Employee.Housing.Name
        });

        var reports = new List<HousingRejectionReport>();

        foreach (var housingGroup in housingGroups)
        {
            var housingShifts = housingGroup.ToList();
            var riderGroups = housingShifts.GroupBy(s => s.RiderId);
            var riderDetails = new List<RiderRejectionDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalShifts = riderShifts.Count;
                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
                var targetOrders = totalDays * TARGET_ORDERS_PER_DAY;
                var totalRejections = riderShifts.Sum(s => s.RejectedDailyOrders);
                var totalRealRejections = riderShifts.Sum(s => s.RealRejectedDailyOrders);

                var rejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRejections / totalOrders * 100, 2)
                    : 0;

                var realRejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRealRejections / totalOrders * 100, 2)
                    : 0;

                riderDetails.Add(new RiderRejectionDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    TotalShifts: totalShifts,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    TotalRejections: totalRejections,
                    TotalRealRejections: totalRealRejections,
                    RejectionRate: rejectionRate,
                    RealRejectionRate: realRejectionRate
                ));
            }

            riderDetails = riderDetails.OrderByDescending(r => r.TotalRealRejections).ToList();

            var totalAllOrders = riderDetails.Sum(r => r.TotalOrders);
            var totalAllRejections = riderDetails.Sum(r => r.TotalRejections);
            var totalAllRealRejections = riderDetails.Sum(r => r.TotalRealRejections);

            var overallRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRejections / totalAllOrders * 100, 2)
                : 0;

            var overallRealRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRealRejections / totalAllOrders * 100, 2)
                : 0;

            var totals = new RejectionTotals(
                TotalRiders: riderDetails.Count,
                TotalShifts: riderDetails.Sum(r => r.TotalShifts),
                TotalOrders: totalAllOrders,
                TotalTargetOrders: riderDetails.Sum(r => r.TargetOrders),
                TotalRejections: totalAllRejections,
                TotalRealRejections: totalAllRealRejections,
                OverallRejectionRate: overallRejectionRate,
                OverallRealRejectionRate: overallRealRejectionRate
            );

            var rejectionReport = new RejectionReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalDays: totalDays,
                RiderDetails: riderDetails,
                Totals: totals
            );

            reports.Add(new HousingRejectionReport(
                HousingName: housingGroup.Key.HousingName,
                RejectionReport: rejectionReport
            ));
        }

        return Result.Success(reports);
    }
    public async Task<Result<List<HousingRejectionReport>>> GetAllHousingsRejectionReportAsync2(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

        // Get all shifts with housing data from shift
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.Rider.CompanyId == 2 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       s.Rider.Employee.Housing != null)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingRejectionReport>());

        // Group by housing from shift data
        var housingGroups = shifts.GroupBy(s => new
        {
            HousingId = s.Rider.Employee.Housing.Id,
            HousingName = s.Rider.Employee.Housing.Name
        });

        var reports = new List<HousingRejectionReport>();

        foreach (var housingGroup in housingGroups)
        {
            var housingShifts = housingGroup.ToList();
            var riderGroups = housingShifts.GroupBy(s => s.RiderId);
            var riderDetails = new List<RiderRejectionDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalShifts = riderShifts.Count;
                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
                var targetOrders = totalDays * 12;
                var totalRejections = riderShifts.Sum(s => s.RejectedDailyOrders);
                var totalRealRejections = riderShifts.Sum(s => s.RealRejectedDailyOrders);

                var rejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRejections / totalOrders * 100, 2)
                    : 0;

                var realRejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRealRejections / totalOrders * 100, 2)
                    : 0;

                riderDetails.Add(new RiderRejectionDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    TotalShifts: totalShifts,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    TotalRejections: totalRejections,
                    TotalRealRejections: totalRealRejections,
                    RejectionRate: rejectionRate,
                    RealRejectionRate: realRejectionRate
                ));
            }

            riderDetails = riderDetails.OrderByDescending(r => r.TotalRealRejections).ToList();

            var totalAllOrders = riderDetails.Sum(r => r.TotalOrders);
            var totalAllRejections = riderDetails.Sum(r => r.TotalRejections);
            var totalAllRealRejections = riderDetails.Sum(r => r.TotalRealRejections);

            var overallRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRejections / totalAllOrders * 100, 2)
                : 0;

            var overallRealRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRealRejections / totalAllOrders * 100, 2)
                : 0;

            var totals = new RejectionTotals(
                TotalRiders: riderDetails.Count,
                TotalShifts: riderDetails.Sum(r => r.TotalShifts),
                TotalOrders: totalAllOrders,
                TotalTargetOrders: riderDetails.Sum(r => r.TargetOrders),
                TotalRejections: totalAllRejections,
                TotalRealRejections: totalAllRealRejections,
                OverallRejectionRate: overallRejectionRate,
                OverallRealRejectionRate: overallRealRejectionRate
            );

            var rejectionReport = new RejectionReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalDays: totalDays,
                RiderDetails: riderDetails,
                Totals: totals
            );

            reports.Add(new HousingRejectionReport(
                HousingName: housingGroup.Key.HousingName,
                RejectionReport: rejectionReport
            ));
        }

        return Result.Success(reports);
    }

    // Note: GetComprehensiveDashboardAsync already uses housing from shift data
    // via the GetHousingStatistics method which correctly filters shifts where
    // s.Rider?.Employee?.Housing != null
    public async Task<Result<RiderMonthlyHistorys>> GetRiderMonthlyHistoryAsync(
     long riderIqamaNo,
     CancellationToken cancellationToken = default)
    {
        try
        {
            // Get rider details
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo, cancellationToken);

            if (rider == null)
            {
                return Result.Failure<RiderMonthlyHistorys>(
                    new Error($"Rider with Iqama number {riderIqamaNo} not found", "not_found", 404));
            }

            // Get all shifts for this rider
            var shifts = await _dbcontext.RiderShifts
                .Where(s => s.RiderId == rider.Id && s.CompanyId == 1)
                .OrderBy(s => s.ShiftDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<RiderMonthlyHistorys>(
                    new Error("No shift history found for this rider", "no_data", 404));
            }

            // Calculate monthly summaries
            var firstShiftDate = shifts.First().ShiftDate;
            var lastShiftDate = shifts.Last().ShiftDate;
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

            // Use the later of last shift date or today
            var endDate = lastShiftDate > today ? lastShiftDate : today;

            var monthlyData = GenerateMonthlyShiftSummaries(shifts, firstShiftDate, endDate);

            // Get active months (months with orders > 0)
            var activeMonths = monthlyData
                .Where(m => m.TotalAcceptedOrders > 0)
                .ToList();

            var activeMonthNumbers = activeMonths
                .Select(m => m.Month)
                .ToList();

            var activeMonthsCount = activeMonths.Count;

            // Calculate average orders per active month
            var avgOrdersPerActiveMonth = activeMonthsCount > 0
                ? (decimal)activeMonths.Sum(m => m.TotalAcceptedOrders) / activeMonthsCount
                : 0;

            var history = new RiderMonthlyHistorys(
                IqamaNo: riderIqamaNo,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? "0",
                FirstShiftDate: firstShiftDate,
                LastShiftDate: lastShiftDate,
                TotalMonths: monthlyData.Count,
                ActiveMonthsCount: activeMonthsCount,
                AverageOrdersPerActiveMonth: avgOrdersPerActiveMonth,
                ActiveMonthNumbers: activeMonthNumbers,
                MonthlyData: monthlyData
            );

            return Result.Success(history);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderMonthlyHistorys>(
                new Error($"Error generating rider monthly history: {ex.Message}", "server_error", 500));
        }
    }
    public async Task<Result<RiderMonthlyHistorys>> GetRiderMonthlyHistoryAsync2(
     long riderIqamaNo,
     CancellationToken cancellationToken = default)
    {
        try
        {
            // Get rider details
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo, cancellationToken);

            if (rider == null)
            {
                return Result.Failure<RiderMonthlyHistorys>(
                    new Error($"Rider with Iqama number {riderIqamaNo} not found", "not_found", 404));
            }

            // Get all shifts for this rider
            var shifts = await _dbcontext.RiderShifts
                .Where(s => s.RiderId == rider.Id && s.CompanyId == 2)
                .OrderBy(s => s.ShiftDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<RiderMonthlyHistorys>(
                    new Error("No shift history found for this rider", "no_data", 404));
            }

            // Calculate monthly summaries
            var firstShiftDate = shifts.First().ShiftDate;
            var lastShiftDate = shifts.Last().ShiftDate;
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

            // Use the later of last shift date or today
            var endDate = lastShiftDate > today ? lastShiftDate : today;

            var monthlyData = GenerateMonthlyShiftSummaries(shifts, firstShiftDate, endDate);

            // Get active months (months with orders > 0)
            var activeMonths = monthlyData
                .Where(m => m.TotalAcceptedOrders > 0)
                .ToList();

            var activeMonthNumbers = activeMonths
                .Select(m => m.Month)
                .ToList();

            var activeMonthsCount = activeMonths.Count;

            // Calculate average orders per active month
            var avgOrdersPerActiveMonth = activeMonthsCount > 0
                ? (decimal)activeMonths.Sum(m => m.TotalAcceptedOrders) / activeMonthsCount
                : 0;

            var history = new RiderMonthlyHistorys(
                IqamaNo: riderIqamaNo,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? "0",
                FirstShiftDate: firstShiftDate,
                LastShiftDate: lastShiftDate,
                TotalMonths: monthlyData.Count,
                ActiveMonthsCount: activeMonthsCount,
                AverageOrdersPerActiveMonth: avgOrdersPerActiveMonth,
                ActiveMonthNumbers: activeMonthNumbers,
                MonthlyData: monthlyData
            );

            return Result.Success(history);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderMonthlyHistorys>(
                new Error($"Error generating rider monthly history: {ex.Message}", "server_error", 500));
        }
    }
    // Helper method for generating monthly summaries
    private List<MonthlyShiftSummary> GenerateMonthlyShiftSummaries(
        List<RiderShift> shifts,
        DateOnly startDate,
        DateOnly endDate)
    {
        var monthlyData = new List<MonthlyShiftSummary>();
        var currentDate = new DateOnly(startDate.Year, startDate.Month, 1);
        var finalDate = new DateOnly(endDate.Year, endDate.Month, 1);

        // Group shifts by year and month
        var shiftsByMonth = shifts
            .GroupBy(s => new { s.ShiftDate.Year, s.ShiftDate.Month })
            .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.ToList());

        // Iterate through each month from start to end
        while (currentDate <= finalDate)
        {
            var year = currentDate.Year;
            var month = currentDate.Month;

            if (shiftsByMonth.TryGetValue((year, month), out var monthShifts))
            {
                var totalShifts = monthShifts.Count;
                var completedShifts = monthShifts.Count(s => s.ShiftStatus == "Completed");
                var incompleteShifts = monthShifts.Count(s => s.ShiftStatus == "Incomplete");
                var failedShifts = monthShifts.Count(s => s.ShiftStatus == "Failed");

                var completionRate = totalShifts > 0
                    ? (decimal)completedShifts / totalShifts * 100
                    : 0;

                monthlyData.Add(new MonthlyShiftSummary(
                    Year: year,
                    Month: month,
                    MonthName: new DateTime(year, month, 1).ToString("MMMM"),
                    TotalShifts: totalShifts,
                    TotalAcceptedOrders: monthShifts.Sum(s => s.AcceptedDailyOrders),
                    TotalRejectedOrders: monthShifts.Sum(s => s.RejectedDailyOrders),
                    TotalRealRejectedOrders: monthShifts.Sum(s => s.RealRejectedDailyOrders),
                    TotalWorkingHours: monthShifts.Sum(s => s.WorkingHours),
                    CompletedShifts: completedShifts,
                    IncompleteShifts: incompleteShifts,
                    FailedShifts: failedShifts,
                    CompletionRate: completionRate
                ));
            }
            else
            {
                // Month with no shifts
                monthlyData.Add(new MonthlyShiftSummary(
                    Year: year,
                    Month: month,
                    MonthName: new DateTime(year, month, 1).ToString("MMMM"),
                    TotalShifts: 0,
                    TotalAcceptedOrders: 0,
                    TotalRejectedOrders: 0,
                    TotalRealRejectedOrders: 0,
                    TotalWorkingHours: 0,
                    CompletedShifts: 0,
                    IncompleteShifts: 0,
                    FailedShifts: 0,
                    CompletionRate: 0
                ));
            }

            currentDate = currentDate.AddMonths(1);
        }

        return monthlyData;
    }

    //public async Task<Result<List<HousingRiderDailyDetailReport>>> GetAllHousingsRiderDailyDetailReportAsync(
    //DateOnly startDate,
    //DateOnly endDate,
    //CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingRiderDailyDetailReport>();

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riders = await _dbcontext.RiderDetails
    //            .Include(r => r.Employee)
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) &&
    //                       !string.IsNullOrWhiteSpace(r.WorkingId))
    //            .ToListAsync(cancellationToken);

    //        foreach (var rider in riders)
    //        {
    //            var reportResult = await GetRiderDailyDetailReportAsync(
    //                rider.WorkingId!, startDate, endDate, cancellationToken);

    //            if (reportResult.IsSuccess)
    //            {
    //                reports.Add(new HousingRiderDailyDetailReport(
    //                    HousingName: housing.Name,
    //                    RiderReport: reportResult.Value
    //                ));
    //            }
    //        }
    //    }

    //    return Result.Success(reports);
    //}

    //public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync(
    //    DateOnly startDate,
    //    DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingAllRidersSummaryReport>();

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riderIds = await _dbcontext.RiderDetails
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
    //            .Select(r => r.Id)
    //            .ToListAsync(cancellationToken);

    //        if (!riderIds.Any()) continue;

    //        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

    //        var shifts = await _dbcontext.RiderShifts
    //            .Include(s => s.Rider)
    //                .ThenInclude(r => r.Employee)
    //            .Where(s => riderIds.Contains(s.RiderId) &&
    //                       s.CompanyId == 1 &&
    //                       s.ShiftDate >= startDate &&
    //                       s.ShiftDate <= endDate)
    //            .ToListAsync(cancellationToken);

    //        if (!shifts.Any()) continue;

    //        var riderGroups = shifts.GroupBy(s => s.RiderId);
    //        var riderSummaries = new List<RiderSummaryDetail>();

    //        foreach (var group in riderGroups)
    //        {
    //            var rider = group.First().Rider;
    //            if (rider?.Employee == null) continue;

    //            var riderShifts = group.ToList();
    //            var actualWorkingDays = riderShifts.Count;
    //            var missingDays = totalExpectedDays - actualWorkingDays;

    //            var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
    //            var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
    //            var hoursDifference = totalWorkingHours - targetWorkingHours;

    //            var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
    //            var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
    //            var ordersDifference = totalOrders - targetOrders;

    //            riderSummaries.Add(new RiderSummaryDetail(
    //                RiderId: rider.Id,
    //                IqamaNo: rider.EmployeeIqamaNo,
    //                RiderNameAR: rider.Employee.NameAR,
    //                RiderNameEN: rider.Employee.NameEN,
    //                WorkingId: riderShifts.First().WorkingId,
    //                ActualWorkingDays: actualWorkingDays,
    //                MissingDays: missingDays > 0 ? -missingDays : 0,
    //                TotalWorkingHours: totalWorkingHours,
    //                TargetWorkingHours: targetWorkingHours,
    //                HoursDifference: hoursDifference,
    //                TotalOrders: totalOrders,
    //                TargetOrders: targetOrders,
    //                OrdersDifference: ordersDifference
    //            ));
    //        }

    //        riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

    //        var totals = new SummaryTotals(
    //            TotalRiders: riderSummaries.Count,
    //            TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
    //            TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
    //            TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
    //            TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
    //            HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
    //            TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
    //            TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
    //            OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
    //        );

    //        var summaryReport = new AllRidersSummaryReport(
    //            StartDate: startDate,
    //            EndDate: endDate,
    //            TotalExpectedDays: totalExpectedDays,
    //            RiderSummaries: riderSummaries,
    //            Totals: totals
    //        );

    //        reports.Add(new HousingAllRidersSummaryReport(
    //            HousingName: housing.Name,
    //            SummaryReport: summaryReport
    //        ));
    //    }

    //    return Result.Success(reports);
    //}
    //public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync2(
    //    DateOnly startDate,
    //    DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingAllRidersSummaryReport>();

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riderIds = await _dbcontext.RiderDetails
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
    //            .Select(r => r.Id)
    //            .ToListAsync(cancellationToken);

    //        if (!riderIds.Any()) continue;

    //        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

    //        var shifts = await _dbcontext.RiderShifts
    //            .Include(s => s.Rider)
    //                .ThenInclude(r => r.Employee)
    //            .Where(s => riderIds.Contains(s.RiderId) &&
    //                       s.CompanyId == 2 &&
    //                       s.ShiftDate >= startDate &&
    //                       s.ShiftDate <= endDate)
    //            .ToListAsync(cancellationToken);

    //        if (!shifts.Any()) continue;

    //        var riderGroups = shifts.GroupBy(s => s.RiderId);
    //        var riderSummaries = new List<RiderSummaryDetail>();

    //        foreach (var group in riderGroups)
    //        {
    //            var rider = group.First().Rider;
    //            if (rider?.Employee == null) continue;

    //            var riderShifts = group.ToList();
    //            var actualWorkingDays = riderShifts.Count;

    //            // Count days with less than 10 working hours
    //            var daysWithLessThan10Hours = riderShifts.Count(s => s.WorkingHours < 10);

    //            // Calculate missing days: days with no shifts + days with less than 10 hours
    //            var missingDays = (totalExpectedDays - actualWorkingDays) + daysWithLessThan10Hours;

    //            var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
    //            var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY2;
    //            var hoursDifference = totalWorkingHours - targetWorkingHours;

    //            var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
    //            var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY2;
    //            var ordersDifference = totalOrders - targetOrders;

    //            riderSummaries.Add(new RiderSummaryDetail(
    //                RiderId: rider.Id,
    //                IqamaNo: rider.EmployeeIqamaNo,
    //                RiderNameAR: rider.Employee.NameAR,
    //                RiderNameEN: rider.Employee.NameEN,
    //                WorkingId: riderShifts.First().WorkingId,
    //                ActualWorkingDays: actualWorkingDays,
    //                MissingDays: missingDays > 0 ? -missingDays : 0,
    //                TotalWorkingHours: totalWorkingHours,
    //                TargetWorkingHours: targetWorkingHours,
    //                HoursDifference: hoursDifference,
    //                TotalOrders: totalOrders,
    //                TargetOrders: targetOrders,
    //                OrdersDifference: ordersDifference
    //            ));
    //        }

    //        riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

    //        var totals = new SummaryTotals(
    //            TotalRiders: riderSummaries.Count,
    //            TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
    //            TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
    //            TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
    //            TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
    //            HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
    //            TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
    //            TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
    //            OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
    //        );

    //        var summaryReport = new AllRidersSummaryReport(
    //            StartDate: startDate,
    //            EndDate: endDate,
    //            TotalExpectedDays: totalExpectedDays,
    //            RiderSummaries: riderSummaries,
    //            Totals: totals
    //        );

    //        reports.Add(new HousingAllRidersSummaryReport(
    //            HousingName: housing.Name,
    //            SummaryReport: summaryReport
    //        ));
    //    }

    //    return Result.Success(reports);
    //}
    //public async Task<Result<List<HousingRejectionReport>>> GetAllHousingsRejectionReportAsync(
    //    DateOnly startDate,
    //    DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingRejectionReport>();
    //    var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riderIds = await _dbcontext.RiderDetails
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) && r.CompanyId == 1)
    //            .Select(r => r.Id)
    //            .ToListAsync(cancellationToken);

    //        if (!riderIds.Any()) continue;

    //        var shifts = await _dbcontext.RiderShifts
    //            .Include(s => s.Rider)
    //                .ThenInclude(r => r.Employee)
    //            .Where(s => riderIds.Contains(s.RiderId) &&
    //                       s.ShiftDate >= startDate &&
    //                       s.ShiftDate <= endDate)
    //            .ToListAsync(cancellationToken);

    //        if (!shifts.Any()) continue;

    //        var riderGroups = shifts.GroupBy(s => s.RiderId);
    //        var riderDetails = new List<RiderRejectionDetail>();

    //        foreach (var group in riderGroups)
    //        {
    //            var rider = group.First().Rider;
    //            if (rider?.Employee == null) continue;

    //            var riderShifts = group.ToList();
    //            var totalShifts = riderShifts.Count;
    //            var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
    //            var targetOrders = totalDays * TARGET_ORDERS_PER_DAY;
    //            var totalRejections = riderShifts.Sum(s => s.RejectedDailyOrders);
    //            var totalRealRejections = riderShifts.Sum(s => s.RealRejectedDailyOrders);

    //            var rejectionRate = totalOrders > 0
    //                ? Math.Round((decimal)totalRejections / totalOrders * 100, 2)
    //                : 0;

    //            var realRejectionRate = totalOrders > 0
    //                ? Math.Round((decimal)totalRealRejections / totalOrders * 100, 2)
    //                : 0;

    //            riderDetails.Add(new RiderRejectionDetail(
    //                RiderId: rider.Id,
    //                IqamaNo: rider.EmployeeIqamaNo,
    //                RiderNameAR: rider.Employee.NameAR,
    //                RiderNameEN: rider.Employee.NameEN,
    //                WorkingId: riderShifts.First().WorkingId,
    //                TotalShifts: totalShifts,
    //                TotalOrders: totalOrders,
    //                TargetOrders: targetOrders,
    //                TotalRejections: totalRejections,
    //                TotalRealRejections: totalRealRejections,
    //                RejectionRate: rejectionRate,
    //                RealRejectionRate: realRejectionRate
    //            ));
    //        }

    //        riderDetails = riderDetails.OrderByDescending(r => r.TotalRealRejections).ToList();

    //        var totalAllOrders = riderDetails.Sum(r => r.TotalOrders);
    //        var totalAllRejections = riderDetails.Sum(r => r.TotalRejections);
    //        var totalAllRealRejections = riderDetails.Sum(r => r.TotalRealRejections);

    //        var overallRejectionRate = totalAllOrders > 0
    //            ? Math.Round((decimal)totalAllRejections / totalAllOrders * 100, 2)
    //            : 0;

    //        var overallRealRejectionRate = totalAllOrders > 0
    //            ? Math.Round((decimal)totalAllRealRejections / totalAllOrders * 100, 2)
    //            : 0;

    //        var totals = new RejectionTotals(
    //            TotalRiders: riderDetails.Count,
    //            TotalShifts: riderDetails.Sum(r => r.TotalShifts),
    //            TotalOrders: totalAllOrders,
    //            TotalTargetOrders: riderDetails.Sum(r => r.TargetOrders),
    //            TotalRejections: totalAllRejections,
    //            TotalRealRejections: totalAllRealRejections,
    //            OverallRejectionRate: overallRejectionRate,
    //            OverallRealRejectionRate: overallRealRejectionRate
    //        );

    //        var rejectionReport = new RejectionReport(
    //            StartDate: startDate,
    //            EndDate: endDate,
    //            TotalDays: totalDays,
    //            RiderDetails: riderDetails,
    //            Totals: totals
    //        );

    //        reports.Add(new HousingRejectionReport(
    //            HousingName: housing.Name,
    //            RejectionReport: rejectionReport
    //        ));
    //    }

    //    return Result.Success(reports);
    //}
    public async Task<Result<RiderDailyDetailReport>> GetRiderDailyDetailReportAsync(
    string workingId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingId))
            return Result.Failure<RiderDailyDetailReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<RiderDailyDetailReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

            if (rider == null)
                return Result.Failure<RiderDailyDetailReport>(
                    new Error($"Rider with working ID {workingId} not found", "not_found", 404));

            var shifts = await _dbcontext.RiderShifts
                .Where(s => s.RiderId == rider.Id &&
                            s.CompanyId == 1 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .OrderBy(s => s.ShiftDate)
                .ToListAsync(cancellationToken);

            var shiftDictionary = shifts.ToDictionary(s => s.ShiftDate, s => s);

            var dailyDetails = new List<DailyShiftDetail>();
            var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
            var currentDate = startDate;

            while (currentDate <= endDate)
            {
                if (shiftDictionary.TryGetValue(currentDate, out var shift))
                {
                    var hoursDiff = shift.WorkingHours - TARGET_HOURS_PER_DAY;
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: true,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        RejectedOrders: shift.RejectedDailyOrders,
                        RealRejectedOrders: shift.RealRejectedDailyOrders,
                        WorkingHours: shift.WorkingHours,
                        TargetHours: TARGET_HOURS_PER_DAY,
                        HoursDifference: hoursDiff,
                        ShiftStatus: shift.ShiftStatus
                    ));
                }
                else
                {
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: false,
                        AcceptedOrders: 0,
                        RejectedOrders: 0,
                        RealRejectedOrders: 0,
                        WorkingHours: 0,
                        TargetHours: TARGET_HOURS_PER_DAY,
                        HoursDifference: -TARGET_HOURS_PER_DAY,
                        ShiftStatus: "Missing"
                    ));
                }
                currentDate = currentDate.AddDays(1);
            }

            var totalWorkingDays = shifts.Count;
            var missingDays = totalDays - totalWorkingDays;
            var totalWorkingHours = shifts.Sum(s => s.WorkingHours);
            var targetWorkingHours = totalDays * TARGET_HOURS_PER_DAY;
            var hoursDifference = totalWorkingHours - targetWorkingHours;
            var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
            var totalRejections = shifts.Sum(s => s.RejectedDailyOrders);
            var totalRealRejections = shifts.Sum(s => s.RealRejectedDailyOrders);

            var report = new RiderDailyDetailReport(
                RiderId: rider.Id,
                IqamaNo: rider.EmployeeIqamaNo,
                RiderNameAR: rider.Employee.NameAR,
                RiderNameEN: rider.Employee.NameEN,
                WorkingId: workingId,
                StartDate: startDate,
                EndDate: endDate,
                DailyDetails: dailyDetails,
                TotalWorkingDays: totalWorkingDays,
                MissingDays: missingDays,
                TotalWorkingHours: totalWorkingHours,
                TargetWorkingHours: targetWorkingHours,
                HoursDifference: hoursDifference,
                IsAboveTarget: hoursDifference >= 0,
                TotalOrders: totalOrders,
                TotalRejections: totalRejections,
                TotalRealRejections: totalRealRejections
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderDailyDetailReport>(
                new Error($"Error generating daily detail report: {ex.Message}", "server_error", 500));
        }
    }
    public async Task<Result<RiderDailyDetailReport>> GetRiderDailyDetailReportAsync2(
    string workingId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingId))
            return Result.Failure<RiderDailyDetailReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<RiderDailyDetailReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

            if (rider == null)
                return Result.Failure<RiderDailyDetailReport>(
                    new Error($"Rider with working ID {workingId} not found", "not_found", 404));

            var shifts = await _dbcontext.RiderShifts
                .Where(s => s.RiderId == rider.Id &&
                            s.CompanyId == 2 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .OrderBy(s => s.ShiftDate)
                .ToListAsync(cancellationToken);

            var shiftDictionary = shifts.ToDictionary(s => s.ShiftDate, s => s);

            var dailyDetails = new List<DailyShiftDetail>();
            var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
            var currentDate = startDate;

            while (currentDate <= endDate)
            {
                if (shiftDictionary.TryGetValue(currentDate, out var shift))
                {
                    var hoursDiff = shift.WorkingHours - TARGET_HOURS_PER_DAY2;
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: true,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        RejectedOrders: shift.RejectedDailyOrders,
                        RealRejectedOrders: shift.RealRejectedDailyOrders,
                        WorkingHours: shift.WorkingHours,
                        TargetHours: TARGET_HOURS_PER_DAY2,
                        HoursDifference: hoursDiff,
                        ShiftStatus: shift.ShiftStatus
                    ));
                }
                else
                {
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: false,
                        AcceptedOrders: 0,
                        RejectedOrders: 0,
                        RealRejectedOrders: 0,
                        WorkingHours: 0,
                        TargetHours: TARGET_HOURS_PER_DAY2,
                        HoursDifference: -TARGET_HOURS_PER_DAY2,
                        ShiftStatus: "Missing"
                    ));
                }
                currentDate = currentDate.AddDays(1);
            }

            var totalWorkingDays = shifts.Count;
            var missingDays = totalDays - totalWorkingDays;
            var totalWorkingHours = shifts.Sum(s => s.WorkingHours);
            var targetWorkingHours = totalDays * TARGET_HOURS_PER_DAY2;
            var hoursDifference = totalWorkingHours - targetWorkingHours;
            var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
            var totalRejections = shifts.Sum(s => s.RejectedDailyOrders);
            var totalRealRejections = shifts.Sum(s => s.RealRejectedDailyOrders);

            var report = new RiderDailyDetailReport(
                RiderId: rider.Id,
                IqamaNo: rider.EmployeeIqamaNo,
                RiderNameAR: rider.Employee.NameAR,
                RiderNameEN: rider.Employee.NameEN,
                WorkingId: workingId,
                StartDate: startDate,
                EndDate: endDate,
                DailyDetails: dailyDetails,
                TotalWorkingDays: totalWorkingDays,
                MissingDays: missingDays,
                TotalWorkingHours: totalWorkingHours,
                TargetWorkingHours: targetWorkingHours,
                HoursDifference: hoursDifference,
                IsAboveTarget: hoursDifference >= 0,
                TotalOrders: totalOrders,
                TotalRejections: totalRejections,
                TotalRealRejections: totalRealRejections
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderDailyDetailReport>(
                new Error($"Error generating daily detail report: {ex.Message}", "server_error", 500));
        }
    }

    // ============================================
    // 2. ALL RIDERS SUMMARY REPORT
    // ============================================

    public async Task<Result<AllRidersSummaryReport>> GetAllRidersSummaryReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<AllRidersSummaryReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Success(new AllRidersSummaryReport(
                    StartDate: startDate,
                    EndDate: endDate,
                    TotalExpectedDays: totalExpectedDays,
                    RiderSummaries: new List<RiderSummaryDetail>(),
                    Totals: new SummaryTotals(0, 0, 0, 0, 0, 0, 0, 0, 0)
                ));
            }

            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var riderSummaries = new List<RiderSummaryDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var actualWorkingDays = riderShifts.Count;
                var missingDays = totalExpectedDays - actualWorkingDays;

                var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
                var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
                var hoursDifference = totalWorkingHours - targetWorkingHours;

                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
                var ordersDifference = totalOrders - targetOrders;

                riderSummaries.Add(new RiderSummaryDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    ActualWorkingDays: actualWorkingDays,
                    MissingDays: missingDays > 0 ? -missingDays : 0, // Negative if missing, 0 otherwise
                    TotalWorkingHours: totalWorkingHours,
                    TargetWorkingHours: targetWorkingHours,
                    HoursDifference: hoursDifference,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    OrdersDifference: ordersDifference
                ));
            }

            riderSummaries = riderSummaries
                .OrderByDescending(r => r.TotalOrders)
                .ToList();

            var totals = new SummaryTotals(
                TotalRiders: riderSummaries.Count,
                TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
                TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
                TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
                TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
                HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
                TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
                TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
                OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
            );

            var report = new AllRidersSummaryReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                RiderSummaries: riderSummaries,
                Totals: totals
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<AllRidersSummaryReport>(
                new Error($"Error generating summary report: {ex.Message}", "server_error", 500));
        }
    }

    // ============================================
    // 3. REJECTION REPORT
    // ============================================

    public async Task<Result<RejectionReport>> GetRejectionReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<RejectionReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Success(new RejectionReport(
                    StartDate: startDate,
                    EndDate: endDate,
                    TotalDays: totalDays,
                    RiderDetails: new List<RiderRejectionDetail>(),
                    Totals: new RejectionTotals(0, 0, 0, 0, 0, 0, 0, 0)
                ));
            }

            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var riderDetails = new List<RiderRejectionDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalShifts = riderShifts.Count;
                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
                var targetOrders = totalDays * TARGET_ORDERS_PER_DAY;
                var totalRejections = riderShifts.Sum(s => s.RejectedDailyOrders);
                var totalRealRejections = riderShifts.Sum(s => s.RealRejectedDailyOrders);

                var rejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRejections / totalOrders * 100, 2)
                    : 0;

                var realRejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRealRejections / totalOrders * 100, 2)
                    : 0;

                riderDetails.Add(new RiderRejectionDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    TotalShifts: totalShifts,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    TotalRejections: totalRejections,
                    TotalRealRejections: totalRealRejections,
                    RejectionRate: rejectionRate,
                    RealRejectionRate: realRejectionRate
                ));
            }

            riderDetails = riderDetails
                .OrderByDescending(r => r.TotalRealRejections)
                .ToList();

            var totalAllOrders = riderDetails.Sum(r => r.TotalOrders);
            var totalAllRejections = riderDetails.Sum(r => r.TotalRejections);
            var totalAllRealRejections = riderDetails.Sum(r => r.TotalRealRejections);

            var overallRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRejections / totalAllOrders * 100, 2)
                : 0;

            var overallRealRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRealRejections / totalAllOrders * 100, 2)
                : 0;

            var totals = new RejectionTotals(
                TotalRiders: riderDetails.Count,
                TotalShifts: riderDetails.Sum(r => r.TotalShifts),
                TotalOrders: totalAllOrders,
                TotalTargetOrders: riderDetails.Sum(r => r.TargetOrders),
                TotalRejections: totalAllRejections,
                TotalRealRejections: totalAllRealRejections,
                OverallRejectionRate: overallRejectionRate,
                OverallRealRejectionRate: overallRealRejectionRate
            );

            var report = new RejectionReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalDays: totalDays,
                RiderDetails: riderDetails,
                Totals: totals
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RejectionReport>(
                new Error($"Error generating rejection report: {ex.Message}", "server_error", 500));
        }
    }
    public async Task<Result<PeriodOrdersComparison>> ComparePeriodOrdersAsync(
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        // Validate period 2 dates
        if (period2End < period2Start)
            return Result.Failure<PeriodOrdersComparison>(
                new Error("Period 2: End date must be after or equal to start date", "invalid_input", 400));

        // Automatically calculate Period 1 (previous month of Period 2)
        var period1Start = period2Start.AddMonths(-1);
        var period1End = period2End.AddMonths(-1);

        try
        {
            // Get shifts for period 1
            var period1Shifts = await _dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= period1Start && s.ShiftDate <= period1End && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Get shifts for period 2
            var period2Shifts = await _dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= period2Start && s.ShiftDate <= period2End && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Calculate total orders for each period
            var period1TotalOrders = period1Shifts.Sum(s => s.AcceptedDailyOrders);
            var period2TotalOrders = period2Shifts.Sum(s => s.AcceptedDailyOrders);

            // Calculate difference and percentage
            var ordersDifference = period2TotalOrders - period1TotalOrders;
            var changePercentage = period1TotalOrders > 0
                ? Math.Round(((decimal)ordersDifference / period1TotalOrders) * 100, 2)
                : (period2TotalOrders > 0 ? 100m : 0m);

            // Generate trend description
            var trendDescription = GenerateTrendDescription(
                ordersDifference, changePercentage, period1TotalOrders, period2TotalOrders);

            var comparison = new PeriodOrdersComparison(
                Period1Start: period1Start,
                Period1End: period1End,
                Period2Start: period2Start,
                Period2End: period2End,
                Period1TotalOrders: period1TotalOrders,
                Period2TotalOrders: period2TotalOrders,
                OrdersDifference: ordersDifference,
                ChangePercentage: changePercentage,
                TrendDescription: trendDescription
            );

            return Result.Success(comparison);
        }
        catch (Exception ex)
        {
            return Result.Failure<PeriodOrdersComparison>(
                new Error($"Error comparing periods: {ex.Message}", "server_error", 500));
        }
    }

    /// <summary>
    /// Get daily summary report grouped by housing
    /// </summary>
    public async Task<Result<HousingDailySummaryReport>> GetHousingDailySummaryAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts for the specified date with housing information
            var shifts = await _dbcontext.RiderShifts
                .Include(m => m.Housing)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == reportDate && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDailySummaryReport>(
                    new Error($"No shifts found for date {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Filter shifts with valid housing
            var validShifts = shifts
                .Where(s => s.Rider?.Employee?.Housing != null)
                .ToList();

            if (!validShifts.Any())
            {
                return Result.Failure<HousingDailySummaryReport>(
                    new Error($"No shifts with housing information found for {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Calculate totals
            var totalOrders = validShifts.Sum(s => s.AcceptedDailyOrders);
            var totalRiders = validShifts.Select(s => s.RiderId).Distinct().Count();

            // Group by housing
            var housingGroups = validShifts
                .GroupBy(s => new
                {
                    HousingId = s.HousingId,
                    HousingName = s.Housing?.Name
                });

            var housingSummaries = new List<HousingDailySummary>();

            foreach (var group in housingGroups)
            {
                var housingShifts = group.ToList();
                var housingOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var activeRiders = housingShifts.Select(s => s.RiderId).Distinct().Count();
                var avgOrdersPerRider = activeRiders > 0
                    ? Math.Round((decimal)housingOrders / activeRiders, 2)
                    : 0;
                var percentageOfTotal = totalOrders > 0
                    ? Math.Round((decimal)housingOrders / totalOrders * 100, 2)
                    : 0;

                housingSummaries.Add(new HousingDailySummary(
                    HousingId: group.Key.HousingId ?? 1,
                    HousingName: group.Key.HousingName!,
                    TotalOrders: housingOrders,
                    ActiveRiders: activeRiders,
                    AverageOrdersPerRider: avgOrdersPerRider,
                    PercentageOfTotalOrders: percentageOfTotal
                ));
            }

            // Sort by total orders descending
            housingSummaries = housingSummaries
                .OrderByDescending(h => h.TotalOrders)
                .ToList();

            var avgOrdersPerRiderOverall = totalRiders > 0
                ? Math.Round((decimal)totalOrders / totalRiders, 2)
                : 0;

            var report = new HousingDailySummaryReport(
                ReportDate: reportDate,
                HousingSummaries: housingSummaries,
                TotalOrders: totalOrders,
                TotalRiders: totalRiders,
                AverageOrdersPerRider: avgOrdersPerRiderOverall
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDailySummaryReport>(
                new Error($"Error generating housing daily summary: {ex.Message}", "server_error", 500));
        }
    }

    /// <summary>
    /// Get detailed daily report with individual riders grouped by housing
    /// </summary>
    public async Task<Result<HousingDailyDetailedReport>> GetHousingDailyDetailedReportAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts for the specified date with full details
            var shifts = await _dbcontext.RiderShifts
                .Include(m => m.Housing)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == reportDate && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDailyDetailedReport>(
                    new Error($"No shifts found for date {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Filter shifts with valid housing
            var validShifts = shifts
                .Where(s => s.Rider?.Employee?.Housing != null)
                .ToList();

            if (!validShifts.Any())
            {
                return Result.Failure<HousingDailyDetailedReport>(
                    new Error($"No shifts with housing information found for {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Calculate grand totals
            var grandTotalOrders = validShifts.Sum(s => s.AcceptedDailyOrders);
            var grandTotalRiders = validShifts.Select(s => s.RiderId).Distinct().Count();

            // Group by housing
            var housingGroups = validShifts
                .GroupBy(s => new
                {
                    HousingId = s.Rider.Employee.Housing.Id,
                    HousingName = s.Rider.Employee.Housing.Name
                });

            var housingDetails = new List<HousingDailyDetails>();

            foreach (var group in housingGroups)
            {
                var housingShifts = group.ToList();
                var housingTotalOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var housingRiderCount = housingShifts.Select(s => s.RiderId).Distinct().Count();
                var percentageOfCompany = grandTotalOrders > 0
                    ? Math.Round((decimal)housingTotalOrders / grandTotalOrders * 100, 2)
                    : 0;

                // Get individual rider performances
                var riderPerformances = housingShifts
                    .Select(s => new RiderDailyPerformance(
                        RiderId: s.RiderId,
                        RiderName: s.Rider?.Employee.NameAR ?? "Unknown",
                        RiderNameE: s.Rider?.Employee.NameEN ?? "Unknown",
                        s.Rider?.Employee.Phone ?? "050",
                        WorkingId: s.WorkingId ?? "0",
                        AcceptedOrders: s.AcceptedDailyOrders,
                        ShiftDate: s.ShiftDate
                    ))
                    .OrderByDescending(r => r.AcceptedOrders)
                    .ToList();

                housingDetails.Add(new HousingDailyDetails(
                    HousingId: group.Key.HousingId,
                    HousingName: group.Key.HousingName,
                    Riders: riderPerformances,
                    HousingTotalOrders: housingTotalOrders,
                    HousingRiderCount: housingRiderCount,
                    PercentageOfCompanyTotal: percentageOfCompany
                ));
            }

            // Sort by total orders descending
            housingDetails = housingDetails
                .OrderByDescending(h => h.HousingTotalOrders)
                .ToList();

            var report = new HousingDailyDetailedReport(
                ReportDate: reportDate,
                HousingDetails: housingDetails,
                GrandTotalOrders: grandTotalOrders,
                GrandTotalRiders: grandTotalRiders
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDailyDetailedReport>(
                new Error($"Error generating housing daily detailed report: {ex.Message}", "server_error", 500));
        }
    }

    // Helper method for trend description
    private string GenerateTrendDescription(
        int difference,
        decimal changePercentage,
        int period1Total,
        int period2Total)
    {
        if (difference == 0)
            return "📊 Orders remained stable between periods";

        if (difference > 0)
        {
            if (changePercentage >= 50)
                return $"🚀 Significant increase of {difference:N0} orders (+{changePercentage:F1}%) - Excellent growth!";
            else if (changePercentage >= 20)
                return $"📈 Strong increase of {difference:N0} orders (+{changePercentage:F1}%) - Good performance!";
            else if (changePercentage >= 10)
                return $"✅ Moderate increase of {difference:N0} orders (+{changePercentage:F1}%)";
            else
                return $"↗️ Slight increase of {difference:N0} orders (+{changePercentage:F1}%)";
        }
        else
        {
            var absChange = Math.Abs(changePercentage);
            if (absChange >= 50)
                return $"📉 Significant decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%) - Needs urgent attention!";
            else if (absChange >= 20)
                return $"⚠️ Notable decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%) - Review required";
            else if (absChange >= 10)
                return $"↘️ Moderate decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%)";
            else
                return $"➡️ Slight decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%)";
        }
    }
    public async Task<Result<ComprehensiveDashboard>> GetComprehensiveDashboardAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveEndDate = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var effectiveStartDate = startDate ?? effectiveEndDate.AddDays(-30);

            if (effectiveEndDate < effectiveStartDate)
                return Result.Failure<ComprehensiveDashboard>(
                    new Error("End date must be after start date", "invalid_input", 400));

            var allCompanies = await _dbcontext.Companies
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var allRiders = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Where(d => !d.Employee.IsDeleted)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
                .Where(s => s.ShiftDate >= effectiveStartDate && s.ShiftDate <= effectiveEndDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var allHousings = await _dbcontext.Housings
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var substitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Where(s => s.IsActive)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var allVehicles = await _dbcontext.Vehicles
            .Include(v => v.RiderDetails)
            .Include(v => v.RiderVehicleStatuses)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

            // Now process everything in memory - no more DB calls
            var companies = GetCompaniesStatistics(allCompanies, shifts);
            var riders = GetRidersStatistics(allRiders, shifts, substitutions);
            var shiftsStats = GetShiftsStatistics(shifts, effectiveStartDate, effectiveEndDate);
            var orders = GetOrdersStatistics(shifts);
            var performance = GetPerformanceMetrics(shifts);
            var housing = GetHousingStatistics(allHousings, shifts);
            var trends = GetTrendsAnalysis(shifts, effectiveStartDate, effectiveEndDate);

            var vehicles = GetVehicleStatistics(allVehicles, allRiders, effectiveStartDate, effectiveEndDate);


            var dashboard = new ComprehensiveDashboard(
                GeneratedAt: DateTime.UtcNow.AddHours(3),
                PeriodStart: effectiveStartDate,
                PeriodEnd: effectiveEndDate,
                Companies: companies,
                Riders: riders,
                Shifts: shiftsStats,
                Orders: orders,
                Performance: performance,
                Housing: housing,
                Trends: trends,
                Vehicle: vehicles

            );

            return Result.Success(dashboard);
        }
        catch (Exception ex)
        {
            return Result.Failure<ComprehensiveDashboard>(
                new Error($"Error generating dashboard: {ex.Message}", "server_error", 500));
        }
    }
    private VehicleStatistics GetVehicleStatistics(
    List<Vehicle> vehicles,
    List<RiderDetails> riders,
    DateOnly startDate,
    DateOnly endDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        // Total vehicles count
        var totalVehicles = vehicles.Count;

        // Vehicles by type
        var byType = vehicles
            .GroupBy(v => v.VehicleType)
            .Select(g => new VehicleTypeCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // Vehicles by manufacturer
        var byManufacturer = vehicles
            .Where(v => !string.IsNullOrEmpty(v.Manufacturer))
            .GroupBy(v => v.Manufacturer)
            .Select(g => new ManufacturerCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // License expiry analysis
        var expiredLicenses = vehicles.Count(v => v.LicenseExpiryDate < today);
        var expiringIn30Days = vehicles.Count(v =>
            v.LicenseExpiryDate >= today &&
            v.LicenseExpiryDate <= today.AddDays(30));
        var expiringIn90Days = vehicles.Count(v =>
            v.LicenseExpiryDate > today.AddDays(30) &&
            v.LicenseExpiryDate <= today.AddDays(90));

        // Assigned vs unassigned vehicles
        var assignedVehicles = vehicles.Count(v => v.RiderDetails != null);
        var unassignedVehicles = totalVehicles - assignedVehicles;

        // Average vehicle age
        var currentYear = DateTime.Now.Year;
        var averageAge = vehicles.Any()
            ? vehicles.Average(v => currentYear - v.ManufactureYear)
            : 0;

        // Vehicles by location
        var byLocation = vehicles
            .Where(v => !string.IsNullOrEmpty(v.Location))
            .GroupBy(v => v.Location)
            .Select(g => new LocationCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // Vehicles with complete documentation
        var withCompleteDocumentation = vehicles.Count(v =>
            !string.IsNullOrEmpty(v.VehicleImagePath) &&
            !string.IsNullOrEmpty(v.LicenseImagePath));

        // Recent registrations (within the selected period)
        var recentRegistrations = vehicles.Count(v =>
            DateOnly.FromDateTime(v.CreatedAt) >= startDate &&
            DateOnly.FromDateTime(v.CreatedAt) <= endDate);

        return new VehicleStatistics(
            TotalVehicles: totalVehicles,
            AssignedVehicles: assignedVehicles,
            UnassignedVehicles: unassignedVehicles,
            ExpiredLicenses: expiredLicenses,
            ExpiringIn30Days: expiringIn30Days,
            ExpiringIn90Days: expiringIn90Days,
            AverageVehicleAge: Math.Round(averageAge, 1),
            WithCompleteDocumentation: withCompleteDocumentation,
            RecentRegistrations: recentRegistrations,
            ByType: byType,
            ByManufacturer: byManufacturer,
            ByLocation: byLocation
        );
    }

    public record VehicleStatistics(
        int TotalVehicles,
        int AssignedVehicles,
        int UnassignedVehicles,
        int ExpiredLicenses,
        int ExpiringIn30Days,
        int ExpiringIn90Days,
        double AverageVehicleAge,
        int WithCompleteDocumentation,
        int RecentRegistrations,
        List<VehicleTypeCount> ByType,
        List<ManufacturerCount> ByManufacturer,
        List<LocationCount> ByLocation
    );

    public record VehicleTypeCount(string Type, int Count);
    public record ManufacturerCount(string Manufacturer, int Count);
    public record LocationCount(string Location, int Count);
    private CompaniesStatistics GetCompaniesStatistics(
        List<Company> allCompanies,
        List<RiderShift> shifts)
    {
        var companyDetails = allCompanies.Select(company =>
        {
            var companyShifts = shifts.Where(s => s.Company.Id == company.Id).ToList();
            var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(company.Name);
            var totalShifts = companyShifts.Count;
            var expectedOrders = totalShifts * dailyTarget;
            var acceptedOrders = companyShifts.Sum(s => s.AcceptedDailyOrders);

            var performanceScore = expectedOrders > 0
                ? (decimal)acceptedOrders / expectedOrders * 100
                : 0;

            return new CompanyDetail(
                CompanyId: company.Id,
                CompanyName: company.Name,
                DailyOrderTarget: dailyTarget,
                TotalShifts: totalShifts,
                ActiveRiders: companyShifts.Select(s => s.RiderId).Distinct().Count(),
                TotalAcceptedOrders: acceptedOrders,
                TotalRejectedOrders: companyShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                PerformanceScore: performanceScore,
                TotalWorkingHours: companyShifts.Sum(s => s.WorkingHours)
            );
        })
            .Where(c=>c.TotalShifts > 0) // Only include companies with shifts in the period
            .OrderByDescending(c => c.PerformanceScore).ToList();

        var topPerformer = companyDetails.FirstOrDefault();
        var lowestPerformer = companyDetails.LastOrDefault();

        return new CompaniesStatistics(
            TotalCompanies: allCompanies.Count,
            ActiveCompanies: companyDetails.Count(c => c.TotalShifts > 0),
            CompanyDetails: companyDetails,
            TopPerformingCompany: topPerformer != null ? topPerformer.CompanyName : null,
            LowestPerformingCompany: lowestPerformer != null ? lowestPerformer.CompanyName : null,
            AverageCompanyPerformance: companyDetails.Any() ? companyDetails.Average(c => c.PerformanceScore) : 0
        );
    }

    private RidersStatistics GetRidersStatistics(
        List<RiderDetails> allRiders,
        List<RiderShift> shifts,
        List<RiderShiftSubstitution> substitutions)
    {
        var activeRiderIds = shifts.Select(s => s.RiderId).Distinct().ToList();
        var activeRiders = dbcontext
                   .Employees
                   .AsNoTracking()
                   .Where(r => r.RiderDetails != null && r.Status.ToLower() == "enable")
                   .Include(e => e.Housing)
                   .Include(e => e.RiderDetails)
                   .ToList();


        return new RidersStatistics(
            TotalRiders: allRiders.Count,
            ActiveRiders: activeRiders.Count,
            InactiveRiders: allRiders.Count - activeRiders.Count,
            RidersWithWorkingId: allRiders.Count(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0"),
            RidersWithSubstitution: substitutions.Count,
            AverageShiftsPerRider: activeRiders.Any() ? (decimal)shifts.Count / activeRiders.Count : 0,
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours)
        );
    }

    private ShiftsStatistics GetShiftsStatistics(
        List<RiderShift> shifts,
        DateOnly startDate,
        DateOnly endDate)
    {
        var totalShifts = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());

        var dailyBreakdown = shifts
            .GroupBy(s => s.ShiftDate)
            .Select(g => new DailyShiftBreakdown(
                Date: g.Key,
                TotalShifts: g.Count(),
                CompletedShifts: g.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                TotalOrders: g.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                AcceptedOrders: g.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: g.Sum(s => s.RejectedDailyOrders),
                StackedDeliveries: g.Sum(s => s.StackedDeliveries) // ADD THIS

            ))
            .OrderBy(d => d.Date)
            .ToList();

        return new ShiftsStatistics(
            TotalShifts: totalShifts,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            CompletionRate: totalShifts > 0 ? (decimal)completedShifts / totalShifts * 100 : 0,
            AverageWorkingHoursPerShift: totalShifts > 0 ? shifts.Sum(s => s.WorkingHours) / totalShifts : 0,
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours),
            DailyBreakdown: dailyBreakdown
        );
    }

    private OrdersStatistics GetOrdersStatistics(List<RiderShift> shifts)
    {
        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejected = shifts.Sum(s => s.RejectedDailyOrders);
        var totalStacked = shifts.Sum(s => s.StackedDeliveries); // ADD THIS
        var totalRealRejected = shifts.Sum(s => s.RealRejectedDailyOrders);
        var totalOrders = totalAccepted + totalRejected;

        var acceptanceRate = totalOrders > 0 ? (decimal)totalAccepted / totalOrders * 100 : 0;
        var rejectionRate = totalOrders > 0 ? (decimal)totalRejected / totalOrders * 100 : 0;
        var stackedRate = totalAccepted > 0 ? (decimal)totalStacked / totalAccepted * 100 : 0; // ADD THIS


        var avgOrdersPerShift = shifts.Count > 0 ? (decimal)totalAccepted / shifts.Count : 0;
        var avgStackedPerShift = shifts.Count > 0 ? (decimal)totalStacked / shifts.Count : 0; // ADD THIS

        var problematicShifts = shifts.Count(s =>
            s.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold);

        return new OrdersStatistics(
            TotalOrders: totalOrders,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: totalRejected,
            TotalRealRejectedOrders: totalRealRejected,
            AcceptanceRate: acceptanceRate,
            RejectionRate: rejectionRate,
            AverageOrdersPerShift: avgOrdersPerShift,
            ProblematicShiftsCount: problematicShifts,
            TotalPenaltyAmount: shifts.Sum(s => CalculatePenalty(s)),
                    TotalStackedDeliveries: totalStacked, // ADD THIS
                    StackedDeliveryRate: stackedRate, // ADD THIS
        AverageStackedPerShift: avgStackedPerShift // ADD THIS

        );
    }

    private PerformanceMetrics GetPerformanceMetrics(List<RiderShift> shifts)
    {
        // Calculate overall performance score
        var companyGroups = shifts.GroupBy(s => s.Company.Name);
        var companyScores = new List<decimal>();

        foreach (var group in companyGroups)
        {
            var companyShifts = group.ToList();
            var target = CompanyShiftConfiguration.GetDailyOrderTarget(group.Key);
            var expected = companyShifts.Count * target;
            var actual = companyShifts.Sum(s => s.AcceptedDailyOrders);

            if (expected > 0)
            {
                companyScores.Add((decimal)actual / expected * 100);
            }
        }

        var overallScore = companyScores.Any() ? companyScores.Average() : 0;

        // Top performers
        var riderPerformances = shifts
            .GroupBy(s => s.RiderId)
            .Select(g =>
            {
                var riderShifts = g.ToList();
                var rider = riderShifts.First().Rider;
                var companyName = riderShifts.First().Company?.Name ?? "Unknown";
                var target = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
                var expected = riderShifts.Count * target;
                var actual = riderShifts.Sum(s => s.AcceptedDailyOrders);

                return new TopPerformer(
                    RiderId: g.Key,
                    RiderName: rider?.Employee.NameAR ?? "Unknown",
                    WorkingId: riderShifts.First().WorkingId,
                    TotalOrders: actual,
                    PerformanceScore: expected > 0 ? (decimal)actual / expected * 100 : 0,
                    CompletionRate: CalculateCompletionRate(riderShifts)
                );
            })
            .OrderByDescending(p => p.PerformanceScore)
            .Take(10)
            .ToList();

        var totalDays = shifts.Select(s => s.ShiftDate).Distinct().Count();
        var avgOrdersPerDay = totalDays > 0 ? (decimal)shifts.Sum(s => s.AcceptedDailyOrders) / totalDays : 0;

        return new PerformanceMetrics(
            OverallPerformanceScore: overallScore,
            TopPerformers: riderPerformances,
            AverageCompletionRate: shifts.Any()
                ? (decimal)shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()) / shifts.Count * 100
                : 0,
            AverageOrdersPerDay: avgOrdersPerDay
        );
    }

    private HousingStatistics GetHousingStatistics(
        List<Housing> allHousings,
        List<RiderShift> shifts)
    {
        var validShifts = shifts
            .Where(s => s.Rider?.Employee?.Housing != null)
            .ToList();

        var housingGroups = validShifts.GroupBy(s => s.Rider.Employee.HousingId);

        var housingDetails = housingGroups.Select(g =>
        {
            var housing = g.First().Rider.Employee.Housing;
            var housingShifts = g.ToList();
            var totalOrders = housingShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
            var accepted = housingShifts.Sum(s => s.AcceptedDailyOrders);

            return new HousingDetail(
                HousingId: housing.Id,
                HousingName: housing.Name,
                TotalRiders: housingShifts.Select(s => s.RiderId).Distinct().Count(),
                TotalShifts: housingShifts.Count,
                TotalOrders: totalOrders,
                AcceptedOrders: accepted,
                CompletionRate: totalOrders > 0 ? (decimal)accepted / totalOrders * 100 : 0
            );
        }).OrderByDescending(h => h.CompletionRate).ToList();

        return new HousingStatistics(
            TotalHousings: allHousings.Count,
            ActiveHousings: housingDetails.Count,
            HousingDetails: housingDetails,
            TopPerformingHousing: housingDetails.FirstOrDefault()?.HousingName,
            AverageRidersPerHousing: housingDetails.Any() ? housingDetails.Average(h => h.TotalRiders) : 0
        );
    }

    private TrendsAnalysis GetTrendsAnalysis(
        List<RiderShift> shifts,
        DateOnly startDate,
        DateOnly endDate)
    {
        // Weekly trends
        var weeklyData = shifts
            .GroupBy(s => GetWeekNumber(s.ShiftDate))
            .Select(g => new WeeklyTrend(
                WeekNumber: g.Key,
                TotalShifts: g.Count(),
                TotalOrders: g.Sum(s => s.AcceptedDailyOrders),
                AveragePerformance: CalculateWeeklyPerformance(g.ToList())
            ))
            .OrderBy(w => w.WeekNumber)
            .ToList();

        // Growth metrics
        var firstWeek = weeklyData.FirstOrDefault();
        var lastWeek = weeklyData.LastOrDefault();

        var ordersGrowth = firstWeek != null && lastWeek != null && firstWeek.TotalOrders > 0
            ? ((decimal)(lastWeek.TotalOrders - firstWeek.TotalOrders) / firstWeek.TotalOrders) * 100
            : 0;

        var shiftsGrowth = firstWeek != null && lastWeek != null && firstWeek.TotalShifts > 0
            ? ((decimal)(lastWeek.TotalShifts - firstWeek.TotalShifts) / firstWeek.TotalShifts) * 100
            : 0;

        return new TrendsAnalysis(
            WeeklyTrends: weeklyData,
            OrdersGrowthRate: ordersGrowth,
            ShiftsGrowthRate: shiftsGrowth,
            PerformanceTrend: CalculatePerformanceTrend(weeklyData)
        );
    }

    //// Helper methods
    private decimal CalculatePenalty(RiderShift shift)
    {
        var excessRejections = Math.Max(0,
            shift.RealRejectedDailyOrders - CompanyShiftConfiguration.RejectionThreshold);
        return excessRejections * CompanyShiftConfiguration.PenaltyPerExcessRejection;
    }

    private decimal CalculateCompletionRate(List<RiderShift> shifts)
    {
        var total = shifts.Count;
        var completed = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        return total > 0 ? (decimal)completed / total * 100 : 0;
    }

    private int GetWeekNumber(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        var day = (int)dateTime.DayOfWeek;
        return (dateTime.DayOfYear - day + 10) / 7;
    }

    private decimal CalculateWeeklyPerformance(List<RiderShift> shifts)
    {
        if (!shifts.Any()) return 0;

        var companyGroups = shifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var scores = new List<decimal>();

        foreach (var group in companyGroups)
        {
            var target = CompanyShiftConfiguration.GetDailyOrderTarget(group.Key);
            var expected = group.Count() * target;
            var actual = group.Sum(s => s.AcceptedDailyOrders);

            if (expected > 0)
            {
                scores.Add((decimal)actual / expected * 100);
            }
        }

        return scores.Any() ? scores.Average() : 0;
    }

    private string CalculatePerformanceTrend(List<WeeklyTrend> weeklyData)
    {
        if (weeklyData.Count < 2) return "Stable";

        var firstHalf = weeklyData.Take(weeklyData.Count / 2).Average(w => w.AveragePerformance);
        var secondHalf = weeklyData.Skip(weeklyData.Count / 2).Average(w => w.AveragePerformance);

        var difference = secondHalf - firstHalf;

        if (difference > 5) return "Improving";
        if (difference < -5) return "Declining";
        return "Stable";
    }




    public async Task<Result<PreviousDayCompanySummary>> GetPreviousDayCompanySummaryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get yesterday's date and current month range
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var yesterday = today.AddDays(-1);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            // Get all shifts for yesterday
            var yesterdayShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate == yesterday)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Get all shifts for the current month up to today
            var monthShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate >= monthStart && s.ShiftDate <= yesterday)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!yesterdayShifts.Any() && !monthShifts.Any())
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No shifts found for {yesterday:yyyy-MM-dd} or current month", "no_data", 404));
            }

            // ===== YESTERDAY'S DATA =====

            // Filter shifts for Hunger company (yesterday)
            var hungerYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (yesterday)
            var ketaYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate Hunger summary (yesterday)
            var hungerDaySummary = new CompanyDaySummary(
                CompanyName: "Hunger",
                TotalOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerYesterdayShifts.Count,
                AcceptedOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // Calculate Keta summary (yesterday)
            var ketaDaySummary = new CompanyDaySummary(
                CompanyName: "Keta",
                TotalOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaYesterdayShifts.Count,
                AcceptedOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // ===== MONTH-TO-DATE DATA =====

            // Filter shifts for Hunger company (month-to-date)
            var hungerMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (month-to-date)
            var ketaMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate number of days with data in the month
            var daysInMonth = monthShifts
                .Select(s => s.ShiftDate)
                .Distinct()
                .Count();

            // Calculate Hunger month-to-date summary
            var hungerMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Hunger",
                TotalOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerMonthShifts.Count,
                AcceptedOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // Calculate Keta month-to-date summary
            var ketaMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Keta",
                TotalOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaMonthShifts.Count,
                AcceptedOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // ===== CALCULATE TOTALS =====

            var totalDayOrders = hungerDaySummary.AcceptedOrders + ketaDaySummary.AcceptedOrders;
            var totalDayShifts = hungerDaySummary.TotalShifts + ketaDaySummary.TotalShifts;

            var totalMonthOrders = hungerMonthSummary.AcceptedOrders + ketaMonthSummary.AcceptedOrders;
            var totalMonthShifts = hungerMonthSummary.TotalShifts + ketaMonthSummary.TotalShifts;

            var summary = new PreviousDayCompanySummary(
                ReportDate: yesterday,
                Hunger: hungerDaySummary,
                Keta: ketaDaySummary,
                TotalDayOrders: totalDayOrders,
                TotalDayShifts: totalDayShifts,
                HungerMonthToDate: hungerMonthSummary,
                KetaMonthToDate: ketaMonthSummary,
                TotalMonthOrders: totalMonthOrders,
                TotalMonthShifts: totalMonthShifts,
                MonthStartDate: monthStart
            );

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            return Result.Failure<PreviousDayCompanySummary>(
                new Error($"Error generating previous day summary: {ex.Message}", "server_error", 500));
        }
    }




    public async Task<Result<PreviousDayCompanySummary>> GetHousingPreviousDayCompanySummaryAsync(
       long managerIqamaNo,
       CancellationToken cancellationToken = default)
    {
        try
        {
            // Find the housing by manager iqama number
            var housing = await _dbcontext.Set<Housing>()
                .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo, cancellationToken);

            if (housing == null)
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No housing found for manager iqama number {managerIqamaNo}", "housing_not_found", 404));
            }

            // Get yesterday's date and current month range
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var yesterday = today.AddDays(-1);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            // Get rider IDs for employees in this housing
            var housingRiderIds = await _dbcontext.Set<Employees>()
                .Where(e => e.HousingId == housing.Id)
                .Join(_dbcontext.Set<RiderDetails>(),
                      emp => emp.IqamaNo,
                      rider => rider.EmployeeIqamaNo,
                      (emp, rider) => rider.Id)
                .ToListAsync(cancellationToken);

            if (!housingRiderIds.Any())
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No riders found in housing managed by {managerIqamaNo}", "no_riders", 404));
            }

            // Get all shifts for yesterday for riders in this housing
            var yesterdayShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate == yesterday && housingRiderIds.Contains(s.RiderId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Get all shifts for the current month up to today for riders in this housing
            var monthShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate >= monthStart && s.ShiftDate < today && housingRiderIds.Contains(s.RiderId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!yesterdayShifts.Any() && !monthShifts.Any())
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No shifts found for housing managed by {managerIqamaNo} on {yesterday:yyyy-MM-dd} or current month", "no_data", 404));
            }

            // ===== YESTERDAY'S DATA =====

            // Filter shifts for Hunger company (yesterday)
            var hungerYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (yesterday)
            var ketaYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate Hunger summary (yesterday)
            var hungerDaySummary = new CompanyDaySummary(
                CompanyName: "Hunger",
                TotalOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerYesterdayShifts.Count,
                AcceptedOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // Calculate Keta summary (yesterday)
            var ketaDaySummary = new CompanyDaySummary(
                CompanyName: "Keta",
                TotalOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaYesterdayShifts.Count,
                AcceptedOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // ===== MONTH-TO-DATE DATA =====

            // Filter shifts for Hunger company (month-to-date)
            var hungerMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (month-to-date)
            var ketaMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate number of days with data in the month
            var daysInMonth = monthShifts
                .Select(s => s.ShiftDate)
                .Distinct()
                .Count();

            // Calculate Hunger month-to-date summary
            var hungerMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Hunger",
                TotalOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerMonthShifts.Count,
                AcceptedOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // Calculate Keta month-to-date summary
            var ketaMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Keta",
                TotalOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaMonthShifts.Count,
                AcceptedOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // ===== CALCULATE TOTALS =====

            var totalDayOrders = hungerDaySummary.AcceptedOrders + ketaDaySummary.AcceptedOrders;
            var totalDayShifts = hungerDaySummary.TotalShifts + ketaDaySummary.TotalShifts;

            var totalMonthOrders = hungerMonthSummary.AcceptedOrders + ketaMonthSummary.AcceptedOrders;
            var totalMonthShifts = hungerMonthSummary.TotalShifts + ketaMonthSummary.TotalShifts;

            var summary = new PreviousDayCompanySummary(
                ReportDate: yesterday,
                Hunger: hungerDaySummary,
                Keta: ketaDaySummary,
                TotalDayOrders: totalDayOrders,
                TotalDayShifts: totalDayShifts,
                HungerMonthToDate: hungerMonthSummary,
                KetaMonthToDate: ketaMonthSummary,
                TotalMonthOrders: totalMonthOrders,
                TotalMonthShifts: totalMonthShifts,
                MonthStartDate: monthStart
            );

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            return Result.Failure<PreviousDayCompanySummary>(
                new Error($"Error generating previous day summary for housing: {ex.Message}", "server_error", 500));
        }
    }

    public async Task<Result<MonthlyRiderReport>> GetMonthlyReportByWorkingIdAsync(
        string WorkingId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<MonthlyRiderReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (month < 1 || month > 12)
            return Result.Failure<MonthlyRiderReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<MonthlyRiderReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(CreateEmptyMonthlyReport(
                rider.Id, rider.Employee.NameAR, WorkingId, year, month));
        }

        var workingIdHistory = DetectWorkingIdChanges(shifts);
        var totalWorkingDays = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());
        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejected = shifts.Sum(s => s.RejectedDailyOrders);
        var totalRealRejected = shifts.Sum(s => s.RealRejectedDailyOrders);
        var totalHours = shifts.Sum(s => s.WorkingHours);
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));

        var companyBreakdowns = CalculateCompanyBreakdowns(shifts);

        var overallPerformanceScore = companyBreakdowns.Any()
            ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / totalWorkingDays
            : 0;

        var problematicShifts = shifts
            .Where(s => HasRejectionProblem(s) || s.ShiftStatus == ShiftStatus.Failed.ToString())
            .Select(CreateProblemShiftDetail)
            .ToList();

        var report = new MonthlyRiderReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
            Year: year,
            Month: month,
            TotalWorkingDays: totalWorkingDays,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: totalRejected,
            TotalRealRejectedOrders: totalRealRejected,
            TotalWorkingHours: totalHours,
            ProblematicShiftsCount: problematicShifts.Count,
            TotalPenaltyAmount: totalPenalty,
            OverallPerformanceScore: overallPerformanceScore,
            CompanyBreakdowns: companyBreakdowns,
            ProblematicShifts: problematicShifts,
            WorkingIdHistory: workingIdHistory
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<MonthlyRiderReport>>> GetAllRidersMonthlyReportAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return Result.Failure<IEnumerable<MonthlyRiderReport>>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var allRiders = await _dbcontext.RiderDetails
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var reports = new List<MonthlyRiderReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetMonthlyReportByWorkingIdAsync(
                rider.WorkingId!, year, month, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<MonthlyRiderReport>>(reports);
    }

    public async Task<Result<YearlyRiderReport>> GetYearlyReportByWorkingIdAsync(
        string WorkingId,
        int year,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<YearlyRiderReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<YearlyRiderReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(CreateEmptyYearlyReport(
                rider.Id, rider.Employee.NameAR, WorkingId, year));
        }

        var workingIdHistory = DetectWorkingIdChanges(shifts);
        var totalWorkingDays = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));
        var problematicCount = shifts.Count(s => HasRejectionProblem(s));

        var yearlyCompanyBreakdowns = CalculateYearlyCompanyBreakdowns(shifts);
        var monthlyBreakdowns = CalculateMonthlyBreakdowns(shifts);

        var avgPerformanceScore = monthlyBreakdowns.Any()
            ? monthlyBreakdowns.Average(mb => mb.PerformanceScore)
            : 0;

        var report = new YearlyRiderReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
            Year: year,
            TotalWorkingDays: totalWorkingDays,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            TotalAcceptedOrders: shifts.Sum(s => s.AcceptedDailyOrders),
            TotalRejectedOrders: shifts.Sum(s => s.RejectedDailyOrders),
            TotalRealRejectedOrders: shifts.Sum(s => s.RealRejectedDailyOrders),
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours),
            ProblematicShiftsCount: problematicCount,
            TotalPenaltyAmount: totalPenalty,
            AveragePerformanceScore: avgPerformanceScore,
            YearlyCompanyBreakdowns: yearlyCompanyBreakdowns,
            MonthlyBreakdowns: monthlyBreakdowns,
            WorkingIdHistory: workingIdHistory
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<YearlyRiderReport>>> GetAllRidersYearlyReportAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        var allRiders = await _dbcontext.RiderDetails
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var reports = new List<YearlyRiderReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetYearlyReportByWorkingIdAsync(
                rider.WorkingId!, year, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<YearlyRiderReport>>(reports);
    }


    public async Task<Result<DateRangeReport>> GetCustomDateRangeReportByWorkingIdAsync(
        string WorkingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<DateRangeReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<DateRangeReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<DateRangeReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(CreateEmptyDateRangeReport(
                rider.Id, rider.EmployeeIqamaNo, rider.Employee.NameAR, WorkingId, startDate, endDate));
        }

        var workingIdHistory = DetectWorkingIdChanges(shifts);
        var totalWorkingDays = shifts.Count;
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));

        var companyBreakdowns = CalculateCompanyBreakdowns(shifts);
        var overallPerformanceScore = companyBreakdowns.Any()
            ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / totalWorkingDays
            : 0;

        var problematicShifts = shifts
            .Where(s => HasRejectionProblem(s) || s.ShiftStatus == ShiftStatus.Failed.ToString())
            .Select(CreateProblemShiftDetail)
            .ToList();

        var report = new DateRangeReport(
            RiderId: rider.Id,
            IqamaNo: rider.EmployeeIqamaNo,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
            StartDate: startDate,
            EndDate: endDate,
            TotalWorkingDays: totalWorkingDays,
            CompletedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
            IncompleteShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
            FailedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
            TotalAcceptedOrders: shifts.Sum(s => s.AcceptedDailyOrders),
            TotalRejectedOrders: shifts.Sum(s => s.RejectedDailyOrders),
            TotalRealRejectedOrders: shifts.Sum(s => s.RealRejectedDailyOrders),
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours),
            ProblematicShiftsCount: problematicShifts.Count,
            TotalPenaltyAmount: totalPenalty,
            OverallPerformanceScore: overallPerformanceScore,
            CompanyBreakdowns: companyBreakdowns,
            ProblematicShifts: problematicShifts,
            WorkingIdHistory: workingIdHistory
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<DateRangeReport>>> GetAllRidersCustomDateRangeReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<IEnumerable<DateRangeReport>>(
                new Error("End date must be after start date", "invalid_input", 400));

        var allRiders = await _dbcontext.RiderDetails
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var reports = new List<DateRangeReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetCustomDateRangeReportByWorkingIdAsync(
                rider.WorkingId!, startDate, endDate, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<DateRangeReport>>(reports);
    }


    public async Task<Result<CompanyPerformanceReport>> GetCompanyPerformanceReportAsync(
        string companyName,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return Result.Failure<CompanyPerformanceReport>(
                new Error("Company name is required", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<CompanyPerformanceReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        var company = await _dbcontext.Companies
            .FirstOrDefaultAsync(c => c.Name == companyName, cancellationToken);

        if (company == null)
            return Result.Failure<CompanyPerformanceReport>(
                new Error($"Company '{companyName}' not found", "not_found", 404));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.Company.Name == companyName &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Failure<CompanyPerformanceReport>(
                new Error($"No shifts found for company '{companyName}' in the specified period", "no_data", 404));

        var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
        var totalWorkingDays = shifts.Count;
        var expectedOrders = totalWorkingDays * dailyTarget;
        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var performanceScore = expectedOrders > 0
            ? (decimal)totalAccepted / expectedOrders * 100
            : 0;

        var riderPerformances = shifts
            .GroupBy(s => s.RiderId)
            .Select(g => new RiderCompanyPerformance(
                RiderId: g.Key,
                RiderName: g.First().Rider?.Employee.NameAR ?? "Unknown",
                WorkingId: g.First().WorkingId,
                TotalShifts: g.Count(),
                CompletedShifts: g.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                TotalAcceptedOrders: g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders: g.Sum(s => s.RejectedDailyOrders),
                PerformanceScore: CalculateRiderPerformanceScore(g.ToList(), dailyTarget)
            ))
            .OrderByDescending(r => r.PerformanceScore)
            .ToList();

        var report = new CompanyPerformanceReport(
            CompanyName: companyName,
            StartDate: startDate,
            EndDate: endDate,
            DailyOrderTarget: dailyTarget,
            TotalWorkingDays: totalWorkingDays,
            ExpectedOrders: expectedOrders,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: shifts.Sum(s => s.RejectedDailyOrders),
            CompletedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
            IncompleteShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
            FailedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
            OverallPerformanceScore: performanceScore,
            TotalPenaltyAmount: shifts.Sum(s => CalculatePenalty(s)),
            RiderPerformances: riderPerformances
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<ProblemShiftDetail>>> GetProblematicShiftsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<IEnumerable<ProblemShiftDetail>>(
                new Error("End date must be after start date", "invalid_input", 400));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       (s.ShiftStatus != ShiftStatus.Completed.ToString() ||
                        s.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold))
            .OrderByDescending(s => s.RealRejectedDailyOrders)
            .ThenBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        var problematicShifts = shifts
            .Select(CreateProblemShiftDetail)
            .ToList();

        return Result.Success<IEnumerable<ProblemShiftDetail>>(problematicShifts);
    }


    public async Task<Result<RiderPeriodComparison>> CompareRiderPeriodsAsync(
    string WorkingId,
    DateOnly period1Start,
    DateOnly period1End,
    DateOnly period2Start,
    DateOnly period2End,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (period1End < period1Start)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<RiderPeriodComparison>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

        // Get shifts for both periods
        var period1Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= period1Start &&
                       s.ShiftDate <= period1End)
            .ToListAsync(cancellationToken);

        var period2Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= period2Start &&
                       s.ShiftDate <= period2End)
            .ToListAsync(cancellationToken);

        // Build period summaries
        var period1Summary = BuildPeriodSummary(period1Start, period1End, period1Shifts);
        var period2Summary = BuildPeriodSummary(period2Start, period2End, period2Shifts);

        // Calculate comparison metrics
        var comparisonMetrics = CalculateComparisonMetrics(period1Summary, period2Summary);

        // Generate verdict
        var verdict = GeneratePerformanceVerdict(period1Summary, period2Summary, comparisonMetrics);

        // Generate insights and recommendations
        var insights = GenerateComparisonInsights(period1Summary, period2Summary, comparisonMetrics);
        var recommendations = GenerateRecommendations(period2Summary, comparisonMetrics, verdict);

        var comparison = new RiderPeriodComparison(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
            Period1: period1Summary,
            Period2: period2Summary,
            Comparison: comparisonMetrics,
            Verdict: verdict,
            KeyInsights: insights,
            Recommendations: recommendations
        );

        return Result.Success(comparison);
    }

    public async Task<Result<IEnumerable<RiderPeriodComparison>>> CompareAllRidersPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        if (period1End < period1Start)
            return Result.Failure<IEnumerable<RiderPeriodComparison>>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<IEnumerable<RiderPeriodComparison>>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        var allRiders = await _dbcontext.RiderDetails
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var comparisons = new List<RiderPeriodComparison>();

        foreach (var rider in allRiders)
        {
            var result = await CompareRiderPeriodsAsync(
                rider.WorkingId!,
                period1Start,
                period1End,
                period2Start,
                period2End,
                cancellationToken);

            if (result.IsSuccess)
            {
                comparisons.Add(result.Value);
            }
        }

        // Sort by overall improvement
        var sortedComparisons = comparisons
            .OrderByDescending(c => c.Verdict.ImprovementScore)
            .ToList();

        return Result.Success<IEnumerable<RiderPeriodComparison>>(sortedComparisons);
    }

    public async Task<Result<CompanyPeriodComparison>> CompareCompanyPeriodsAsync(
        string companyName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return Result.Failure<CompanyPeriodComparison>(
                new Error("Company name is required", "invalid_input", 400));

        if (period1End < period1Start)
            return Result.Failure<CompanyPeriodComparison>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<CompanyPeriodComparison>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        var company = await _dbcontext.Companies
            .FirstOrDefaultAsync(c => c.Name == companyName, cancellationToken);

        if (company == null)
            return Result.Failure<CompanyPeriodComparison>(
                new Error($"Company '{companyName}' not found", "not_found", 404));

        // Get shifts for both periods
        var period1Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.Company.Name == companyName &&
                       s.ShiftDate >= period1Start &&
                       s.ShiftDate <= period1End)
            .ToListAsync(cancellationToken);

        var period2Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.Company.Name == companyName &&
                       s.ShiftDate >= period2Start &&
                       s.ShiftDate <= period2End)
            .ToListAsync(cancellationToken);

        if (!period1Shifts.Any() && !period2Shifts.Any())
            return Result.Failure<CompanyPeriodComparison>(
                new Error($"No shifts found for company '{companyName}' in either period", "no_data", 404));

        // Build period summaries
        var period1Summary = BuildPeriodSummary(period1Start, period1End, period1Shifts);
        var period2Summary = BuildPeriodSummary(period2Start, period2End, period2Shifts);

        // Calculate comparison metrics
        var comparisonMetrics = CalculateComparisonMetrics(period1Summary, period2Summary);

        // Get rider comparisons for top improved and declined
        var riderComparisons = await GetRiderComparisonsForCompany(
            companyName, period1Start, period1End, period2Start, period2End, cancellationToken);

        var topImproved = riderComparisons
            .Where(r => r.Verdict.ImprovementScore > 0)
            .OrderByDescending(r => r.Verdict.ImprovementScore)
            .Take(5)
            .ToList();

        var topDeclined = riderComparisons
            .Where(r => r.Verdict.ImprovementScore < 0)
            .OrderBy(r => r.Verdict.ImprovementScore)
            .Take(5)
            .ToList();

        var overallTrend = DetermineOverallTrend(comparisonMetrics, riderComparisons);

        var comparison = new CompanyPeriodComparison(
            CompanyName: companyName,
            Period1: period1Summary,
            Period2: period2Summary,
            Comparison: comparisonMetrics,
            TopImprovedRiders: topImproved,
            TopDeclinedRiders: topDeclined,
            OverallTrend: overallTrend
        );

        return Result.Success(comparison);
    }

    public async Task<Result<RiderPeriodComparison>> CompareRiderMonthsAsync(
        string WorkingId,
        int year1,
        int month1,
        int year2,
        int month2,
        CancellationToken cancellationToken = default)
    {
        if (month1 < 1 || month1 > 12)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Month 1 must be between 1 and 12", "invalid_input", 400));

        if (month2 < 1 || month2 > 12)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Month 2 must be between 1 and 12", "invalid_input", 400));

        var period1Start = new DateOnly(year1, month1, 1);
        var period1End = period1Start.AddMonths(1).AddDays(-1);

        var period2Start = new DateOnly(year2, month2, 1);
        var period2End = period2Start.AddMonths(1).AddDays(-1);

        return await CompareRiderPeriodsAsync(
            WorkingId,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
    }

    public async Task<Result<RiderPeriodComparison>> CompareRiderYearsAsync(
        string WorkingId,
        int year1,
        int year2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Invalid working ID", "invalid_input", 400));

        var period1Start = new DateOnly(year1, 1, 1);
        var period1End = new DateOnly(year1, 12, 31);

        var period2Start = new DateOnly(year2, 1, 1);
        var period2End = new DateOnly(year2, 12, 31);

        return await CompareRiderPeriodsAsync(
            WorkingId,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
    }





    private string DetermineOverallTrend(
    ComparisonMetrics companyMetrics,
    List<RiderPeriodComparison> riderComparisons)
    {
        if (!riderComparisons.Any())
            return "➡️ No Data Available";

        var totalRiders = riderComparisons.Count;
        var improvingRiders = 0;
        var decliningRiders = 0;
        var stableRiders = 0;

        // Analyze company-level metrics
        var companyImprovements = 0;
        var companyDeclines = 0;

        if (companyMetrics.OrdersChangePercent > 5) companyImprovements++;
        else if (companyMetrics.OrdersChangePercent < -5) companyDeclines++;

        if (companyMetrics.CompletionRateChangePercent > 3) companyImprovements++;
        else if (companyMetrics.CompletionRateChangePercent < -3) companyDeclines++;

        if (companyMetrics.PerformanceScoreChangePercent > 5) companyImprovements++;
        else if (companyMetrics.PerformanceScoreChangePercent < -5) companyDeclines++;

        if (companyMetrics.ProblematicShiftsChangePercent < -10) companyImprovements++;
        else if (companyMetrics.ProblematicShiftsChangePercent > 10) companyDeclines++;

        // Analyze individual rider performance
        foreach (var rider in riderComparisons)
        {
            switch (rider.Verdict.OverallResult)
            {
                case ComparisonResult.Better:
                    improvingRiders++;
                    break;
                case ComparisonResult.Worse:
                    decliningRiders++;
                    break;
                default:
                    stableRiders++;
                    break;
            }
        }

        // Calculate percentages
        var improvingPercent = (decimal)improvingRiders / totalRiders * 100;
        var decliningPercent = (decimal)decliningRiders / totalRiders * 100;
        var stablePercent = (decimal)stableRiders / totalRiders * 100;

        // Determine company-level trend
        string companyTrend;
        if (companyImprovements > companyDeclines + 1)
            companyTrend = "strong improvement";
        else if (companyImprovements > companyDeclines)
            companyTrend = "improving";
        else if (companyDeclines > companyImprovements + 1)
            companyTrend = "declining";
        else if (companyDeclines > companyImprovements)
            companyTrend = "needs attention";
        else
            companyTrend = "stable";

        // Combine company and rider trends for final verdict
        if (companyImprovements > companyDeclines && improvingPercent >= 60)
            return $"📈 Strong Overall Improvement - Company metrics {companyTrend}, {improvingPercent:F0}% of riders improving ({improvingRiders}/{totalRiders})";

        if (companyImprovements > companyDeclines && improvingPercent >= 40)
            return $"✅ Positive Trend - Company {companyTrend}, majority of riders improving ({improvingRiders}/{totalRiders})";

        if (companyDeclines > companyImprovements && decliningPercent >= 60)
            return $"📉 Significant Decline - Company metrics {companyTrend}, {decliningPercent:F0}% of riders declining ({decliningRiders}/{totalRiders})";

        if (companyDeclines > companyImprovements && decliningPercent >= 40)
            return $"⚠️ Needs Attention - Company {companyTrend}, {decliningPercent:F0}% of riders declining ({decliningRiders}/{totalRiders})";

        if (improvingPercent >= 50)
            return $"✅ Generally Improving - Company {companyTrend}, {improvingPercent:F0}% improving vs {decliningPercent:F0}% declining";

        if (decliningPercent >= 50)
            return $"⚠️ Concerning Trend - Company {companyTrend}, {decliningPercent:F0}% declining vs {improvingPercent:F0}% improving";

        if (stablePercent >= 50)
            return $"➡️ Stable Performance - Company {companyTrend}, {stablePercent:F0}% of riders maintaining performance";

        return $"🔄 Mixed Results - Company {companyTrend}, riders split: {improvingPercent:F0}% improving, {decliningPercent:F0}% declining, {stablePercent:F0}% stable";
    }
    private PeriodSummary BuildPeriodSummary(
        DateOnly startDate,
        DateOnly endDate,
        List<RiderShift> shifts)
    {
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
        var workingDays = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());
        var absentShifts = totalDays - workingDays;

        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejected = shifts.Sum(s => s.RejectedDailyOrders);
        var totalRealRejected = shifts.Sum(s => s.RealRejectedDailyOrders);
        var totalStacked = shifts.Sum(s => s.StackedDeliveries);
        var totalHours = shifts.Sum(s => s.WorkingHours);

        var problematicCount = shifts.Count(s => HasRejectionProblem(s));
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));

        var avgOrdersPerDay = workingDays > 0 ? (decimal)totalAccepted / workingDays : 0;
        var avgStackedPerDay = workingDays > 0 ? (decimal)totalStacked / workingDays : 0;
        var completionRate = workingDays > 0 ? (decimal)completedShifts / workingDays * 100 : 0;

        // Calculate performance score
        var companyBreakdowns = CalculateCompanyBreakdowns(shifts);
        var performanceScore = companyBreakdowns.Any() && workingDays > 0
            ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / workingDays
            : 0;

        return new PeriodSummary(
            StartDate: startDate,
            EndDate: endDate,
            TotalDays: totalDays,
            WorkingDays: workingDays,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            AbsentShifts: absentShifts,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: totalRejected,
            TotalRealRejectedOrders: totalRealRejected,
            TotalStackedDeliveries: totalStacked,
            TotalWorkingHours: totalHours,
            ProblematicShiftsCount: problematicCount,
            TotalPenaltyAmount: totalPenalty,
            AverageStackedPerDay: avgStackedPerDay,
            AverageOrdersPerDay: avgOrdersPerDay,
            CompletionRate: completionRate,
            PerformanceScore: performanceScore,
            CompanyBreakdowns: companyBreakdowns
        );
    }

    private ComparisonMetrics CalculateComparisonMetrics(
        PeriodSummary period1,
        PeriodSummary period2)
    {
        return new ComparisonMetrics(
            WorkingDaysDifference: period2.WorkingDays - period1.WorkingDays,
            WorkingDaysChangePercent: CalculatePercentChange(period1.WorkingDays, period2.WorkingDays),
            OrdersDifference: period2.TotalAcceptedOrders - period1.TotalAcceptedOrders,
            OrdersChangePercent: CalculatePercentChange(period1.TotalAcceptedOrders, period2.TotalAcceptedOrders),
            AverageOrdersPerDayDifference: period2.AverageOrdersPerDay - period1.AverageOrdersPerDay,
            AverageOrdersPerDayChangePercent: CalculatePercentChange(period1.AverageOrdersPerDay, period2.AverageOrdersPerDay),
            CompletionRateDifference: period2.CompletionRate - period1.CompletionRate,
            CompletionRateChangePercent: CalculatePercentChange(period1.CompletionRate, period2.CompletionRate),
            PerformanceScoreDifference: period2.PerformanceScore - period1.PerformanceScore,
            PerformanceScoreChangePercent: CalculatePercentChange(period1.PerformanceScore, period2.PerformanceScore),
            WorkingHoursDifference: period2.TotalWorkingHours - period1.TotalWorkingHours,
            WorkingHoursChangePercent: CalculatePercentChange((decimal)period1.TotalWorkingHours, (decimal)period2.TotalWorkingHours),
            PenaltyDifference: period2.TotalPenaltyAmount - period1.TotalPenaltyAmount,
            PenaltyChangePercent: CalculatePercentChange(period1.TotalPenaltyAmount, period2.TotalPenaltyAmount),
            ProblematicShiftsDifference: period2.ProblematicShiftsCount - period1.ProblematicShiftsCount,
            ProblematicShiftsChangePercent: CalculatePercentChange(period1.ProblematicShiftsCount, period2.ProblematicShiftsCount),
            RejectionRateDifference: CalculateRejectionRate(period2) - CalculateRejectionRate(period1),
            RejectionRateChangePercent: CalculatePercentChange(CalculateRejectionRate(period1), CalculateRejectionRate(period2))
        );
    }

    private decimal CalculateRejectionRate(PeriodSummary period)
    {
        var totalOrders = period.TotalAcceptedOrders + period.TotalRejectedOrders;
        return totalOrders > 0 ? (decimal)period.TotalRejectedOrders / totalOrders * 100 : 0;
    }

    private PeriodPerformanceVerdict GeneratePerformanceVerdict(
        PeriodSummary period1,
        PeriodSummary period2,
        ComparisonMetrics metrics)
    {
        var improvements = new List<MetricChange>();
        var declines = new List<MetricChange>();

        // Analyze each metric
        AnalyzeMetricChange(
            "Performance Score",
            period1.PerformanceScore, period2.PerformanceScore,
            metrics.PerformanceScoreChangePercent,
            improvements, declines, isHigherBetter: true);

        AnalyzeMetricChange(
            "Completion Rate",
            period1.CompletionRate, period2.CompletionRate,
            metrics.CompletionRateChangePercent,
            improvements, declines, isHigherBetter: true);

        AnalyzeMetricChange(
            "Average Orders/Day",
            period1.AverageOrdersPerDay, period2.AverageOrdersPerDay,
            metrics.AverageOrdersPerDayChangePercent,
            improvements, declines, isHigherBetter: true);

        AnalyzeMetricChange(
            "Rejection Rate",
            CalculateRejectionRate(period1), CalculateRejectionRate(period2),
            metrics.RejectionRateChangePercent,
            improvements, declines, isHigherBetter: false);

        AnalyzeMetricChange(
            "Penalties",
            period1.TotalPenaltyAmount, period2.TotalPenaltyAmount,
            metrics.PenaltyChangePercent,
            improvements, declines, isHigherBetter: false);

        AnalyzeMetricChange(
            "Problematic Shifts",
            period1.ProblematicShiftsCount, period2.ProblematicShiftsCount,
            metrics.ProblematicShiftsChangePercent,
            improvements, declines, isHigherBetter: false);

        // Calculate improvement score
        var improvementScore = CalculateImprovementScore(improvements, declines);

        // Determine overall result
        var overallResult = DetermineOverallResult(improvementScore, improvements.Count, declines.Count);

        // Generate summary
        var summary = GenerateVerdictSummary(overallResult, improvementScore, improvements, declines);

        return new PeriodPerformanceVerdict(
            OverallResult: overallResult,
            Summary: summary,
            ImprovementScore: improvementScore,
            TopImprovements: improvements.OrderByDescending(i => Math.Abs(i.ChangePercent)).Take(3).ToList(),
            TopDeclines: declines.OrderByDescending(d => Math.Abs(d.ChangePercent)).Take(3).ToList()
        );
    }

    private void AnalyzeMetricChange(
        string metricName,
        decimal oldValue,
        decimal newValue,
        decimal changePercent,
        List<MetricChange> improvements,
        List<MetricChange> declines,
        bool isHigherBetter)
    {
        if (Math.Abs(changePercent) < 1) return; // Ignore negligible changes

        var direction = newValue > oldValue ? TrendDirection.Up :
                        newValue < oldValue ? TrendDirection.Down :
                        TrendDirection.Stable;

        var isImprovement = (isHigherBetter && direction == TrendDirection.Up) ||
                            (!isHigherBetter && direction == TrendDirection.Down);

        var change = new MetricChange(
            MetricName: metricName,
            OldValue: FormatMetricValue(oldValue, metricName),
            NewValue: FormatMetricValue(newValue, metricName),
            ChangePercent: changePercent,
            Direction: direction
        );

        if (isImprovement)
            improvements.Add(change);
        else if (direction != TrendDirection.Stable)
            declines.Add(change);
    }

    private string FormatMetricValue(decimal value, string metricName)
    {
        if (metricName.Contains("Rate") || metricName.Contains("Score"))
            return $"{value:F1}%";
        if (metricName.Contains("Penalties"))
            return $"{value:F2} SAR";
        return $"{value:F1}";
    }

    private decimal CalculateImprovementScore(
        List<MetricChange> improvements,
        List<MetricChange> declines)
    {
        var improvementWeight = improvements.Sum(i => Math.Abs(i.ChangePercent));
        var declineWeight = declines.Sum(d => Math.Abs(d.ChangePercent));

        if (improvementWeight + declineWeight == 0)
            return 0;

        return ((improvementWeight - declineWeight) / (improvementWeight + declineWeight)) * 100;
    }

    private ComparisonResult DetermineOverallResult(
        decimal improvementScore,
        int improvementCount,
        int declineCount)
    {
        if (Math.Abs(improvementScore) < 10 && Math.Abs(improvementCount - declineCount) <= 1)
            return ComparisonResult.Same;

        if (improvementCount > 0 && declineCount > 0)
            return improvementScore > 20 ? ComparisonResult.Better :
                   improvementScore < -20 ? ComparisonResult.Worse :
                   ComparisonResult.Mixed;

        return improvementScore > 0 ? ComparisonResult.Better : ComparisonResult.Worse;
    }

    private string GenerateVerdictSummary(
        ComparisonResult result,
        decimal improvementScore,
        List<MetricChange> improvements,
        List<MetricChange> declines)
    {
        return result switch
        {
            ComparisonResult.Better =>
                $"Performance improved significantly with an improvement score of {improvementScore:F1}. " +
                $"{improvements.Count} metrics showed positive changes.",

            ComparisonResult.Worse =>
                $"Performance declined with an improvement score of {improvementScore:F1}. " +
                $"{declines.Count} metrics showed negative changes.",

            ComparisonResult.Mixed =>
                $"Performance showed mixed results (score: {improvementScore:F1}). " +
                $"{improvements.Count} improvements vs {declines.Count} declines.",

            ComparisonResult.Same =>
                $"Performance remained relatively stable with minimal changes (score: {improvementScore:F1}).",

            _ => "Unable to determine performance trend."
        };
    }

    private List<string> GenerateComparisonInsights(
        PeriodSummary period1,
        PeriodSummary period2,
        ComparisonMetrics metrics)
    {
        var insights = new List<string>();

        // Working days insight
        if (Math.Abs(metrics.WorkingDaysChangePercent) >= 20)
        {
            var direction = metrics.WorkingDaysDifference > 0 ? "increased" : "decreased";
            insights.Add($"📅 Working days {direction} by {Math.Abs(metrics.WorkingDaysChangePercent):F1}% " +
                        $"({period1.WorkingDays} → {period2.WorkingDays})");
        }

        // Orders insight
        if (Math.Abs(metrics.AverageOrdersPerDayChangePercent) >= 10)
        {
            var emoji = metrics.AverageOrdersPerDayDifference > 0 ? "📈" : "📉";
            insights.Add($"{emoji} Daily average orders changed by {metrics.AverageOrdersPerDayChangePercent:F1}% " +
                        $"({period1.AverageOrdersPerDay:F1} → {period2.AverageOrdersPerDay:F1})");
        }

        // Completion rate insight
        if (Math.Abs(metrics.CompletionRateDifference) >= 5)
        {
            var emoji = metrics.CompletionRateDifference > 0 ? "✅" : "⚠️";
            insights.Add($"{emoji} Completion rate changed by {metrics.CompletionRateDifference:F1} percentage points " +
                        $"({period1.CompletionRate:F1}% → {period2.CompletionRate:F1}%)");
        }

        // Performance score insight
        if (Math.Abs(metrics.PerformanceScoreDifference) >= 5)
        {
            var emoji = metrics.PerformanceScoreDifference > 0 ? "🌟" : "📊";
            insights.Add($"{emoji} Performance score {(metrics.PerformanceScoreDifference > 0 ? "improved" : "declined")} " +
                        $"by {Math.Abs(metrics.PerformanceScoreDifference):F1} points");
        }

        // Penalty insight
        if (metrics.PenaltyDifference != 0)
        {
            var emoji = metrics.PenaltyDifference < 0 ? "💰" : "⚠️";
            var change = metrics.PenaltyDifference < 0 ? "reduced" : "increased";
            insights.Add($"{emoji} Penalties {change} by {Math.Abs(metrics.PenaltyDifference):F2} SAR");
        }

        // Problematic shifts insight
        if (metrics.ProblematicShiftsDifference != 0)
        {
            var emoji = metrics.ProblematicShiftsDifference < 0 ? "✨" : "🔴";
            insights.Add($"{emoji} Problematic shifts changed from {period1.ProblematicShiftsCount} to {period2.ProblematicShiftsCount}");
        }

        if (!insights.Any())
            insights.Add("📊 Performance metrics remained relatively stable between periods");

        return insights;
    }

    private List<string> GenerateRecommendations(
        PeriodSummary period2,
        ComparisonMetrics metrics,
        PeriodPerformanceVerdict verdict)
    {
        var recommendations = new List<string>();

        // Based on completion rate
        if (period2.CompletionRate < 85)
        {
            recommendations.Add("🎯 Focus on improving shift completion rate - currently below target");
        }

        // Based on rejection rate
        var rejectionRate = CalculateRejectionRate(period2);
        if (rejectionRate > 15)
        {
            recommendations.Add("⚠️ High rejection rate detected - review order acceptance strategy");
        }

        // Based on penalties
        if (period2.TotalPenaltyAmount > 100)
        {
            recommendations.Add("💰 Reduce penalty costs by minimizing excess rejections");
        }

        // Based on performance score
        if (period2.PerformanceScore < 75)
        {
            recommendations.Add("📈 Performance score needs improvement - aim for 85% or higher");
        }

        // Based on trends
        if (metrics.CompletionRateChangePercent < -5)
        {
            recommendations.Add("🔄 Completion rate declining - investigate causes of incomplete shifts");
        }

        if (metrics.AverageOrdersPerDayChangePercent < -10)
        {
            recommendations.Add("📊 Daily order average declining - consider productivity improvements");
        }

        // Positive reinforcement
        if (verdict.OverallResult == ComparisonResult.Better)
        {
            recommendations.Add("⭐ Maintain current positive trend and consistency");
        }

        // If no issues, encourage continued excellence
        if (!recommendations.Any())
        {
            recommendations.Add("✅ Maintain excellent performance and consistency");
        }

        return recommendations;
    }

    private async Task<List<RiderPeriodComparison>> GetRiderComparisonsForCompany(
        string companyName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken)
    {
        // Get all riders who worked for this company in either period
        var riderIds = await _dbcontext.RiderShifts
            .Where(s => s.Company.Name == companyName &&
                       ((s.ShiftDate >= period1Start && s.ShiftDate <= period1End) ||
                        (s.ShiftDate >= period2Start && s.ShiftDate <= period2End)))
            .Select(s => s.Rider.WorkingId)
            .Distinct()
.Where(r => !string.IsNullOrWhiteSpace(r) && r != "0").ToListAsync(cancellationToken);

        var comparisons = new List<RiderPeriodComparison>();

        foreach (var workingId in riderIds)
        {
            if (string.IsNullOrEmpty(workingId)) continue;

            var result = await CompareRiderPeriodsAsync(
                workingId,
                period1Start,
                period1End,
                period2Start,
                period2End,
                cancellationToken);

            if (result.IsSuccess)
            {
                // Filter to only include shifts from this company
                var comparison = result.Value;
                var hasCompanyData = comparison.Period1.CompanyBreakdowns.Any(c => c.CompanyName == companyName) ||
                                   comparison.Period2.CompanyBreakdowns.Any(c => c.CompanyName == companyName);

                if (hasCompanyData)
                {
                    comparisons.Add(comparison);
                }
            }
        }

        return comparisons;
    }



    public async Task<Result<List<HousingPeriodComparison>>> CompareHousingPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        // Validate dates
        if (period1End < period1Start)
            return Result.Failure<List<HousingPeriodComparison>>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<List<HousingPeriodComparison>>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        // Get analysis for both periods
        var period1Result = await GetHousingAnalysisForPeriodAsync(
            period1Start, period1End, cancellationToken);

        if (!period1Result.IsSuccess)
            return Result.Failure<List<HousingPeriodComparison>>(period1Result.Error);

        var period2Result = await GetHousingAnalysisForPeriodAsync(
            period2Start, period2End, cancellationToken);

        if (!period2Result.IsSuccess)
            return Result.Failure<List<HousingPeriodComparison>>(period2Result.Error);

        var period1Analysis = period1Result.Value;
        var period2Analysis = period2Result.Value;

        // Get all housing IDs from both periods
        var allHousingIds = period1Analysis.HousingBreakdowns
            .Select(h => h.HousingId)
            .Union(period2Analysis.HousingBreakdowns.Select(h => h.HousingId))
            .Distinct()
            .ToList();

        var comparisons = new List<HousingPeriodComparison>();

        foreach (var housingId in allHousingIds)
        {
            var p1Housing = period1Analysis.HousingBreakdowns
                .FirstOrDefault(h => h.HousingId == housingId);

            var p2Housing = period2Analysis.HousingBreakdowns
                .FirstOrDefault(h => h.HousingId == housingId);

            // Only compare if housing exists in both periods
            if (p1Housing != null && p2Housing != null)
            {
                var metrics = CalculateHousingComparisonMetrics(p1Housing, p2Housing);
                var insights = GenerateHousingInsights(p1Housing, p2Housing, metrics);

                comparisons.Add(new HousingPeriodComparison(
                    HousingName: p2Housing.HousingName,
                    Period1Breakdown: p1Housing,
                    Period2Breakdown: p2Housing,
                    Comparison: metrics,
                    Insights: insights
                ));
            }
        }

        return Result.Success(comparisons.OrderByDescending(c => c.Period2Breakdown.CompletionRate).ToList());
    }

    public async Task<Result<PeriodHousingAnalysis>> GetHousingAnalysisForPeriodAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<PeriodHousingAnalysis>(
                new Error("End date must be after start date", "invalid_input", 400));

        // Get all shifts in the period with necessary includes
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                .ThenInclude(e => e.Housing)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Failure<PeriodHousingAnalysis>(
                new Error("No shifts found in the specified period", "no_data", 404));

        // Filter out shifts without housing information
        var validShifts = shifts
            .Where(s => s.Rider?.Employee?.Housing != null)
            .ToList();

        if (!validShifts.Any())
            return Result.Failure<PeriodHousingAnalysis>(
                new Error("No shifts with housing information found", "no_data", 404));

        // Group by housing
        var housingGroups = validShifts.GroupBy(s => s.Rider.Employee.HousingId);
        var housingBreakdowns = new List<HousingPeriodBreakdown>();
        var totalOrders = 0;
        var allRiderIds = new HashSet<int>();

        foreach (var group in housingGroups)
        {
            var housing = group.First().Rider.Employee.Housing;
            if (housing == null) continue;

            var housingShifts = group.ToList();
            var totalDailyOrders = housingShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
            var completedOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
            var rejectedOrders = housingShifts.Sum(s => s.RejectedDailyOrders);

            var completionRate = totalDailyOrders > 0
                ? (decimal)completedOrders / totalDailyOrders * 100
                : 0;

            var riderIds = housingShifts.Select(s => s.RiderId).Distinct().ToList();
            allRiderIds.UnionWith(riderIds);

            var riderAssignments = GetRiderAssignmentsForHousingFromShifts(
                riderIds, housingShifts);

            var problematicOrders = housingShifts
                .Count(s => s.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold);

            var avgOrdersPerRider = riderIds.Count > 0
                ? (decimal)completedOrders / riderIds.Count
                : 0;

            totalOrders += totalDailyOrders;

            housingBreakdowns.Add(new HousingPeriodBreakdown(
                HousingId: housing.Id,
                HousingName: housing.Name,
                DailyOrdersCount: totalDailyOrders,
                CompletedOrdersCount: completedOrders,
                RejectedOrdersCount: rejectedOrders,
                CompletionRate: completionRate,
                RiderCount: riderIds.Count,
                RiderAssignments: riderAssignments,
                HousingContribution: 0, // Will be calculated below
                ProblematicOrdersCount: problematicOrders,
                AverageOrdersPerRider: avgOrdersPerRider
            ));
        }

        // Calculate housing contributions
        housingBreakdowns = housingBreakdowns
            .Select(h => h with
            {
                HousingContribution = totalOrders > 0
                    ? (decimal)h.DailyOrdersCount / totalOrders * 100
                    : 0
            })
            .OrderByDescending(h => h.CompletionRate)
            .ToList();

        var topPerforming = housingBreakdowns.FirstOrDefault();
        var lowestPerforming = housingBreakdowns.LastOrDefault();

        var analysis = new PeriodHousingAnalysis(
            StartDate: startDate,
            EndDate: endDate,
            HousingBreakdowns: housingBreakdowns,
            TotalOrders: totalOrders,
            TotalRiders: allRiderIds.Count,
            TopPerformingHousing: topPerforming != null
                ? new HousingPerformanceRanking(
                    topPerforming.HousingId,
                    topPerforming.HousingName,
                    topPerforming.CompletionRate,
                    topPerforming.DailyOrdersCount,
                    topPerforming.RiderCount)
                : null,
            LowestPerformingHousing: lowestPerforming != null
                ? new HousingPerformanceRanking(
                    lowestPerforming.HousingId,
                    lowestPerforming.HousingName,
                    lowestPerforming.CompletionRate,
                    lowestPerforming.DailyOrdersCount,
                    lowestPerforming.RiderCount)
                : null
        );

        return Result.Success(analysis);
    }

    public async Task<Result<HousingPeriodComparison>> CompareSpecificHousingAsync(
        string housingName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {

        var housing = await _dbcontext.Housings
            .FirstOrDefaultAsync(h => h.Name == housingName, cancellationToken);

        if (housing == null)
            return Result.Failure<HousingPeriodComparison>(
                new Error($"Housing with  {housingName} not found", "not_found", 404));

        var period1Result = await GetHousingAnalysisForPeriodAsync(
            period1Start, period1End, cancellationToken);

        var period2Result = await GetHousingAnalysisForPeriodAsync(
            period2Start, period2End, cancellationToken);

        var p1Housing = period1Result.IsSuccess
            ? period1Result.Value.HousingBreakdowns.FirstOrDefault(h => h.HousingName == housingName)
            : null;

        var p2Housing = period2Result.IsSuccess
            ? period2Result.Value.HousingBreakdowns.FirstOrDefault(h => h.HousingName == housingName)
            : null;

        if (p1Housing == null || p2Housing == null)
            return Result.Failure<HousingPeriodComparison>(
                new Error($"Housing data not found for one or both periods", "no_data", 404));

        var metrics = CalculateHousingComparisonMetrics(p1Housing, p2Housing);
        var insights = GenerateHousingInsights(p1Housing, p2Housing, metrics);

        var comparison = new HousingPeriodComparison(
            HousingName: housing.Name,
            Period1Breakdown: p1Housing,
            Period2Breakdown: p2Housing,
            Comparison: metrics,
            Insights: insights
        );

        return Result.Success(comparison);
    }

    public async Task<Result<List<RiderHousingAssignment>>> GetRidersForHousingAsync(
        string housingName,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {

        if (endDate < startDate)
            return Result.Failure<List<RiderHousingAssignment>>(
                new Error("End date must be after start date", "invalid_input", 400));

        var housing = await _dbcontext.Housings
            .FirstOrDefaultAsync(h => h.Name == housingName, cancellationToken);

        if (housing == null)
            return Result.Failure<List<RiderHousingAssignment>>(
                new Error($"Housing with {housingName} not found", "not_found", 404));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                .ThenInclude(e => e.Housing)
            .Where(s => s.Rider.Employee.Housing.Name == housingName
                   && s.ShiftDate >= startDate
                   && s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Failure<List<RiderHousingAssignment>>(
                new Error($"No shifts found for housing '{housing.Name}' in the specified period", "no_data", 404));

        var riderGroups = shifts.GroupBy(s => s.RiderId);
        var assignments = new List<RiderHousingAssignment>();

        foreach (var group in riderGroups)
        {
            var rider = group.First().Rider;
            if (rider == null) continue;

            var riderShifts = group.ToList();
            var completed = riderShifts.Sum(s => s.AcceptedDailyOrders);
            var rejected = riderShifts.Sum(s => s.RejectedDailyOrders);
            var total = completed + rejected;

            var completionRate = total > 0
                ? (decimal)completed / total * 100
                : 0;

            assignments.Add(new RiderHousingAssignment(
                RiderId: rider.Id,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? "0",
                ShiftsCount: riderShifts.Count,
                OrdersCompleted: completed,
                OrdersRejected: rejected,
                CompletionRate: completionRate,
                TotalWorkingHours: riderShifts.Sum(s => s.WorkingHours)
            ));
        }

        return Result.Success(assignments.OrderByDescending(a => a.OrdersCompleted).ToList());
    }
    private List<RiderHousingAssignment> GetRiderAssignmentsForHousingFromShifts(
        List<int> riderIds,
        List<RiderShift> shifts)
    {
        var assignments = new List<RiderHousingAssignment>();
        var riderGroups = shifts.GroupBy(s => s.RiderId);

        foreach (var group in riderGroups)
        {
            var rider = group.First().Rider;
            if (rider == null) continue;

            var riderShifts = group.ToList();
            var completed = riderShifts.Sum(s => s.AcceptedDailyOrders);
            var rejected = riderShifts.Sum(s => s.RejectedDailyOrders);
            var total = completed + rejected;

            var completionRate = total > 0
                ? (decimal)completed / total * 100
                : 0;

            assignments.Add(new RiderHousingAssignment(
                RiderId: rider.Id,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? "0",
                ShiftsCount: riderShifts.Count,
                OrdersCompleted: completed,
                OrdersRejected: rejected,
                CompletionRate: completionRate,
                TotalWorkingHours: riderShifts.Sum(s => s.WorkingHours)
            ));
        }

        return assignments.OrderByDescending(a => a.OrdersCompleted).ToList();
    }

    private HousingComparisonMetrics CalculateHousingComparisonMetrics(
        HousingPeriodBreakdown period1,
        HousingPeriodBreakdown period2)
    {
        return new HousingComparisonMetrics(
            DailyOrdersDifference: period2.DailyOrdersCount - period1.DailyOrdersCount,
            DailyOrdersChangePercent: CalculatePercentChange(period1.DailyOrdersCount, period2.DailyOrdersCount),
            CompletedOrdersDifference: period2.CompletedOrdersCount - period1.CompletedOrdersCount,
            CompletedOrdersChangePercent: CalculatePercentChange(period1.CompletedOrdersCount, period2.CompletedOrdersCount),
            CompletionRateDifference: period2.CompletionRate - period1.CompletionRate,
            CompletionRateChangePercent: CalculatePercentChange(period1.CompletionRate, period2.CompletionRate),
            RiderCountDifference: period2.RiderCount - period1.RiderCount,
            RiderCountChangePercent: CalculatePercentChange(period1.RiderCount, period2.RiderCount),
            RejectedOrdersDifference: period2.RejectedOrdersCount - period1.RejectedOrdersCount,
            RejectionRateChangePercent: CalculatePercentChange(
                CalculateRejectionRate(period1),
                CalculateRejectionRate(period2)),
            HousingContributionDifference: period2.HousingContribution - period1.HousingContribution
        );
    }

    private decimal CalculatePercentChange(decimal oldValue, decimal newValue)
    {
        if (oldValue == 0)
            return newValue > 0 ? 100 : 0;

        return Math.Round(((newValue - oldValue) / oldValue) * 100, 2);
    }

    private decimal CalculateRejectionRate(HousingPeriodBreakdown housing)
    {
        var total = housing.CompletedOrdersCount + housing.RejectedOrdersCount;
        return total > 0
            ? (decimal)housing.RejectedOrdersCount / total * 100
            : 0;
    }
    public async Task<Result<TopRidersReport>> GetTopRidersInPeriodAsync(
        TopRidersRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        if (request.EndDate < request.StartDate)
            return Result.Failure<TopRidersReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        if (request.TopCount <= 0)
            return Result.Failure<TopRidersReport>(
                new Error("Top count must be greater than 0", "invalid_input", 400));

        try
        {
            // Load all shifts in period with necessary includes
            var shiftsQuery = _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Company)
                .Where(s => s.ShiftDate >= request.StartDate &&
                           s.ShiftDate <= request.EndDate);

            // Apply company filter if specified
            if (!string.IsNullOrWhiteSpace(request.CompanyFilter))
            {
                shiftsQuery = shiftsQuery.Where(s => s.Company.Name == request.CompanyFilter);
            }

            var shifts = await shiftsQuery.ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<TopRidersReport>(
                    new Error("No shifts found in the specified period", "no_data", 404));
            }

            // Load active substitutions to mark riders correctly
            var activeSubstitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Where(s => s.IsActive)
                .Include(s => s.ActualRider)
                .AsNoTracking()
                .ToListAsync(cancellationToken);


            var substitutionDict = activeSubstitutions
                .ToDictionary(s => s.ActualRiderId, s => s);

            // Group shifts by rider
            var riderGroups = shifts
                .GroupBy(s => s.RiderId)
                .ToList();

            // Filter by minimum shifts if specified
            if (request.MinimumShifts > 0)
            {
                riderGroups = riderGroups
                    .Where(g => g.Count() >= request.MinimumShifts)
                    .ToList();
            }

            if (!riderGroups.Any())
            {
                return Result.Failure<TopRidersReport>(
                    new Error($"No riders found with at least {request.MinimumShifts} shifts", "no_data", 404));
            }

            // Calculate metrics for each rider
            var riderDetails = new List<TopRiderDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalAccepted = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var totalRejected = riderShifts.Sum(s => s.RejectedDailyOrders);
                var totalRealRejected = riderShifts.Sum(s => s.RealRejectedDailyOrders);
                var totalHours = riderShifts.Sum(s => s.WorkingHours);
                var totalShifts = riderShifts.Count;

                var totalStacked = riderShifts.Sum(s => s.StackedDeliveries); // ADD THIS
                var avgStackedPerShift = totalShifts > 0
                ? (decimal)totalStacked / totalShifts
                : 0; // ADD THIS

                var completedShifts = riderShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
                var incompleteShifts = riderShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
                var failedShifts = riderShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());

                var completionRate = totalShifts > 0
                    ? (decimal)completedShifts / totalShifts * 100
                    : 0;

                var avgOrdersPerShift = totalShifts > 0
                    ? (decimal)totalAccepted / totalShifts
                    : 0;

                var totalOrders = totalAccepted + totalRejected;
                var rejectionRate = totalOrders > 0
                    ? (decimal)totalRejected / totalOrders * 100
                    : 0;

                // Calculate performance score
                var companyName = riderShifts.First().Company?.Name ?? "Unknown";
                var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
                var expectedOrders = totalShifts * dailyTarget;
                var performanceScore = expectedOrders > 0
                    ? (decimal)totalAccepted / expectedOrders * 100
                    : 0;

                // Calculate penalty
                var totalPenalty = riderShifts.Sum(s => CalculatePenalty(s));
                var problematicCount = riderShifts.Count(s => HasRejectionProblem(s));

                // Determine performance grade
                var grade = DeterminePerformanceGrade(performanceScore);

                // Generate achievements
                var achievements = GenerateRiderAchievements(
                    totalAccepted, avgOrdersPerShift, completionRate,
                    rejectionRate, totalShifts, performanceScore, totalStacked, avgStackedPerShift);

                // Check for active substitution
                var hasSubstitution = substitutionDict.ContainsKey(rider.Id);
                var originalWorkingId = hasSubstitution
                    ? substitutionDict[rider.Id].SubstituteWorkingId
                    : (string?)null;

                riderDetails.Add(new TopRiderDetail(
                    RiderId: rider.Id,
                    WorkingId: riderShifts.First().WorkingId,
                    RiderNameEN: rider.Employee.NameEN,
                    RiderNameAR: rider.Employee.NameAR,
                    CompanyName: companyName,
                    TotalShifts: totalShifts,
                    TotalAcceptedOrders: totalAccepted,
                    TotalRejectedOrders: totalRejected,
                    TotalRealRejectedOrders: totalRealRejected,
                    TotalWorkingHours: totalHours,
                    CompletedShifts: completedShifts,
                    IncompleteShifts: incompleteShifts,
                    FailedShifts: failedShifts,
                    CompletionRate: completionRate,
                    AverageOrdersPerShift: avgOrdersPerShift,
                    RejectionRate: rejectionRate,
                    PerformanceScore: performanceScore,
                        TotalStackedDeliveries: totalStacked, // ADD THIS
    AverageStackedPerShift: avgStackedPerShift,
                    TotalPenalty: totalPenalty,
                    ProblematicShiftsCount: problematicCount,
                    Rank: 0, // Will be assigned after sorting
                    PerformanceGrade: grade,
                    Achievements: achievements,
                    IsSubstitutionActive: hasSubstitution,
                    OriginalWorkingId: originalWorkingId
                ));
            }

            // Sort by requested criteria
            riderDetails = SortRiderDetails(riderDetails, request.SortBy);

            // Assign ranks
            for (int i = 0; i < riderDetails.Count; i++)
            {
                riderDetails[i] = riderDetails[i] with { Rank = i + 1 };
            }

            // Take top N
            var topRiders = riderDetails.Take(request.TopCount).ToList();

            // Calculate company breakdown
            var companyBreakdown = CalculateCompanyBreakdown(
                shifts, riderDetails, request.IncludeAllCompanies);

            var report = new TopRidersReport(
                StartDate: request.StartDate,
                EndDate: request.EndDate,
                TotalRiders: riderGroups.Count,
                TotalShifts: shifts.Count,
                TotalOrders: shifts.Sum(s => s.AcceptedDailyOrders),
                TopRiders: topRiders,
                CompanyBreakdown: companyBreakdown
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<TopRidersReport>(
                new Error($"Error generating top riders report: {ex.Message}", "server_error", 500));
        }
    }



    public async Task<Result<MonthlyStackedDeliveriesReport>> GetMonthlyStackedDeliveriesByWorkingIdAsync(
    string WorkingId,
    int year,
    int month,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<MonthlyStackedDeliveriesReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (month < 1 || month > 12)
            return Result.Failure<MonthlyStackedDeliveriesReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<MonthlyStackedDeliveriesReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var shifts = await _dbcontext.RiderShifts
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(new MonthlyStackedDeliveriesReport(
                RiderId: rider.Id,
                RiderName: rider.Employee.NameAR,
                WorkingId: WorkingId,
                Year: year,
                Month: month,
                TotalStackedDeliveries: 0,
                TotalShifts: 0,
                AverageStackedPerShift: 0,
                MaxStackedInDay: 0,
                MaxStackedDate: null,
                DailyBreakdown: new List<DailyStackedBreakdown>()
            ));
        }

        var totalStacked = shifts.Sum(s => s.StackedDeliveries);
        var totalShifts = shifts.Count;
        var avgStackedPerShift = totalShifts > 0 ? (decimal)totalStacked / totalShifts : 0;

        var maxStackedShift = shifts.OrderByDescending(s => s.StackedDeliveries).First();
        var maxStacked = maxStackedShift.StackedDeliveries;
        var maxStackedDate = maxStackedShift.ShiftDate;

        var dailyBreakdown = shifts.Select(s =>
        {
            var totalOrders = s.AcceptedDailyOrders;
            var stackedPercentage = totalOrders > 0
                ? (decimal)s.StackedDeliveries / totalOrders * 100
                : 0;

            return new DailyStackedBreakdown(
                Date: s.ShiftDate,
                StackedDeliveries: s.StackedDeliveries,
                AcceptedOrders: s.AcceptedDailyOrders,
                StackedPercentage: stackedPercentage
            );
        }).ToList();

        var report = new MonthlyStackedDeliveriesReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
            Year: year,
            Month: month,
            TotalStackedDeliveries: totalStacked,
            TotalShifts: totalShifts,
            AverageStackedPerShift: avgStackedPerShift,
            MaxStackedInDay: maxStacked,
            MaxStackedDate: maxStackedDate,
            DailyBreakdown: dailyBreakdown
        );

        return Result.Success(report);
    }

    public async Task<Result<AllRidersStackedDeliveriesReport>> GetAllRidersStackedDeliveriesAsync(
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (startDate > endDate)
            return Result.Failure<AllRidersStackedDeliveriesReport>(
                new Error("Start date must be before or equal to end date", "invalid_input", 400));

        // Get all riders with their shifts in the date range
        var ridersWithShifts = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .Where(r => r.RiderShifts.Any(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate))
            .Select(r => new
            {
                Rider = r,
                Shifts = r.RiderShifts
                    .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                    .OrderBy(s => s.ShiftDate)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        if (!ridersWithShifts.Any())
        {
            return Result.Success(new AllRidersStackedDeliveriesReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalRiders: 0,
                TotalStackedDeliveries: 0,
                TotalShifts: 0,
                AverageStackedPerRider: 0,
                RiderSummaries: new List<RiderStackedSummary>()
            ));
        }

        var riderSummaries = new List<RiderStackedSummary>();
        var grandTotalStacked = 0;
        var grandTotalShifts = 0;

        foreach (var item in ridersWithShifts)
        {
            var shifts = item.Shifts;
            var totalStacked = shifts.Sum(s => s.StackedDeliveries);
            var totalShifts = shifts.Count;
            var totalAcceptedOrders = shifts.Sum(s => s.AcceptedDailyOrders);

            var avgStackedPerShift = totalShifts > 0 ? (decimal)totalStacked / totalShifts : 0;

            var maxStackedShift = shifts.OrderByDescending(s => s.StackedDeliveries).FirstOrDefault();
            var maxStacked = maxStackedShift?.StackedDeliveries ?? 0;
            var maxStackedDate = maxStackedShift?.ShiftDate;

            var stackedPercentage = totalAcceptedOrders > 0
                ? (decimal)totalStacked / totalAcceptedOrders * 100
                : 0;

            riderSummaries.Add(new RiderStackedSummary(
                RiderId: item.Rider.Id,
                RiderName: item.Rider.Employee.NameAR,
                WorkingId: item.Rider.WorkingId ?? "0",
                TotalStackedDeliveries: totalStacked,
                TotalShifts: totalShifts,
                AverageStackedPerShift: avgStackedPerShift,
                MaxStackedInDay: maxStacked,
                MaxStackedDate: maxStackedDate,
                TotalStackedPercentage: stackedPercentage
            ));

            grandTotalStacked += totalStacked;
            grandTotalShifts += totalShifts;
        }

        // Sort by total stacked deliveries descending
        riderSummaries = riderSummaries
            .OrderByDescending(r => r.TotalStackedDeliveries)
            .ToList();

        var avgStackedPerRider = riderSummaries.Count > 0
            ? (decimal)grandTotalStacked / riderSummaries.Count
            : 0;

        var report = new AllRidersStackedDeliveriesReport(
            StartDate: startDate,
            EndDate: endDate,
            TotalRiders: riderSummaries.Count,
            TotalStackedDeliveries: grandTotalStacked,
            TotalShifts: grandTotalShifts,
            AverageStackedPerRider: avgStackedPerRider,
            RiderSummaries: riderSummaries
        );

        return Result.Success(report);
    }

    public async Task<Result<TopRidersReport>> GetTopRidersForMonthAsync(
        int year,
        int month,
        int topCount = 100,
        string? companyFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return Result.Failure<TopRidersReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var request = new TopRidersRequest(
            StartDate: startDate,
            EndDate: endDate,
            TopCount: topCount,
            CompanyFilter: companyFilter,
            SortBy: TopRidersSortBy.TotalOrders,
            IncludeAllCompanies: true,
            MinimumShifts: 0
        );

        return await GetTopRidersInPeriodAsync(request, cancellationToken);
    }


    public async Task<Result<TopRidersReport>> GetTopRidersForYearAsync(
        int year,
        int topCount = 100,
        string? companyFilter = null,
        CancellationToken cancellationToken = default)
    {
        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        var request = new TopRidersRequest(
            StartDate: startDate,
            EndDate: endDate,
            TopCount: topCount,
            CompanyFilter: companyFilter,
            SortBy: TopRidersSortBy.TotalOrders,
            IncludeAllCompanies: true,
            MinimumShifts: 5
        );

        return await GetTopRidersInPeriodAsync(request, cancellationToken);
    }

    public async Task<Result<Dictionary<string, List<TopRiderDetail>>>> GetTopRidersPerCompanyAsync(
        DateOnly startDate,
        DateOnly endDate,
        int topCountPerCompany = 100,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<Dictionary<string, List<TopRiderDetail>>>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            // Get all companies
            var companies = await _dbcontext.Companies
                .Select(c => c.Name)
                .Distinct()
                .ToListAsync(cancellationToken);

            var result = new Dictionary<string, List<TopRiderDetail>>();

            foreach (var company in companies)
            {
                var request = new TopRidersRequest(
                    StartDate: startDate,
                    EndDate: endDate,
                    TopCount: topCountPerCompany,
                    CompanyFilter: company,
                    SortBy: TopRidersSortBy.PerformanceScore,
                    IncludeAllCompanies: false,
                    MinimumShifts: 1
                );

                var companyReport = await GetTopRidersInPeriodAsync(request, cancellationToken);

                if (companyReport.IsSuccess && companyReport.Value.TopRiders.Any())
                {
                    result[company] = companyReport.Value.TopRiders;
                }
            }

            if (!result.Any())
            {
                return Result.Failure<Dictionary<string, List<TopRiderDetail>>>(
                    new Error("No data found for any company", "no_data", 404));
            }

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<Dictionary<string, List<TopRiderDetail>>>(
                new Error($"Error generating company rankings: {ex.Message}", "server_error", 500));
        }
    }


    private List<TopRiderDetail> SortRiderDetails(
        List<TopRiderDetail> riders,
        TopRidersSortBy sortBy)
    {
        return sortBy switch
        {
            TopRidersSortBy.TotalOrders => riders
                .OrderByDescending(r => r.TotalAcceptedOrders)
                .ThenByDescending(r => r.CompletionRate)
                .ToList(),

            TopRidersSortBy.CompletionRate => riders
                .OrderByDescending(r => r.CompletionRate)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            TopRidersSortBy.PerformanceScore => riders
                .OrderByDescending(r => r.PerformanceScore)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            TopRidersSortBy.AverageOrdersPerShift => riders
                .OrderByDescending(r => r.AverageOrdersPerShift)
                .ThenByDescending(r => r.CompletionRate)
                .ToList(),

            TopRidersSortBy.TotalShifts => riders
                .OrderByDescending(r => r.TotalShifts)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            TopRidersSortBy.WorkingHours => riders
                .OrderByDescending(r => r.TotalWorkingHours)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            _ => riders
                .OrderByDescending(r => r.TotalAcceptedOrders)
                .ToList()
        };
    }

    private string DeterminePerformanceGrade(decimal performanceScore)
    {
        return performanceScore switch
        {
            >= 95m => PerformanceGrade.Exceptional.ToString(),
            >= 85m => PerformanceGrade.Excellent.ToString(),
            >= 75m => PerformanceGrade.Good.ToString(),
            >= 65m => PerformanceGrade.Average.ToString(),
            >= 50m => PerformanceGrade.BelowAverage.ToString(),
            _ => PerformanceGrade.Poor.ToString()
        };
    }

    private List<string> GenerateRiderAchievements(
        int totalOrders,
        decimal avgOrdersPerShift,
        decimal completionRate,
        decimal rejectionRate,
        int totalShifts,
        decimal performanceScore, int totalStacked, // ADD THIS PARAMETER
    decimal avgStackedPerShift)
    {
        var achievements = new List<string>();

        // Order-based achievements
        if (totalOrders >= 1000)
            achievements.Add("🏆 1000+ Orders Club");
        else if (totalOrders >= 500)
            achievements.Add("⭐ 500+ Orders Milestone");
        else if (totalOrders >= 250)
            achievements.Add("✨ 250+ Orders Achievement");

        if (totalStacked >= 500)
            achievements.Add("📦 Stacking Master (500+)");
        else if (totalStacked >= 250)
            achievements.Add("📦 Stacking Expert (250+)");
        else if (totalStacked >= 100)
            achievements.Add("📦 Efficient Stacker (100+)");

        // Consistency achievements
        if (totalShifts >= 30 && completionRate >= 90m)
            achievements.Add("💎 Consistency Champion");
        else if (totalShifts >= 20 && completionRate >= 85m)
            achievements.Add("🎯 Reliable Performer");

        // Average performance
        if (avgOrdersPerShift >= 25m)
            achievements.Add("🚀 High Volume Expert");
        else if (avgOrdersPerShift >= 20m)
            achievements.Add("📈 Above Average Performer");

        // Low rejection rate
        if (rejectionRate <= 5m && totalOrders >= 100)
            achievements.Add("✅ Quality Master");
        else if (rejectionRate <= 10m && totalOrders >= 100)
            achievements.Add("👍 Quality Focused");

        // Overall performance
        if (performanceScore >= 95m)
            achievements.Add("🌟 Exceptional Rating");
        else if (performanceScore >= 85m)
            achievements.Add("⚡ Excellent Rating");

        // Perfect month
        if (completionRate == 100m && totalShifts >= 15)
            achievements.Add("💯 Perfect Record");

        return achievements;
    }

    private CompanyBreakdownSummary CalculateCompanyBreakdown(
        List<RiderShift> allShifts,
        List<TopRiderDetail> allRiderDetails,
        bool includeAll)
    {
        var companyGroups = allShifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var companySummaries = new List<CompanyTopRiders>();

        foreach (var companyGroup in companyGroups)
        {
            var companyName = companyGroup.Key;
            var companyShifts = companyGroup.ToList();
            var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);

            // Get riders for this company
            var companyRiders = allRiderDetails
                .Where(r => r.CompanyName == companyName)
                .OrderByDescending(r => r.PerformanceScore)
                .ToList();

            if (!companyRiders.Any()) continue;

            var topPerformer = companyRiders.First();
            var topPerformersCount = companyRiders.Count(r => r.PerformanceScore >= 85m);

            var totalOrders = companyShifts.Sum(s => s.AcceptedDailyOrders);
            var expectedOrders = companyShifts.Count * dailyTarget;
            var companyScore = expectedOrders > 0
                ? (decimal)totalOrders / expectedOrders * 100
                : 0;

            companySummaries.Add(new CompanyTopRiders(
                CompanyName: companyName,
                DailyOrderTarget: dailyTarget,
                TotalRiders: companyRiders.Count,
                TotalShifts: companyShifts.Count,
                TotalOrders: totalOrders,
                CompanyPerformanceScore: companyScore,
                TopPerformer: topPerformer,
                TopPerformersCount: topPerformersCount
            ));
        }

        return new CompanyBreakdownSummary(
            CompaniesSummary: companySummaries
                .OrderByDescending(c => c.CompanyPerformanceScore)
                .ToList()
        );
    }



    private List<string> GenerateHousingInsights(
        HousingPeriodBreakdown period1,
        HousingPeriodBreakdown period2,
        HousingComparisonMetrics metrics)
    {
        var insights = new List<string>();

        // Orders change
        if (Math.Abs(metrics.DailyOrdersChangePercent) >= 15)
        {
            var emoji = metrics.DailyOrdersChangePercent > 0 ? "📈" : "📉";
            var direction = metrics.DailyOrdersChangePercent > 0 ? "increased" : "decreased";
            insights.Add($"{emoji} Orders {direction} by {Math.Abs(metrics.DailyOrdersChangePercent):F1}% " +
                        $"from {period1.DailyOrdersCount} to {period2.DailyOrdersCount}");
        }

        // Completion rate change
        if (Math.Abs(metrics.CompletionRateDifference) >= 5)
        {
            var emoji = metrics.CompletionRateDifference > 0 ? "✅" : "❌";
            var direction = metrics.CompletionRateDifference > 0 ? "improved" : "declined";
            insights.Add($"{emoji} Completion rate {direction} from {period1.CompletionRate:F1}% to {period2.CompletionRate:F1}%");
        }

        // Rider count change
        if (metrics.RiderCountDifference != 0)
        {
            var direction = metrics.RiderCountDifference > 0 ? "increased" : "decreased";
            insights.Add($"👥 Rider count {direction} from {period1.RiderCount} to {period2.RiderCount}");
        }

        // Rejection rate change
        if (Math.Abs(metrics.RejectionRateChangePercent) >= 10)
        {
            var emoji = metrics.RejectionRateChangePercent < 0 ? "🎯" : "⚠️";
            var direction = metrics.RejectionRateChangePercent < 0 ? "improved" : "increased";
            insights.Add($"{emoji} Rejection rate {direction} by {Math.Abs(metrics.RejectionRateChangePercent):F1}%");
        }

        // Efficiency change
        var avgChange = period2.AverageOrdersPerRider - period1.AverageOrdersPerRider;
        if (Math.Abs(avgChange) >= 2)
        {
            var emoji = avgChange > 0 ? "🚀" : "⚠️";
            var status = avgChange > 0 ? "more efficient" : "less efficient";
            insights.Add($"{emoji} Riders becoming {status}: avg orders per rider " +
                        $"from {period1.AverageOrdersPerRider:F1} to {period2.AverageOrdersPerRider:F1}");
        }

        if (!insights.Any())
        {
            insights.Add("✨ Performance remained relatively stable between periods");
        }

        return insights;
    }



    private List<CompanyPeriodBreakdown> CalculateCompanyBreakdowns(List<RiderShift> shifts)
    {
        var companyGroups = shifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var breakdowns = new List<CompanyPeriodBreakdown>();

        foreach (var companyGroup in companyGroups)
        {
            var companyName = companyGroup.Key;
            var companyShifts = companyGroup.ToList();
            var companyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
            var companyWorkingDays = companyShifts.Count;
            var companyExpected = companyWorkingDays * companyTarget;
            var companyAccepted = companyShifts.Sum(s => s.AcceptedDailyOrders);
            var companyPenalty = companyShifts.Sum(s => CalculatePenalty(s));
            var companyProblematic = companyShifts.Count(s => HasRejectionProblem(s));
            var companyStacked = companyShifts.Sum(s => s.StackedDeliveries); // ADD THIS

            var performanceScore = companyExpected > 0
                ? (decimal)companyAccepted / companyExpected * 100
                : 0;

            var avgStackedPerShift = companyWorkingDays > 0
         ? (decimal)companyStacked / companyWorkingDays
         : 0; // A

            breakdowns.Add(new CompanyPeriodBreakdown(
                CompanyName: companyName,
                DailyOrderTarget: companyTarget,
                WorkingDays: companyWorkingDays,
                CompletedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalAcceptedOrders: companyAccepted,
                TotalRejectedOrders: companyShifts.Sum(s => s.RejectedDailyOrders),
                TotalRealRejectedOrders: companyShifts.Sum(s => s.RealRejectedDailyOrders),
                TotalWorkingHours: companyShifts.Sum(s => s.WorkingHours),
                ProblematicShiftsCount: companyProblematic,
                PenaltyAmount: companyPenalty,
                            TotalStackedDeliveries: companyStacked, // ADD THIS
                            AverageStackedPerShift: avgStackedPerShift, // ADD THIS

                PerformanceScore: performanceScore,
                ExpectedOrders: companyExpected
            ));
        }

        return breakdowns.OrderByDescending(b => b.PerformanceScore).ToList();
    }

    private List<YearlyCompanyBreakdown> CalculateYearlyCompanyBreakdowns(List<RiderShift> shifts)
    {
        var companyGroups = shifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var breakdowns = new List<YearlyCompanyBreakdown>();

        foreach (var companyGroup in companyGroups)
        {
            var companyName = companyGroup.Key;
            var companyShifts = companyGroup.ToList();
            var monthlyData = companyShifts
                .GroupBy(s => s.ShiftDate.Month)
                .Select(monthGroup => new MonthlyCompanyData(
                    Month: monthGroup.Key,
                    WorkingDays: monthGroup.Count(),
                    AcceptedOrders: monthGroup.Sum(s => s.AcceptedDailyOrders),
                    RejectedOrders: monthGroup.Sum(s => s.RejectedDailyOrders)
                ))
                .OrderBy(m => m.Month)
                .ToList();

            var companyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
            var totalWorkingDays = companyShifts.Count;
            var expectedOrders = totalWorkingDays * companyTarget;
            var totalAccepted = companyShifts.Sum(s => s.AcceptedDailyOrders);

            var performanceScore = expectedOrders > 0
                ? (decimal)totalAccepted / expectedOrders * 100
                : 0;

            breakdowns.Add(new YearlyCompanyBreakdown(
                CompanyName: companyName,
                DailyOrderTarget: companyTarget,
                TotalWorkingDays: totalWorkingDays,
                TotalAcceptedOrders: totalAccepted,
                TotalRejectedOrders: companyShifts.Sum(s => s.RejectedDailyOrders),
                AveragePerformanceScore: performanceScore,
                MonthlyDetails: monthlyData
            ));
        }

        return breakdowns.OrderByDescending(b => b.AveragePerformanceScore).ToList();
    }

    private List<MonthlyBreakdown> CalculateMonthlyBreakdowns(List<RiderShift> shifts)
    {
        return shifts
            .GroupBy(s => s.ShiftDate.Month)
            .Select(monthGroup =>
            {
                var monthShifts = monthGroup.ToList();
                var companyBreakdowns = CalculateCompanyBreakdowns(monthShifts);
                var totalWorkingDays = monthShifts.Count;

                var performanceScore = companyBreakdowns.Any() && totalWorkingDays > 0
                    ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / totalWorkingDays
                    : 0;

                return new MonthlyBreakdown(
                    Month: monthGroup.Key,
                    WorkingDays: totalWorkingDays,
                    CompletedShifts: monthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                    TotalAcceptedOrders: monthShifts.Sum(s => s.AcceptedDailyOrders),
                    TotalRejectedOrders: monthShifts.Sum(s => s.RejectedDailyOrders),
                    PerformanceScore: performanceScore,
                    CompanyBreakdowns: companyBreakdowns
                );
            })
            .OrderBy(m => m.Month)
            .ToList();
    }

    private List<WorkingIdPeriod> DetectWorkingIdChanges(List<RiderShift> shifts)
    {
        if (!shifts.Any())
            return new List<WorkingIdPeriod>();

        var periods = new List<WorkingIdPeriod>();
        var currentWorkingId = shifts[0].WorkingId;
        var periodStart = shifts[0].ShiftDate;
        var shiftCount = 0;
        DateOnly? lastDate = null;

        foreach (var shift in shifts)
        {
            if (shift.WorkingId != currentWorkingId)
            {
                periods.Add(new WorkingIdPeriod(
                    WorkingId: currentWorkingId,
                    StartDate: periodStart,
                    EndDate: lastDate ?? periodStart,
                    ShiftCount: shiftCount
                ));

                currentWorkingId = shift.WorkingId;
                periodStart = shift.ShiftDate;
                shiftCount = 1;
            }
            else
            {
                shiftCount++;
            }
            lastDate = shift.ShiftDate;
        }

        periods.Add(new WorkingIdPeriod(
            WorkingId: currentWorkingId,
            StartDate: periodStart,
            EndDate: lastDate ?? periodStart,
            ShiftCount: shiftCount
        ));

        return periods;
    }


    private bool HasRejectionProblem(RiderShift shift)
    {
        return shift.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold;
    }

    private decimal CalculateRiderPerformanceScore(List<RiderShift> shifts, int dailyTarget)
    {
        var totalDays = shifts.Count;
        var expectedOrders = totalDays * dailyTarget;
        var actualOrders = shifts.Sum(s => s.AcceptedDailyOrders);

        return expectedOrders > 0
            ? (decimal)actualOrders / expectedOrders * 100
            : 0;
    }

    private ProblemShiftDetail CreateProblemShiftDetail(RiderShift shift)
    {
        var problems = new List<string>();

        if (shift.ShiftStatus != ShiftStatus.Completed.ToString())
            problems.Add($"Status: {shift.ShiftStatus}");

        if (HasRejectionProblem(shift))
        {
            var excess = shift.RealRejectedDailyOrders - CompanyShiftConfiguration.RejectionThreshold;
            problems.Add($"Excess rejections: {excess} (Total: {shift.RealRejectedDailyOrders})");
        }

        return new ProblemShiftDetail(
            RiderId: shift.RiderId,
            RiderName: shift.Rider?.Employee.NameAR ?? "Unknown",
            WorkingId: shift.WorkingId,
            ShiftDate: shift.ShiftDate,
            CompanyName: shift.Company?.Name ?? "Unknown",
            AcceptedOrders: shift.AcceptedDailyOrders,
            RejectedOrders: shift.RejectedDailyOrders,
            RealRejectedOrders: shift.RealRejectedDailyOrders,
            Status: shift.ShiftStatus,
            PenaltyAmount: CalculatePenalty(shift),
            ProblemDescription: string.Join(", ", problems)
        );
    }

    private MonthlyRiderReport CreateEmptyMonthlyReport(
        int riderId, string riderName, string WorkingId, int year, int month)
    {
        return new MonthlyRiderReport(
            RiderId: riderId,
            RiderName: riderName,
            WorkingId: WorkingId,
            Year: year,
            Month: month,
            TotalWorkingDays: 0,
            CompletedShifts: 0,
            IncompleteShifts: 0,
            FailedShifts: 0,
            TotalAcceptedOrders: 0,
            TotalRejectedOrders: 0,
            TotalRealRejectedOrders: 0,
            TotalWorkingHours: 0,
            ProblematicShiftsCount: 0,
            TotalPenaltyAmount: 0,
            OverallPerformanceScore: 0,
            CompanyBreakdowns: new List<CompanyPeriodBreakdown>(),
            ProblematicShifts: new List<ProblemShiftDetail>(),
            WorkingIdHistory: new List<WorkingIdPeriod>()
        );
    }

    private YearlyRiderReport CreateEmptyYearlyReport(
        int riderId, string riderName, string WorkingId, int year)
    {
        return new YearlyRiderReport(
            RiderId: riderId,
            RiderName: riderName,
            WorkingId: WorkingId,
            Year: year,
            TotalWorkingDays: 0,
            CompletedShifts: 0,
            IncompleteShifts: 0,
            FailedShifts: 0,
            TotalAcceptedOrders: 0,
            TotalRejectedOrders: 0,
            TotalRealRejectedOrders: 0,
            TotalWorkingHours: 0,
            ProblematicShiftsCount: 0,
            TotalPenaltyAmount: 0,
            AveragePerformanceScore: 0,
            YearlyCompanyBreakdowns: new List<YearlyCompanyBreakdown>(),
            MonthlyBreakdowns: new List<MonthlyBreakdown>(),
            WorkingIdHistory: new List<WorkingIdPeriod>()
        );
    }

    private DateRangeReport CreateEmptyDateRangeReport(
        int riderId, long IqamaNo, string riderName, string WorkingId, DateOnly startDate, DateOnly endDate)
    {
        return new DateRangeReport(
            RiderId: riderId,
            IqamaNo: IqamaNo,
            RiderName: riderName,
            WorkingId: WorkingId,
            StartDate: startDate,
            EndDate: endDate,
            TotalWorkingDays: 0,
            CompletedShifts: 0,
            IncompleteShifts: 0,
            FailedShifts: 0,
            TotalAcceptedOrders: 0,
            TotalRejectedOrders: 0,
            TotalRealRejectedOrders: 0,
            TotalWorkingHours: 0,
            ProblematicShiftsCount: 0,
            TotalPenaltyAmount: 0,
            OverallPerformanceScore: 0,
            CompanyBreakdowns: new List<CompanyPeriodBreakdown>(),
            ProblematicShifts: new List<ProblemShiftDetail>(),
            WorkingIdHistory: new List<WorkingIdPeriod>()
        );
    }
}
