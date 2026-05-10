namespace Application.Contracts.TransporterShifts;

// ═══════════════════════════════════════════════════════════════════════════════
// Import / Parse
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// One cell from the Excel sheet, already extracted by the client.
/// Column headers (e.g. "Sun, 03/May") are parsed server-side.
/// </summary>
public record ExcelShiftCell(
    string TransporterId,       // Column B – maps to RiderDetails.WorkingId
    string AssociateName,       // Column A – for display / fallback matching
    string ColumnHeader,        // e.g. "Sun, 03/May" or "Sat, 09/May"
    string CellContent          // e.g. "Driver • 6 PM • 5h\nDriver • 12 PM • 5h"
);

/// <summary>Bulk import request – the full parsed grid from one Excel file/sheet.</summary>
public record ImportTransporterScheduleRequest(
    List<ExcelShiftCell> Cells,
    int? OverrideYear = null    // Optional – defaults to current year
);

// ═══════════════════════════════════════════════════════════════════════════════
// Responses
// ═══════════════════════════════════════════════════════════════════════════════

public record ImportResultResponse(
    int TotalCellsProcessed,
    int ShiftsCreated,
    int BreakDaysMarked,
    int UnmatchedTransporterIds,
    List<string> Warnings
);

/// <summary>Single shift block (one line inside an Excel cell).</summary>
public record ShiftBlockResponse(
    int Id,
    int ShiftIndex,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    float DurationHours,
    bool IsBreakDay,
    string? RawEntry,
    bool IsManuallyEdited,
    string? Notes
);

/// <summary>All shifts for one rider on one day.</summary>
public record RiderDayShiftResponse(
    int RiderId,
    string WorkingId,
    string NameEN,
    string NameAR,
    DateOnly ShiftDate,
    bool HasShift,
    bool IsBreakDay,
    float TotalHoursScheduled,
    List<ShiftBlockResponse> Shifts
);

/// <summary>Summary for a whole day – used by the day view endpoint.</summary>
public record DayScheduleSummaryResponse(
    DateOnly Date,
    int TotalRiders,
    int RidersWithShifts,
    int RidersOnBreak,
    int RidersWithNoData,
    List<RiderDayShiftResponse> Riders
);

/// <summary>Riders active (i.e. their shift window covers) a specific point in time.</summary>
public record TimeSlotRidersResponse(
    DateOnly Date,
    TimeOnly Time,
    int ActiveCount,
    int InactiveCount,
    List<RiderDayShiftResponse> ActiveRiders,
    List<RiderDayShiftResponse> InactiveRiders
);

/// <summary>Monthly overview per rider.</summary>
public record RiderMonthlyScheduleResponse(
    int RiderId,
    string WorkingId,
    string NameEN,
    string NameAR,
    int Year,
    int Month,
    int TotalWorkingDays,
    int TotalBreakDays,
    int TotalDaysWithNoData,
    float TotalScheduledHours,
    List<RiderDayShiftResponse> DailyBreakdown
);

// ═══════════════════════════════════════════════════════════════════════════════
// Edit Requests
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Manually add or update a single shift block.</summary>
public record UpsertShiftRequest(
    int RiderId,
    DateOnly ShiftDate,
    int ShiftIndex,             // 1 or 2
    TimeOnly? StartTime,
    float DurationHours,
    bool IsBreakDay,
    string? Notes
);

/// <summary>Delete a single shift block by id.</summary>
public record DeleteShiftRequest(int ShiftId, string DeletedBy);

/// <summary>Patch only the time fields of an existing shift.</summary>
public record PatchShiftTimesRequest(
    int ShiftId,
    TimeOnly? NewStartTime,
    float? NewDurationHours,
    string? Notes,
    string UpdatedBy
);