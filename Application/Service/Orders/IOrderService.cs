using Application.Abstraction;
using Application.Contracts.Orders;

namespace Application.Service.Orders;

public interface IOrderService
{
    // ── Employee Queries ──────────────────────────────────────────────────────

    /// <summary>All employees in Company 4 that are IsEmployee + enable.</summary>
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

    /// <summary>All-time statistics for Company 4 orders.</summary>
    Task<Result<OrderStatisticsResponse>> GetStatisticsAsync();

    /// <summary>Today's summary: every eligible employee with their order status.</summary>
    Task<Result<DailyOrderReportResponse>> GetTodayReportAsync();
}