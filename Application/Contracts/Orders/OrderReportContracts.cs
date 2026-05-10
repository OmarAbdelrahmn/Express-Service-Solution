namespace Application.Contracts.Orders;

// ── Employee List ─────────────────────────────────────────────────────────────


// ── Shared sub-record ─────────────────────────────────────────────────────────

/// <summary>
/// Lightweight shift block summary embedded inside DispatchRiderResponse.
/// Does not carry the full ShiftBlockResponse (no DB id needed on the dispatch view).
/// </summary>
public record ShiftBlockSummary(
    int ShiftIndex,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    float DurationHours,
    bool IsManuallyEdited,
    string? RawEntry
);

// ── Per-rider merged row ──────────────────────────────────────────────────────

/// <summary>
/// One rider entry in a dispatch response.
/// Combines transporter shift schedule with the order summary for the same day.
/// </summary>
public record DispatchRiderResponse(
    // Identity
    int RiderId,
    long EmployeeIqamaNo,
    string WorkingId,
    string NameEN,
    string NameAR,
    string? HousingName,

    // Shift
    bool HasShift,
    bool IsBreakDay,
    float TotalHoursScheduled,
    List<ShiftBlockSummary> Shifts,

    // Orders
    bool HadOrderToday,
    bool IsCurrentlyOnOrder,
    int TotalOrdersToday,
    double TotalMinutesOnOrder,
    DateTime? FirstOrderAt,
    DateTime? LastOrderAt,
    List<OrderDetailResponse> Orders
);

// ── Snapshot (time-filtered) ──────────────────────────────────────────────────

/// <summary>
/// Response for GetDispatchNowAsync / GetDispatchAtAsync.
/// Riders are split into active (shift window covers the queried time)
/// and inactive (off-shift, break, or no schedule data).
/// </summary>
public record DispatchSnapshotResponse(
    DateOnly Date,
    TimeOnly Time,
    DateTime GeneratedAt,
    int ActiveCount,
    int InactiveCount,
    List<DispatchRiderResponse> ActiveRiders,
    List<DispatchRiderResponse> InactiveRiders
);

// ── Full day ──────────────────────────────────────────────────────────────────

/// <summary>
/// Response for GetDispatchAllAsync.
/// All riders for the day, no time filter — each with shift + order data.
/// </summary>
public record DispatchDayResponse(
    DateOnly Date,
    DateTime GeneratedAt,
    int TotalRiders,
    int RidersWithShifts,
    int RidersOnBreak,
    int RidersWithNoData,
    List<DispatchRiderResponse> Riders
);
public record Company4EmployeeResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string Country,
    string Phone,
    string Status,
    string? IBAN,
    bool INKSA,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string? HousingName,
    string? WorkingId,
    DateTime CreatedAt,
    // Today's order snapshot
    bool IsCurrentlyOnOrder,
    int TotalOrdersToday,
    DateTime? CurrentOrderStartedAt,
    string? ProfileImagePath
);

// ── Single Order Detail ───────────────────────────────────────────────────────

public record OrderDetailResponse(
    int Id,
    long EmployeeIqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    string EmployeeStatus,
    string? HousingName,
    string? WorkingId,
    bool Order,
    DateTime StartedAt,
    DateTime? EndedAt,
    double? DurationMinutes,  // null if still open
    DateOnly OrderDate,
    string RequestedBy,
    string? Notes
);

// ── Daily Report ──────────────────────────────────────────────────────────────

public record DailyOrderReportResponse(
    DateOnly Date,
    DateTime GeneratedAt,
    int TotalEligibleEmployees,
    int EmployeesWithOrders,
    int EmployeesWithoutOrders,
    int TotalOrdersCreated,
    int CurrentlyActiveOrders,
    double TotalMinutesWorked,
    List<DailyEmployeeOrderSummary> Employees
);

public record DailyEmployeeOrderSummary(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string? HousingName,
    string? WorkingId,
    bool HadOrderToday,
    bool IsCurrentlyOnOrder,
    int TotalOrders,
    double TotalMinutesOnOrder,
    DateTime? FirstOrderAt,
    DateTime? LastOrderAt,
    List<OrderDetailResponse> Orders
);

// ── Employee Order History Report ─────────────────────────────────────────────

public record EmployeeOrderHistoryResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string Country,
    string? HousingName,
    string? WorkingId,
    string CurrentStatus,
    int TotalOrders,
    int TotalDaysWithOrders,
    double TotalMinutesOnOrder,
    double AverageOrdersPerDay,
    double AverageMinutesPerOrder,
    DateTime? FirstOrderEver,
    DateTime? LastOrderEver,
    List<OrderDetailResponse> Orders
);

// ── Date Range Report ─────────────────────────────────────────────────────────

public record DateRangeOrderReportResponse(
    DateTime StartDate,
    DateTime EndDate,
    DateTime GeneratedAt,
    int TotalDays,
    int TotalOrders,
    int TotalEmployeesInvolved,
    double TotalMinutesWorked,
    List<DateRangeDaySummary> DaySummaries,
    List<DateRangeEmployeeSummary> EmployeeSummaries
);

public record DateRangeDaySummary(
    DateOnly Date,
    int TotalOrders,
    int ActiveEmployees,
    double TotalMinutesWorked
);

public record DateRangeEmployeeSummary(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string? WorkingId,
    int TotalOrders,
    int DaysActive,
    double TotalMinutesOnOrder
);

// ── Statistics ────────────────────────────────────────────────────────────────

public record OrderStatisticsResponse(
    DateTime GeneratedAt,
    int TotalEligibleEmployees,
    int TotalOrdersAllTime,
    int TotalOrdersToday,
    int CurrentlyActiveOrders,
    double TotalMinutesAllTime,
    double AverageOrdersPerDay,
    double AverageMinutesPerOrder,
    Dictionary<string, int> OrdersByMonth,       // "2025-05" -> count
    Dictionary<string, int> OrdersByEmployee,    // NameEN -> count
    Dictionary<string, double> MinutesByEmployee // NameEN -> total minutes
);

public record CreateOrderRequest(
    long EmployeeIqamaNo,
    bool Order,
    string? Notes
);
// ── Active Orders Snapshot ────────────────────────────────────────────────────

public record ActiveOrderSnapshotResponse(
    DateTime SnapshotAt,
    int TotalActiveOrders,
    int TotalEligibleEmployees,
    List<ActiveOrderItem> ActiveOrders,
    List<Company4EmployeeResponse> EmployeesNotOnOrder
);

public record ActiveOrderItem(
    int OrderId,
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string? HousingName,
    string? WorkingId,
    DateTime StartedAt,
    double MinutesElapsed,
    string? Notes
);