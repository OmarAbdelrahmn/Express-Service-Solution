namespace Application.Contracts.TransporterShifts;

// ═══════════════════════════════════════════════════════════════════════════════
// Import / Parse
// ═══════════════════════════════════════════════════════════════════════════════


/// <summary>
/// Represents a single parsed cell from the transporter schedule Excel grid.
/// One cell = one rider × one calendar day.
/// </summary>
public record ImportScheduleCell(
    /// <summary>
    /// Column B value — maps to RiderDetails.WorkingId.
    /// </summary>
    string TransporterId,

    /// <summary>
    /// Column A value — used for warning messages only, not stored.
    /// </summary>
    string AssociateName,

    /// <summary>
    /// Raw column header text, e.g. "Sun, 03/May".
    /// Parsed into a DateOnly by ScheduleHeaderParser inside the service.
    /// </summary>
    string ColumnHeader,

    /// <summary>
    /// Raw cell content. Examples:
    ///   "Driver • 6 PM • 5h"                          → single shift
    ///   "Driver • 6 PM • 5h\nDriver • 12 PM • 5h"    → two shifts
    ///   null / empty                                   → break day
    /// </summary>
    string? CellContent
);

/// <summary>
/// Request payload for ImportScheduleAsync.
/// Produced either by the Excel parser or supplied directly as JSON.
/// </summary>
public record ImportTransporterScheduleRequest(
    /// <summary>All rider × day cells extracted from the schedule.</summary>
    List<ImportScheduleCell> Cells,

    /// <summary>
    /// Optional year override. When null the service infers the year from
    /// the current Saudi time (UTC+3), bumping to the next year when the
    /// parsed month is January and the current month is December.
    /// </summary>
    int? OverrideYear = null
);
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