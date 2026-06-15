namespace Application.Service.DailyReport;

/// <summary>
/// One rider who is behind on the monthly 450-order target.
/// </summary>
public record MonthlyProgressRiderRow(
    string RiderNameAR,
    long IqamaNo,
    string HousingName,
    string WorkingId,
    int OrdersSoFar,           // total accepted orders in the month so far
    int ProportionalTarget,    // expected by today based on days elapsed
    int RemainingToFullTarget, // 450 - OrdersSoFar
    int Shortfall              // ProportionalTarget - OrdersSoFar  (always > 0 for rows in this list)
);

/// <summary>
/// Full payload for one company's monthly progress email.
/// </summary>
public record MonthlyProgressPayload(
    DateOnly ReportDate,       // yesterday (last day with data)
    int CompanyId,
    string CompanyName,
    int DaysElapsed,           // days with data = ReportDate.Day
    int DaysInMonth,
    int MonthlyTarget,         // 450
    int ProportionalTarget,    // expected orders by now  = DaysElapsed / DaysInMonth * MonthlyTarget
    List<MonthlyProgressRiderRow> Riders
);