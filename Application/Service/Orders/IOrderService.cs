using Application.Abstraction;
using Application.Contracts.Orders;

namespace Application.Service.Orders;

public interface IOrderService
{
    // ── Employee Queries ──────────────────────────────────────────────────────

    /// <summary>All employees in Company 3 that are enable.</summary>
    Task<Result<IEnumerable<Company4EmployeeResponse>>> GetCompany4EmployeesAsync();

    /// <summary>Single employee detail with today's order snapshot.</summary>
    Task<Result<Company4EmployeeResponse>> GetCompany4EmployeeAsync(long iqamaNo);

    // ── Order CRUD ────────────────────────────────────────────────────────────

    /// <summary>
    /// Admin creates a new order for an employee.
    /// Closes any open order for that employee today first.
    /// </summary>
    Task<Result<OrderDetailResponse>> CreateOrderAsync(CreateOrderRequest request, string requestedBy);

    /// <summary>Close the open order for a specific employee today.</summary>
    Task<Result> CloseEmployeeOrderAsync(long iqamaNo, string closedBy);

    /// <summary>Close ALL open orders for today.</summary>
    Task<Result> CloseAllOpenOrdersAsync(string closedBy);

    // ── Order Queries ─────────────────────────────────────────────────────────

    /// <summary>All orders for one employee, newest first.</summary>
    Task<Result<EmployeeOrderHistoryResponse>> GetEmployeeOrderHistoryAsync(long iqamaNo);

    /// <summary>All currently open (EndedAt == null) orders for today.</summary>
    Task<Result<ActiveOrderSnapshotResponse>> GetActiveOrdersSnapshotAsync();

    // ── Reports ───────────────────────────────────────────────────────────────

    /// <summary>Full daily report for a given date.</summary>
    Task<Result<DailyOrderReportResponse>> GetDailyReportAsync(DateOnly date);

    /// <summary>Date-range report with per-day and per-employee breakdown.</summary>
    Task<Result<DateRangeOrderReportResponse>> GetDateRangeReportAsync(DateTime start, DateTime end);

    /// <summary>All-time statistics for Company 3 orders.</summary>
    Task<Result<OrderStatisticsResponse>> GetStatisticsAsync();

    /// <summary>Today's summary: every eligible employee with their order status.</summary>
    Task<Result<DailyOrderReportResponse>> GetTodayReportAsync();

    // ── Dispatch (Shift-Aware) ─────────────────────────────────────────────────

    /// <summary>
    /// Riders whose shift window is active RIGHT NOW, each enriched with their
    /// order status for today. Also returns riders currently off-shift / on break.
    /// </summary>
    Task<Result<DispatchSnapshotResponse>> GetDispatchNowAsync();

    /// <summary>
    /// Riders whose shift window is active at the given date + time, each
    /// enriched with their order status for that day.
    /// </summary>
    Task<Result<DispatchSnapshotResponse>> GetDispatchAtAsync(DateOnly date, TimeOnly time);

    /// <summary>
    /// Full day planner: every Company-3 rider with their complete shift schedule
    /// AND their order summary for the given date. No time filter applied.
    /// </summary>
    Task<Result<DispatchDayResponse>> GetDispatchAllAsync(DateOnly date);
}


// ── Add these records to Application/Contracts/Orders/ ───────────────────────