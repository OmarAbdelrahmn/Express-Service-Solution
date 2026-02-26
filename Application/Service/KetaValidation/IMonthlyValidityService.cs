using Application.Abstraction;
using Domain.Entities;

namespace Application.Service.KetaValidation;

public interface IMonthlyValidityService
{
    /// <summary>
    /// Returns all riders with their monthly validity records.
    /// Pass year to filter a specific year; omit to get all years in the DB.
    /// </summary>
    Task<Result<AllRidersValidityResponse>> GetAllRidersValidityAsync(int? year = null);

    /// <summary>
    /// Returns a single rider's monthly validity records by IqamaNo.
    /// Pass year to filter a specific year; omit to get all years in the DB.
    /// </summary>
    Task<Result<RiderValidityResponse>> GetRiderValidityByIqamaAsync(long iqamaNo, int? year = null);
}

public record AllRidersValidityResponse(
    int TotalRiders,
    int TotalValidRecords,
    int TotalInvalidRecords,
    int TotalFreelancerRecords,
    int TotalUnclassifiedRiders,
    List<int> AvailableYears,           // all years found in the DB (or just the filtered year)
    List<RiderValiditySummary> Riders,
    DateTime RetrievedAt
);

public record RiderValiditySummary(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string? WorkingId,
    string? CompanyName,
    List<MonthValidityDetail> Months
);

public record RiderValidityResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string? WorkingId,
    string? CompanyName,
    List<int> AvailableYears,
    List<MonthValidityDetail> Months,
    DateTime RetrievedAt
);

public record MonthValidityDetail(
    int Year,
    int Month,
    string MonthName,
    ValidityStatus? Status,
    string StatusLabel,                 // "صالح" / "غير صالح" / "فري لانسر" / "غير مصنف"
    int RecordedOrders
);