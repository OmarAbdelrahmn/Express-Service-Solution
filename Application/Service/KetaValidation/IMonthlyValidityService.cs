using Application.Abstraction;
using Domain.Entities;
using Microsoft.AspNetCore.Http;

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

    // ============================================================
    // ADD TO: Application/Service/Import/IImportService.cs
    // Place alongside the other Task<Result<...>> method signatures
    // ============================================================

    // ── Method signature ─────────────────────────────────────────────────────

    Task<Result<KeetaShiftImportResponse>> ImportKeetaDriverShiftsAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null);

    // ── Response records ─────────────────────────────────────────────────────

    public record KeetaShiftImportResponse(
        int TotalRowsInExcel,
        DateOnly? EarliestDate,
        DateOnly? LatestDate,
        int DriversFound,       // matched to an internal RiderDetails record
        int DriversNotFound,    // PlatformDriverId had no match in RiderDetails / history
        int ShiftsCreated,
        int ShiftsUpdated,
        int NotInShift,         // rows where IsInShift == false (zero-time days)
        int NoQualifiedSlots,   // IsInShift == true but 0 qualified slots parsed
        int ErrorRows,
        List<KeetaShiftRowResult> Results,
        List<string> ProcessingErrors,
        DateTime ProcessedAt
    );

    public record KeetaShiftRowResult(
        int RowNumber,
        string PlatformDriverId,
        DateOnly ReportDate,
        bool MatchedToRider,
        string? WorkingId,
        int? RiderId,
        bool IsInShift,
        int TasksDelivered,
        int TotalConnectionMinutes,
        int QualifiedSlotsCount,
        List<KeetaSlotDetail> QualifiedSlots,
        KeetaImportAction Action,
        string? ErrorMessage
    );

    public record KeetaSlotDetail(
        string SlotKey,       // "08:00-12:00"
        string DurationRaw,   // "3 س 52 د"
        int DurationMinutes,  // 232
        int SlotOrder         // 1-6 (position in the original 6-slot day)
    );

    public enum KeetaImportAction
    {
        Created = 1,
        Updated = 2,
        DriverNotFound = 3, // stored without rider link
        NotInShift = 4,     // IsInShift == false; stored as informational record
        Error = 5
    }
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