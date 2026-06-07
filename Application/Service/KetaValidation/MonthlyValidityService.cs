using Application.Abstraction;
using Application.Service.KetaValidation;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Keeta;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static Application.Service.KetaValidation.IMonthlyValidityService;

namespace Application.Service.MonthlyValidity;

public class MonthlyValidityService(ApplicationDbcontext db) : IMonthlyValidityService
{
    private readonly ApplicationDbcontext _db = db;

    private static readonly Dictionary<int, string> MonthNames = new()
    {
        { 1,  "يناير"  }, { 2,  "فبراير" }, { 3,  "مارس"   },
        { 4,  "أبريل"  }, { 5,  "مايو"   }, { 6,  "يونيو"  },
        { 7,  "يوليو"  }, { 8,  "أغسطس"  }, { 9,  "سبتمبر" },
        { 10, "أكتوبر" }, { 11, "نوفمبر" }, { 12, "ديسمبر" }
    };

    // ============================================================
    //  KEETA DRIVER SHIFT IMPORT  (optimised for 4 000+ rows)
    // ============================================================

    #region Keeta Driver Shift Import

    /// <summary>
    /// Imports a Keeta driver-shift Excel file in four phases so that the
    /// number of database round-trips is constant regardless of row count:
    ///
    ///   Phase 1 – Parse the entire worksheet in memory   (0 DB calls)
    ///   Phase 2 – Load the rider lookup                  (2 DB calls)
    ///   Phase 3 – Pre-load all existing shift records    (1 DB call)
    ///   Phase 4 – Resolve every row in memory            (0 DB calls)
    ///   Phase 5 – Bulk write: RemoveRange + AddRange +
    ///             single SaveChangesAsync                 (1 DB call)
    ///
    /// Total DB round-trips: O(1) instead of O(n).
    /// </summary>
    public async Task<Result<KeetaShiftImportResponse>> ImportKeetaDriverShiftsAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<KeetaShiftImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<KeetaShiftImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<KeetaShiftRowResult>();
        var errors = new List<string>();
        int driversFound = 0, driversNotFound = 0;
        int shiftsCreated = 0, shiftsUpdated = 0;
        int notInShift = 0, noQualifiedSlots = 0, errorRows = 0;
        DateOnly? earliestDate = null, latestDate = null;

        try
        {
            Console.WriteLine($"[KeetaShiftImport] Starting: {file.FileName}");

            // ══════════════════════════════════════════════════════════════
            //  PHASE 1 — Open & parse the entire worksheet in memory
            //  No DB access in this phase.
            // ══════════════════════════════════════════════════════════════

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            var headerRow = FindKeetaShiftHeaderRow(worksheet);
            if (headerRow == null)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            Console.WriteLine($"[KeetaShiftImport] Header at row {headerRow.RowNumber()}");

            var colMap = BuildKeetaShiftColumnMapping(headerRow);
            if (!colMap.IsValid)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("InvalidColumns", colMap.ErrorMessage!, 400));

            Console.WriteLine("[KeetaShiftImport] Column mapping OK — " +
                $"Date={colMap.DateCol} DriverId={colMap.DriverIdCol} " +
                $"Summary={colMap.ShiftSummaryCol} InShift={colMap.IsInShiftCol} " +
                $"ConnTime={colMap.ConnectionTimeCol} Tasks={colMap.TasksDeliveredCol}");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            int totalRows = dataRows.Count;
            if (totalRows == 0)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            Console.WriteLine($"[KeetaShiftImport] Data rows: {totalRows}");
            progressCallback?.Invoke(0, totalRows);

            // Parse every row into a lightweight struct — purely in memory.
            var parsedRows = new List<(int RowNum, KeetaShiftRowData Data)>(totalRows);
            foreach (var row in dataRows)
            {
                int rn = row.RowNumber();
                parsedRows.Add((rn, ParseKeetaShiftRow(row, colMap, rn)));
            }

            int validCount = parsedRows.Count(p => p.Data.IsValid);
            Console.WriteLine($"[KeetaShiftImport] Parsing complete — valid={validCount}/{totalRows}");
            progressCallback?.Invoke(totalRows / 4, totalRows); // ~25 %

            // ══════════════════════════════════════════════════════════════
            //  PHASE 2 — Load rider lookup  (2 DB queries total)
            // ══════════════════════════════════════════════════════════════

            var riderLookup = await BuildRiderLookupAsync();
            Console.WriteLine($"[KeetaShiftImport] Rider lookup loaded: {riderLookup.Count} entries");

            // ══════════════════════════════════════════════════════════════
            //  PHASE 3 — Pre-load all existing shift records  (1 DB query)
            //
            //  Key: (PlatformDriverId.ToUpperInvariant(), ReportDate)
            //  We load every record whose ReportDate falls inside the file's
            //  date range so we can do O(1) create-vs-update decisions.
            // ══════════════════════════════════════════════════════════════

            var validRows = parsedRows
                .Where(p => p.Data.IsValid && p.Data.ReportDate.HasValue)
                .ToList();

            // existingShifts key = (upperDriverId, date)
            var existingShifts = new Dictionary<(string, DateOnly), KeetaDriverShift>();

            if (validRows.Count > 0)
            {
                var minDate = validRows.Min(p => p.Data.ReportDate!.Value);
                var maxDate = validRows.Max(p => p.Data.ReportDate!.Value);

                Console.WriteLine($"[KeetaShiftImport] Pre-loading existing shifts: {minDate} – {maxDate}");

                // Single query: all shifts in the date window, with their slots.
                var dbShifts = await _db.KeetaDriverShifts
                    .Include(k => k.ShiftSlots)
                    .Where(k => k.ReportDate >= minDate && k.ReportDate <= maxDate)
                    .ToListAsync();

                foreach (var s in dbShifts)
                    existingShifts[(s.PlatformDriverId.Trim().ToUpperInvariant(), s.ReportDate)] = s;

                Console.WriteLine($"[KeetaShiftImport] Existing records loaded: {dbShifts.Count}");
            }

            progressCallback?.Invoke(totalRows / 2, totalRows); // ~50 %

            // ══════════════════════════════════════════════════════════════
            //  PHASE 4 — Resolve every row in memory
            //  All decision-making happens here. No DB access.
            // ══════════════════════════════════════════════════════════════

            var now = DateTime.UtcNow.AddHours(3);
            var shiftsToAdd = new List<KeetaDriverShift>();
            var slotsToDelete = new List<KeetaShiftSlot>();

            foreach (var (rowNum, rd) in parsedRows)
            {
                // ── Invalid / unparseable row ─────────────────────────────
                if (!rd.IsValid)
                {
                    errorRows++;
                    errors.Add($"Row {rowNum}: {rd.ErrorMessage}");
                    results.Add(new KeetaShiftRowResult(
                        rowNum, rd.PlatformDriverId ?? "N/A",
                        rd.ReportDate ?? DateOnly.MinValue,
                        false, null, null, false, 0, 0, 0,
                        [], KeetaImportAction.Error, rd.ErrorMessage));
                    continue;
                }

                // ── Date range tracking ───────────────────────────────────
                if (!earliestDate.HasValue || rd.ReportDate!.Value < earliestDate.Value)
                    earliestDate = rd.ReportDate;
                if (!latestDate.HasValue || rd.ReportDate!.Value > latestDate.Value)
                    latestDate = rd.ReportDate;

                // ── Rider resolution (dictionary O(1)) ────────────────────
                var lookupKey = !string.IsNullOrWhiteSpace(rd.WorkingId)
                    ? rd.WorkingId!.Trim()
                    : rd.PlatformDriverId!.Trim();

                int? riderId = null;
                string? resolvedWorkingId = null;

                if (riderLookup.TryGetValue(lookupKey, out var riderInfo))
                {
                    riderId = riderInfo.Id;
                    resolvedWorkingId = riderInfo.WorkingId;
                    driversFound++;
                }
                else
                {
                    driversNotFound++;
                }

                if (!rd.IsInShift) notInShift++;

                var qualifiedSlots = ParseQualifiedKeetaSlots(rd.RawShiftSummary);
                if (rd.IsInShift && qualifiedSlots.Count == 0) noQualifiedSlots++;

                // ── Create or update (dictionary O(1)) ────────────────────
                var dictKey = (rd.PlatformDriverId!.Trim().ToUpperInvariant(), rd.ReportDate!.Value);
                KeetaImportAction action;

                if (existingShifts.TryGetValue(dictKey, out var existing))
                {
                    // ── Update existing record ────────────────────────────
                    existing.WorkingId = resolvedWorkingId ?? existing.WorkingId;
                    existing.RiderId = riderId ?? existing.RiderId;
                    existing.Supervisor = rd.Supervisor;
                    existing.IsInShift = rd.IsInShift;
                    existing.TotalConnectionTimeRaw = rd.ConnectionTimeRaw;
                    existing.TotalConnectionMinutes = rd.ConnectionMinutes;
                    existing.TasksDelivered = rd.TasksDelivered;
                    existing.RawShiftSummary = rd.RawShiftSummary;
                    existing.QualifiedSlotsCount = qualifiedSlots.Count;
                    existing.UpdatedAt = now;

                    // Collect old slots for bulk deletion; replace with new ones.
                    slotsToDelete.AddRange(existing.ShiftSlots);
                    existing.ShiftSlots.Clear();
                    foreach (var s in qualifiedSlots)
                        existing.ShiftSlots.Add(MapSlot(s));

                    shiftsUpdated++;
                    action = riderId.HasValue
                        ? KeetaImportAction.Updated
                        : (rd.IsInShift ? KeetaImportAction.DriverNotFound : KeetaImportAction.NotInShift);
                }
                else
                {
                    // ── Create new record ─────────────────────────────────
                    var newShift = new KeetaDriverShift
                    {
                        ReportDate = rd.ReportDate!.Value,
                        PlatformDriverId = rd.PlatformDriverId!,
                        WorkingId = resolvedWorkingId,
                        RiderId = riderId,
                        Supervisor = rd.Supervisor,
                        IsInShift = rd.IsInShift,
                        TotalConnectionTimeRaw = rd.ConnectionTimeRaw,
                        TotalConnectionMinutes = rd.ConnectionMinutes,
                        TasksDelivered = rd.TasksDelivered,
                        RawShiftSummary = rd.RawShiftSummary,
                        QualifiedSlotsCount = qualifiedSlots.Count,
                        ImportedBy = uploadedBy,
                        CreatedAt = now,
                    };

                    foreach (var s in qualifiedSlots)
                        newShift.ShiftSlots.Add(MapSlot(s));

                    shiftsToAdd.Add(newShift);

                    // Register immediately so a duplicate row later in the
                    // same file results in an update, not a second insert.
                    existingShifts[dictKey] = newShift;

                    shiftsCreated++;
                    action = riderId.HasValue
                        ? KeetaImportAction.Created
                        : (rd.IsInShift ? KeetaImportAction.DriverNotFound : KeetaImportAction.NotInShift);
                }

                results.Add(new KeetaShiftRowResult(
                    rowNum,
                    rd.PlatformDriverId!,
                    rd.ReportDate!.Value,
                    riderId.HasValue,
                    resolvedWorkingId,
                    riderId,
                    rd.IsInShift,
                    rd.TasksDelivered,
                    rd.ConnectionMinutes,
                    qualifiedSlots.Count,
                    qualifiedSlots.Select(s => new KeetaSlotDetail(
                        s.SlotKey, s.DurationRaw, s.DurationMinutes, s.SlotOrder)).ToList(),
                    action,
                    null));
            }

            Console.WriteLine(
                $"[KeetaShiftImport] Resolution done — " +
                $"New={shiftsToAdd.Count} Updated={shiftsUpdated} " +
                $"SlotsToDelete={slotsToDelete.Count}");

            progressCallback?.Invoke(3 * totalRows / 4, totalRows); // ~75 %

            // ══════════════════════════════════════════════════════════════
            //  PHASE 5 — Bulk DB write  (1 SaveChangesAsync)
            //
            //  - RemoveRange: marks all stale slots deleted in one call.
            //  - AddRange:    queues all new shifts (EF Core auto-batches
            //                 the SQL INSERTs, default ~42 rows per batch).
            //  - SaveChangesAsync: one transaction, one round-trip envelope.
            // ══════════════════════════════════════════════════════════════

            if (slotsToDelete.Count > 0)
                _db.KeetaShiftSlots.RemoveRange(slotsToDelete);

            if (shiftsToAdd.Count > 0)
                _db.KeetaDriverShifts.AddRange(shiftsToAdd);

            await _db.SaveChangesAsync();

            progressCallback?.Invoke(totalRows, totalRows);

            Console.WriteLine(
                $"[KeetaShiftImport] Done — Created={shiftsCreated} Updated={shiftsUpdated} " +
                $"NotFound={driversNotFound} NotInShift={notInShift} " +
                $"NoSlots={noQualifiedSlots} Errors={errorRows}");

            return Result.Success(new KeetaShiftImportResponse(
                TotalRowsInExcel: totalRows,
                EarliestDate: earliestDate,
                LatestDate: latestDate,
                DriversFound: driversFound,
                DriversNotFound: driversNotFound,
                ShiftsCreated: shiftsCreated,
                ShiftsUpdated: shiftsUpdated,
                NotInShift: notInShift,
                NoQualifiedSlots: noQualifiedSlots,
                ErrorRows: errorRows,
                Results: results,
                ProcessingErrors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KeetaShiftImport] FATAL: {ex}");
            return Result.Failure<KeetaShiftImportResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the WorkingId → (RiderId, WorkingId) lookup used during import.
    /// Queries RiderDetails first, then fills gaps from RiderWorkingIdHistory.
    /// Exactly 2 DB queries total.
    /// </summary>
    private async Task<Dictionary<string, (int Id, string WorkingId)>> BuildRiderLookupAsync()
    {
        var lookup = new Dictionary<string, (int Id, string WorkingId)>(
            StringComparer.OrdinalIgnoreCase);

        // Primary: current WorkingId on RiderDetails
        var riders = await _db.RiderDetails
            .Where(r => !string.IsNullOrEmpty(r.WorkingId))
            .Select(r => new { r.Id, r.WorkingId })
            .AsNoTracking()
            .ToListAsync();

        foreach (var r in riders)
            lookup[r.WorkingId!.Trim()] = (r.Id, r.WorkingId!);

        // Fallback: historical WorkingIds (transferred / renamed riders)
        var history = await _db.RiderWorkingIdHistories
            .Where(h => !string.IsNullOrEmpty(h.WorkingId))
            .Include(h => h.Employee)
                .ThenInclude(e => e.RiderDetails)
            .Where(h => h.Employee.RiderDetails != null)
            .Select(h => new { RiderId = h.Employee.RiderDetails!.Id, h.WorkingId })
            .AsNoTracking()
            .ToListAsync();

        foreach (var h in history)
        {
            var k = h.WorkingId.Trim();
            if (!lookup.ContainsKey(k))
                lookup[k] = (h.RiderId, h.WorkingId);
        }

        return lookup;
    }

    // ── Header row finder ─────────────────────────────────────────────────────

    private IXLRow? FindKeetaShiftHeaderRow(IXLWorksheet worksheet)
    {
        var known = new[]
        {
            "التاريخ", "Date",
            "معرّف السائق", "معرف السائق", "driverId", "Driver ID",
            "WorkingId", "Working ID", "معرف العمل",
            "المشرف", "Supervisor",
            "ملخص الاتصال", "Shift Summary",
            "هل أنت في الوردية", "IsInShift", "In Shift",
            "وقت اتصال", "Connection Time",
            "المهام", "Tasks Delivered", "Tasks"
        };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cells = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int hits = cells.Count(cv =>
                known.Any(k =>
                    cv.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(k.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                    cv.Contains(k, StringComparison.OrdinalIgnoreCase)));

            if (hits >= 3) return row;
        }

        return worksheet.RowsUsed()
            .Take(5)
            .OrderByDescending(r => r.CellsUsed().Count())
            .FirstOrDefault();
    }

    // ── Column mapping ────────────────────────────────────────────────────────

    private int FindColumn(List<IXLCell> cells, params string[] possibleNames)
    {
        foreach (var cell in cells)
        {
            try
            {
                if (cell.IsEmpty()) continue;

                string headerValue = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString().Trim()
                    : cell.DataType switch
                    {
                        XLDataType.Text => cell.GetText().Trim(),
                        XLDataType.Number => cell.GetDouble().ToString().Trim(),
                        XLDataType.Boolean => cell.GetBoolean().ToString().Trim(),
                        _ => cell.GetString().Trim()
                    };

                if (string.IsNullOrWhiteSpace(headerValue)) continue;

                foreach (var name in possibleNames)
                    if (headerValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;

                string headerNoSpaces = headerValue.Replace(" ", "").Replace("\t", "")
                                                   .Replace("\n", "").Replace("\r", "");
                foreach (var name in possibleNames)
                {
                    string nameNoSpaces = name.Replace(" ", "").Replace("\t", "")
                                             .Replace("\n", "").Replace("\r", "");
                    if (headerNoSpaces.Equals(nameNoSpaces, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }

                foreach (var name in possibleNames)
                    if (headerValue.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
            }
            catch { continue; }
        }

        return 0;
    }

    private KeetaShiftColumnMapping BuildKeetaShiftColumnMapping(IXLRow headerRow)
    {
        var m = new KeetaShiftColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        m.DateCol = FindColumn(cells, "التاريخ", "Date", "تاريخ", "report_date");

        m.DriverIdCol = FindColumn(cells,
            "معرّد السائق", "معرّف السائق", "معرف السائق",
            "driverId", "Driver ID", "DriverId",
            "WorkingId", "Working ID", "معرف العمل", "رقم العمل",
            "driver_id", "working_id");

        m.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working ID", "معرف العمل", "رقم العمل");

        m.SupervisorCol = FindColumn(cells, "المشرف", "Supervisor", "supervisor");

        m.ShiftSummaryCol = FindColumn(cells,
            "ملخص الاتصال", "Shift Summary", "Shift Period",
            "فترة الوردية_ملخص الاتصال", "shift_summary",
            "شرح الوردية", "تفاصيل الوردية");

        m.IsInShiftCol = FindColumn(cells,
            "هل أنت في الوردية", "هل أنت في الوردية؟",
            "IsInShift", "Is In Shift", "In Shift", "Active",
            "فترة الوردية_هل أنت في الوردية؟", "in_shift");

        m.ConnectionTimeCol = FindColumn(cells,
            "وقت اتصال السائقين", "وقت الاتصال",
            "Connection Time", "ConnectionTime",
            "فترة الوردية_وقت اتصال السائقين عبر تطبيق السائق",
            "connection_time");

        m.TasksDeliveredCol = FindColumn(cells,
            "المهام التي تم تسليمها", "المهام", "Tasks Delivered", "Tasks",
            "أحجام المهام_المهام التي تم تسليمها",
            "tasks_delivered", "Deliveries");

        var missing = new List<string>();
        if (m.DateCol == 0) missing.Add("التاريخ / Date");
        if (m.DriverIdCol == 0) missing.Add("معرّف السائق / WorkingId / Driver ID");

        m.IsValid = !missing.Any();
        m.ErrorMessage = missing.Any()
            ? $"Required columns not found: {string.Join(", ", missing)}"
            : null;

        return m;
    }

    private string? GetCellValue(IXLRow row, int columnIndex)
    {
        if (columnIndex == 0) return null;

        try
        {
            var cell = row.Cell(columnIndex);
            if (cell.IsEmpty()) return null;

            if (cell.IsMerged()) cell = cell.MergedRange().FirstCell();

            if (cell.DataType == XLDataType.Number)
            {
                var numValue = cell.GetDouble();
                return numValue == Math.Floor(numValue)
                    ? ((long)numValue).ToString()
                    : numValue.ToString();
            }

            if (cell.DataType == XLDataType.DateTime)
            {
                try { return cell.GetDateTime().ToString("yyyy-MM-dd"); }
                catch { return cell.GetText().Trim(); }
            }

            if (cell.DataType == XLDataType.Text) return cell.GetText().Trim();
            if (cell.DataType == XLDataType.Boolean) return cell.GetBoolean().ToString();

            return cell.Value.ToString()?.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetCellValue] Error at column {columnIndex}: {ex.Message}");
            return null;
        }
    }

    // ── Row parser ────────────────────────────────────────────────────────────

    private KeetaShiftRowData ParseKeetaShiftRow(
        IXLRow row,
        KeetaShiftColumnMapping map,
        int rowNumber)
    {
        var d = new KeetaShiftRowData { RowNumber = rowNumber };

        try
        {
            var dateStr = GetCellValue(row, map.DateCol);
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                d.IsValid = false; d.ErrorMessage = "Date cell is empty"; return d;
            }
            if (!TryParseKeetaDate(dateStr.Trim(), out DateOnly reportDate))
            {
                d.IsValid = false; d.ErrorMessage = $"Unrecognised date: '{dateStr}'"; return d;
            }
            d.ReportDate = reportDate;

            d.PlatformDriverId = GetCellValue(row, map.DriverIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(d.PlatformDriverId))
            {
                d.IsValid = false; d.ErrorMessage = "Driver ID / Working ID cell is empty"; return d;
            }

            if (map.WorkingIdCol > 0)
            {
                var wid = GetCellValue(row, map.WorkingIdCol)?.Trim();
                if (!string.IsNullOrWhiteSpace(wid)) d.WorkingId = wid;
            }

            d.Supervisor = map.SupervisorCol > 0
                ? GetCellValue(row, map.SupervisorCol)?.Trim()
                : null;
            if (d.Supervisor is "No Supervisor" or "-") d.Supervisor = null;

            d.RawShiftSummary = map.ShiftSummaryCol > 0
                ? GetCellValue(row, map.ShiftSummaryCol)?.Trim()
                : null;

            var inShiftStr = (map.IsInShiftCol > 0
                ? GetCellValue(row, map.IsInShiftCol)
                : null)?.Trim() ?? string.Empty;

            d.IsInShift =
                inShiftStr.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                inShiftStr.Equals("نعم", StringComparison.OrdinalIgnoreCase) ||
                inShiftStr == "1" ||
                inShiftStr.Equals("True", StringComparison.OrdinalIgnoreCase);

            d.ConnectionTimeRaw = map.ConnectionTimeCol > 0
                ? GetCellValue(row, map.ConnectionTimeCol)?.Trim()
                : null;
            d.ConnectionMinutes = ParseArabicDurationToMinutes(d.ConnectionTimeRaw);

            var tasksStr = map.TasksDeliveredCol > 0
                ? GetCellValue(row, map.TasksDeliveredCol)
                : "0";
            int.TryParse(tasksStr, out int tasks);
            d.TasksDelivered = tasks;

            d.IsValid = true;
        }
        catch (Exception ex)
        {
            d.IsValid = false;
            d.ErrorMessage = $"Row parsing error: {ex.Message}";
        }

        return d;
    }

    // ── Keeta date parser ─────────────────────────────────────────────────────

    private static bool TryParseKeetaDate(string raw, out DateOnly result)
    {
        result = DateOnly.MinValue;

        if (raw.Length == 8 && raw.All(char.IsDigit))
        {
            if (int.TryParse(raw[..4], out int y) &&
                int.TryParse(raw[4..6], out int mo) &&
                int.TryParse(raw[6..8], out int dy))
            {
                try { result = new DateOnly(y, mo, dy); return true; }
                catch { }
            }
        }

        if (DateOnly.TryParse(raw, out result)) return true;

        foreach (var fmt in new[]
            { "dd/MM/yyyy", "dd-MM-yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "yyyy/MM/dd" })
        {
            if (DateTime.TryParseExact(raw, fmt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            {
                result = DateOnly.FromDateTime(dt);
                return true;
            }
        }

        return false;
    }

    // ── GetAll Keeta shifts ───────────────────────────────────────────────────

    public async Task<Result<AllKeetaShiftsResponse>> GetAllKeetaDriverShiftsAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        string? platformDriverId = null)
    {
        try
        {
            var query = _db.KeetaDriverShifts
                .Include(k => k.ShiftSlots)
                .Include(k => k.Rider)
                    .ThenInclude(r => r!.Employee)
                .Include(k => k.Rider)
                    .ThenInclude(r => r!.Company)
                .AsNoTracking();

            if (from.HasValue)
                query = query.Where(k => k.ReportDate >= from.Value);
            if (to.HasValue)
                query = query.Where(k => k.ReportDate <= to.Value);
            if (!string.IsNullOrWhiteSpace(platformDriverId))
                query = query.Where(k =>
                    k.PlatformDriverId == platformDriverId ||
                    k.WorkingId == platformDriverId);

            var allShifts = await query
                .OrderBy(k => k.PlatformDriverId)
                .ThenBy(k => k.ReportDate)
                .ToListAsync();

            if (allShifts.Count == 0)
                return Result.Success(new AllKeetaShiftsResponse(
                    TotalRiders: 0, TotalShiftRecords: 0,
                    EarliestDate: null, LatestDate: null,
                    Riders: [],
                    RetrievedAt: DateTime.UtcNow.AddHours(3)));

            var grouped = allShifts
                .GroupBy(k => k.PlatformDriverId)
                .Select(g =>
                {
                    var first = g.First();
                    var rider = first.Rider;

                    var days = g
                        .OrderBy(k => k.ReportDate)
                        .Select(k => new KeetaRiderDayDetail(
                            ReportDate: k.ReportDate,
                            IsInShift: k.IsInShift,
                            TasksDelivered: k.TasksDelivered,
                            ConnectionMinutes: k.TotalConnectionMinutes,
                            ConnectionTimeRaw: k.TotalConnectionTimeRaw,
                            QualifiedSlotsCount: k.QualifiedSlotsCount,
                            Slots: k.ShiftSlots
                                .OrderBy(s => s.StartTime)
                                .Select(s => new KeetaSlotDetail(
                                    s.SlotKey, s.DurationRaw, s.DurationMinutes, s.SlotOrder))
                                .ToList(),
                            CreatedAt: k.CreatedAt,
                            UpdatedAt: k.UpdatedAt))
                        .ToList();

                    return new KeetaRiderShiftSummary(
                        RiderId: first.RiderId,
                        PlatformDriverId: first.PlatformDriverId,
                        WorkingId: first.WorkingId,
                        RiderNameAR: rider?.Employee?.NameAR,
                        RiderNameEN: rider?.Employee?.NameEN,
                        CompanyName: rider?.Company?.Name,
                        Supervisor: first.Supervisor,
                        TotalDays: days.Count,
                        TotalInShiftDays: days.Count(d => d.IsInShift),
                        TotalTasksDelivered: days.Sum(d => d.TasksDelivered),
                        TotalConnectionMinutes: days.Sum(d => d.ConnectionMinutes),
                        Days: days);
                })
                .ToList();

            return Result.Success(new AllKeetaShiftsResponse(
                TotalRiders: grouped.Count,
                TotalShiftRecords: allShifts.Count,
                EarliestDate: allShifts.Min(k => k.ReportDate),
                LatestDate: allShifts.Max(k => k.ReportDate),
                Riders: grouped,
                RetrievedAt: DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            return Result.Failure<AllKeetaShiftsResponse>(
                new Error("RetrievalError",
                    $"Failed to retrieve Keeta shift data: {ex.Message}", 500));
        }
    }

    // ── Arabic duration → minutes ─────────────────────────────────────────────

    private static int ParseArabicDurationToMinutes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        int minutes = 0;
        var tokens = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (!int.TryParse(tokens[i], out int value)) continue;

            switch (tokens[i + 1])
            {
                case "س": minutes += value * 60; break;
                case "د": minutes += value; break;
                    // "ث" (seconds) intentionally skipped
            }
            i++;
        }

        return minutes;
    }

    // ── Qualified slot parser ─────────────────────────────────────────────────

    private static List<KeetaSlotData> ParseQualifiedKeetaSlots(string? rawSummary)
    {
        if (string.IsNullOrWhiteSpace(rawSummary)) return [];

        var qualified = new List<KeetaSlotData>();
        var segments = rawSummary.Split('|', StringSplitOptions.RemoveEmptyEntries);
        int slotOrder = 0;

        foreach (var seg in segments)
        {
            slotOrder++;
            var parts = seg.Trim().Split(',');
            if (parts.Length < 3) continue;

            string timeRange = parts[0].Trim();
            string shiftType = parts[1].Trim();
            string durationRaw = parts[2].Trim();

            bool isOnShift = shiftType.Equals("On-Shift", StringComparison.OrdinalIgnoreCase);
            bool isQualified = parts.Length >= 4 &&
                               parts[3].Trim().Equals("qualified", StringComparison.OrdinalIgnoreCase);

            if (!isOnShift || !isQualified) continue;

            var dash = timeRange.IndexOf('-');
            if (dash < 0) continue;

            var startStr = timeRange[..dash].Trim();
            var endStr = timeRange[(dash + 1)..].Trim();

            if (!TimeOnly.TryParse(startStr, out TimeOnly startTime) ||
                !TimeOnly.TryParse(endStr, out TimeOnly endTime))
                continue;

            qualified.Add(new KeetaSlotData
            {
                SlotKey = timeRange,
                StartTime = startTime,
                EndTime = endTime,
                IsOnShift = true,
                IsQualified = true,
                DurationRaw = durationRaw,
                DurationMinutes = ParseArabicDurationToMinutes(durationRaw),
                SlotOrder = slotOrder
            });
        }

        // Top-3 by duration, tie-break: earlier start; re-sort chronologically.
        return qualified
            .OrderByDescending(s => s.DurationMinutes)
            .ThenBy(s => s.StartTime)
            .Take(3)
            .OrderBy(s => s.StartTime)
            .ToList();
    }

    // ── Entity mapper ─────────────────────────────────────────────────────────

    private static KeetaShiftSlot MapSlot(KeetaSlotData s) => new()
    {
        SlotKey = s.SlotKey,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        IsOnShift = s.IsOnShift,
        IsQualified = s.IsQualified,
        DurationRaw = s.DurationRaw,
        DurationMinutes = s.DurationMinutes,
        SlotOrder = s.SlotOrder
    };

    // ════════════════════════════════════════════════════════════════════════
    //  INTERNAL CLASSES
    // ════════════════════════════════════════════════════════════════════════

    internal class KeetaShiftColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int DateCol { get; set; }
        public int DriverIdCol { get; set; }
        public int WorkingIdCol { get; set; }
        public int SupervisorCol { get; set; }
        public int ShiftSummaryCol { get; set; }
        public int IsInShiftCol { get; set; }
        public int ConnectionTimeCol { get; set; }
        public int TasksDeliveredCol { get; set; }
    }

    internal class KeetaShiftRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public DateOnly? ReportDate { get; set; }
        public string? PlatformDriverId { get; set; }
        public string? WorkingId { get; set; }
        public string? Supervisor { get; set; }
        public bool IsInShift { get; set; }
        public string? ConnectionTimeRaw { get; set; }
        public int ConnectionMinutes { get; set; }
        public int TasksDelivered { get; set; }
        public string? RawShiftSummary { get; set; }
    }

    internal class KeetaSlotData
    {
        public string SlotKey { get; set; } = string.Empty;
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public bool IsOnShift { get; set; }
        public bool IsQualified { get; set; }
        public string DurationRaw { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public int SlotOrder { get; set; }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────
    //  GET ALL RIDERS VALIDITY
    // ─────────────────────────────────────────────────────────────

    public async Task<Result<AllRidersValidityResponse>> GetAllRidersValidityAsync(int? year = null)
    {
        try
        {
            var validityQuery = _db.RiderMonthlyValidities.AsNoTracking();
            if (year.HasValue)
                validityQuery = validityQuery.Where(v => v.Year == year.Value);

            var validityRecords = await validityQuery.ToListAsync();

            var today = DateTime.Now;
            var availableYears = validityRecords.Select(v => v.Year).Distinct().OrderBy(y => y).ToList();

            var yearRanges = availableYears.ToDictionary(
                y => y,
                y =>
                {
                    int start = validityRecords.Where(v => v.Year == y).Min(v => v.Month);
                    int end = y == today.Year ? today.Month : 12;
                    return (Start: start, End: end);
                });

            var iqamasWithRecords = validityRecords.Select(v => v.EmployeeIqamaNo).Distinct().ToHashSet();

            var riders = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .Where(r => iqamasWithRecords.Contains(r.EmployeeIqamaNo))
                .AsNoTracking()
                .ToListAsync();

            var validityMap = validityRecords
                .ToDictionary(v => (v.EmployeeIqamaNo, v.Year, v.Month), v => v);

            var riderSummaries = riders.Select(rider =>
            {
                var monthDetails = new List<MonthValidityDetail>();

                foreach (var y in availableYears)
                {
                    var (start, end) = yearRanges[y];
                    for (int m = start; m <= end; m++)
                    {
                        validityMap.TryGetValue((rider.EmployeeIqamaNo, y, m), out var validity);
                        monthDetails.Add(BuildMonthDetail(y, m, validity));
                    }
                }

                return new RiderValiditySummary(
                    IqamaNo: rider.EmployeeIqamaNo,
                    NameAR: rider.Employee.NameAR,
                    NameEN: rider.Employee.NameEN,
                    WorkingId: rider.WorkingId,
                    CompanyName: rider.Company?.Name,
                    Months: monthDetails);
            }).ToList();

            int totalValid = validityRecords.Count(v => v.Status == ValidityStatus.Valid);
            int totalInvalid = validityRecords.Count(v => v.Status == ValidityStatus.Invalid);
            int totalFreelancer = validityRecords.Count(v => v.Status == ValidityStatus.Freelancer);
            int unclassified = riders.Count(r =>
                !validityRecords.Any(v => v.EmployeeIqamaNo == r.EmployeeIqamaNo));

            return Result.Success(new AllRidersValidityResponse(
                TotalRiders: riders.Count,
                TotalValidRecords: totalValid,
                TotalInvalidRecords: totalInvalid,
                TotalFreelancerRecords: totalFreelancer,
                TotalUnclassifiedRiders: unclassified,
                AvailableYears: availableYears,
                Riders: riderSummaries,
                RetrievedAt: DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            return Result.Failure<AllRidersValidityResponse>(
                new Error("RetrievalError", $"Failed to retrieve validity data: {ex.Message}", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET BY IQAMA
    // ─────────────────────────────────────────────────────────────

    public async Task<Result<RiderValidityResponse>> GetRiderValidityByIqamaAsync(
        long iqamaNo, int? year = null)
    {
        try
        {
            var rider = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo);

            if (rider == null)
            {
                var empExists = await _db.Employees.AnyAsync(e => e.IqamaNo == iqamaNo);
                return Result.Failure<RiderValidityResponse>(
                    new Error(
                        empExists ? "NoRiderDetails" : "NotFound",
                        empExists
                            ? $"Employee {iqamaNo} found but has no RiderDetails record"
                            : $"No employee found with IqamaNo {iqamaNo}",
                        404));
            }

            var validityQuery = _db.RiderMonthlyValidities
                .Where(v => v.EmployeeIqamaNo == iqamaNo)
                .AsNoTracking();
            if (year.HasValue)
                validityQuery = validityQuery.Where(v => v.Year == year.Value);

            var validityRecords = await validityQuery.ToListAsync();

            var today = DateTime.Now;
            var availableYears = validityRecords.Select(v => v.Year).Distinct().OrderBy(y => y).ToList();

            var yearRanges = availableYears.ToDictionary(
                y => y,
                y =>
                {
                    int start = validityRecords.Where(v => v.Year == y).Min(v => v.Month);
                    int end = y == today.Year ? today.Month : 12;
                    return (Start: start, End: end);
                });

            var validityMap = validityRecords.ToDictionary(v => (v.Year, v.Month), v => v);
            var monthDetails = new List<MonthValidityDetail>();

            foreach (var y in availableYears)
            {
                var (start, end) = yearRanges[y];
                for (int m = start; m <= end; m++)
                {
                    validityMap.TryGetValue((y, m), out var validity);
                    monthDetails.Add(BuildMonthDetail(y, m, validity));
                }
            }

            return Result.Success(new RiderValidityResponse(
                IqamaNo: iqamaNo,
                NameAR: rider.Employee.NameAR,
                NameEN: rider.Employee.NameEN,
                WorkingId: rider.WorkingId,
                CompanyName: rider.Company?.Name,
                AvailableYears: availableYears,
                Months: monthDetails,
                RetrievedAt: DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderValidityResponse>(
                new Error("RetrievalError", $"Failed to retrieve validity data: {ex.Message}", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED HELPER
    // ─────────────────────────────────────────────────────────────

    private static MonthValidityDetail BuildMonthDetail(
        int year, int month, RiderMonthlyValidity? validity)
    {
        string statusLabel = validity?.Status switch
        {
            ValidityStatus.Valid => "صالح",
            ValidityStatus.Invalid => "غير صالح",
            ValidityStatus.Freelancer => "فري لانسر",
            _ => "غير مصنف"
        };

        return new MonthValidityDetail(
            Year: year,
            Month: month,
            MonthName: MonthNames.GetValueOrDefault(month, month.ToString()),
            Status: validity?.Status,
            StatusLabel: statusLabel,
            RecordedOrders: validity?.TotalOrders ?? 0);
    }
}