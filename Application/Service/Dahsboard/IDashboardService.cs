using Application.Abstraction;

namespace Application.Service.Dahsboard;

public interface IDashboardService
{
    Task<Result<DashboardOverview>> GetOverviewAsync();
    Task<Result<IEnumerable<CompanyOrderStats>>> GetOrdersByCompanyAsync(int year, int month);
    Task<Result<IEnumerable<MonthlyOrderTrend>>> GetOrderTrendAsync(int months = 6);
    Task<Result<IEnumerable<DailyOrderStats>>> GetDailyOrdersTrendAsync(int days = 30, int? companyId = null);
    Task<Result<IEnumerable<TopRiderStats>>> GetTopRidersAsync(int year, int month, int? companyId = null, int top = 10);
    Task<Result<VehicleStatusStats>> GetVehicleStatsAsync();
    Task<Result<IEnumerable<HousingOccupancyStats>>> GetHousingStatsAsync();
    Task<Result<IqamaExpiryStats>> GetIqamaExpiryStatsAsync();
    Task<Result<EmployeeStatusStats>> GetEmployeeStatusStatsAsync();
    Task<Result<IEnumerable<MonthlyValidityStats>>> GetMonthlyValidityStatsAsync(int year, int month);
    Task<Result<IEnumerable<RiderOrdersByCompanyAndMonth>>> GetRiderOrdersMatrixAsync(int year);
    Task<Result<IEnumerable<CountryDistributionStats>>> GetCountryDistributionAsync();
    Task<Result<IEnumerable<SponsorStats>>> GetSponsorStatsAsync();
    Task<Result<IEnumerable<CompanyRiderCountStats>>> GetRiderCountByCompanyAsync();
    Task<Result<DailyCompanyReport>> GetDailyReportAsync(DailyCompanyReportRequest request);
}


public record DailyCompanyReportRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    int? CompanyId = null          // null = all companies
);

public record DailyCompanyReport(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalDays,
    int TotalCompanies,
    long GrandTotalOrders,
    int GrandTotalShifts,
    IReadOnlyList<CompanyDailyReport> Companies
);

public record CompanyDailyReport(
    int CompanyId,
    string CompanyName,
    int TotalOrders,
    int TotalShifts,
    int TotalUniqueRiders,
    double AvgOrdersPerDay,
    double AvgRidersPerDay,
    IReadOnlyList<DayEntry> Days
);

public record DayEntry(
    DateOnly Date,
    string DateLabel,          // "Mon 02 Jun"
    string DayOfWeek,
    int AcceptedOrders,
    int RejectedOrders,
    int UniqueRiders,
    int TotalShifts,
    double AvgOrdersPerRider,
    double TotalWorkingHours
);

// ── Overview ─────────────────────────────────────────────────────────────────

public record DashboardOverview(
    int TotalEmployees,
    int TotalRiders,
    int TotalVehicles,
    int TotalHousings,
    int ActiveEmployees,
    int ActiveRiders,
    int AvailableVehicles,
    int TakenVehicles,
    int ProblemVehicles,
    int StolenVehicles,
    int BreakUpVehicles,
    int ExpiredIqamas,
    int CriticalIqamas,
    int TotalCompanies,
    int TodayShifts,
    int TodayOrders
);

// ── Company Orders ────────────────────────────────────────────────────────────

public record CompanyOrderStats(
    int CompanyId,
    string CompanyName,
    int TotalOrders,
    int TotalShifts,
    int TotalRiders,
    double AvgOrdersPerRider,
    double AvgOrdersPerShift,
    int Month,
    int Year
);

// ── Order Trends ──────────────────────────────────────────────────────────────

public record MonthlyOrderTrend(
    int Year,
    int Month,
    string MonthLabel,
    int TotalOrders,
    int TotalShifts,
    int TotalRiders,
    int TotalRejected,
    double AvgOrdersPerRider
);

public record DailyOrderStats(
    DateOnly Date,
    string DateLabel,
    int TotalOrders,
    int TotalShifts,
    int TotalRiders,
    int TotalRejected,
    double AvgOrdersPerRider
);

// ── Top Riders ────────────────────────────────────────────────────────────────

public record TopRiderStats(
    int Rank,
    long IqamaNo,
    string WorkingId,
    string NameAR,
    string NameEN,
    string CompanyName,
    string? HousingName,
    int TotalOrders,
    int TotalShifts,
    double AvgOrdersPerShift,
    double TotalHours,
    int TotalRejected,
    int Month,
    int Year
);

// ── Vehicle Stats ─────────────────────────────────────────────────────────────

public record VehicleStatusStats(
    int Total,
    int Available,
    int Taken,
    int Problem,
    int Stolen,
    int BreakUp,
    IReadOnlyList<VehicleTypeBreakdown> ByType
);

public record VehicleTypeBreakdown(
    string VehicleType,
    int Count,
    int Available,
    int Taken
);

// ── Housing ───────────────────────────────────────────────────────────────────

public record HousingOccupancyStats(
    int HousingId,
    string HousingName,
    string Address,
    int Capacity,
    int OccupiedCount,
    double OccupancyRate
);

// ── Iqama Expiry ──────────────────────────────────────────────────────────────

public record IqamaExpiryStats(
    int Expired,
    int Critical,
    int Warning,
    int Upcoming,
    int Safe
);

// ── Employee Status ───────────────────────────────────────────────────────────

public record EmployeeStatusStats(
    int Enable,
    int Disable,
    int Fleeing,
    int Vacation,
    int Accident,
    int Sick,
    int TotalEmployees,
    int TotalRiders
);

// ── Monthly Validity ──────────────────────────────────────────────────────────

public record MonthlyValidityStats(
    int CompanyId,
    string CompanyName,
    int Valid,
    int Invalid,
    int Freelancer,
    int Total,
    int Year,
    int Month
);

// ── Rider Orders Matrix ───────────────────────────────────────────────────────

public record RiderOrdersByCompanyAndMonth(
    int CompanyId,
    string CompanyName,
    IReadOnlyList<MonthOrderPoint> MonthlyData
);

public record MonthOrderPoint(
    int Month,
    string MonthLabel,
    int TotalOrders,
    int TotalRiders
);

// ── Country / Sponsor Distribution ───────────────────────────────────────────

public record CountryDistributionStats(
    string Country,
    int EmployeeCount,
    int RiderCount,
    int Total
);

public record SponsorStats(
    string Sponsor,
    long SponsorNo,
    int EmployeeCount,
    int RiderCount,
    int Total
);

public record CompanyRiderCountStats(
    int CompanyId,
    string CompanyName,
    int TotalRiders,
    int ActiveRiders,
    int InactiveRiders
);