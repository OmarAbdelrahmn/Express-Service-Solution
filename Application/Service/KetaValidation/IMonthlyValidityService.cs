using Application.Abstraction;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.KetaValidation;

public interface IMonthlyValidityService
{
    /// <summary>
    /// Returns all riders with their monthly validity records and
    /// actual accepted-order counts for each month in 2025 (Apr–Dec).
    /// </summary>
    Task<Result<AllRidersValidityResponse>> GetAllRidersValidityAsync(
        int? year = null);

    /// <summary>
    /// Returns a single rider's monthly validity records and actual
    /// accepted-order counts, looked up by IqamaNo.
    /// </summary>
    Task<Result<RiderValidityResponse>> GetRiderValidityByIqamaAsync(
        long iqamaNo,
        int? year = null);
}


public record AllRidersValidityResponse(
    int TotalRiders,
    int TotalValidRecords,
    int TotalInvalidRecords,
    int TotalFreelancerRecords,
    int TotalUnclassifiedRiders,       // riders with no validity row at all
    List<RiderValiditySummary> Riders,
    DateTime RetrievedAt
);

public record RiderValiditySummary(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string? WorkingId,
    string? CompanyName,
    List<MonthValidityDetail> Months   // one entry per month that has a record
);

public record RiderValidityResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string? WorkingId,
    string? CompanyName,
    List<MonthValidityDetail> Months,
    DateTime RetrievedAt
);

public record MonthValidityDetail(
    int Year,
    int Month,
    string MonthName,
    ValidityStatus? Status,            // null if no record exists for that month
    string StatusLabel,                // "صالح" / "غير صالح" / "فري لانسر" / "غير مصنف"
    int RecordedOrders,                // orders stored in RiderMonthlyValidity.TotalOrders
    int ActualShiftOrders,             // sum of AcceptedDailyOrders from RiderShifts
    bool OrdersMismatch                // true when the two counts differ
);