using Application.Abstraction;
using Application.Contracts.TransporterShifts;

namespace Application.Service.TransporterShifts;

public interface ITransporterShiftService
{
    // ── Import ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse and persist the full week/grid extracted from the Excel file.
    /// Matches each TransporterId to RiderDetails.WorkingId.
    /// Overwrites existing shifts for the same rider + date + shiftIndex.
    /// </summary>
    Task<Result<ImportResultResponse>> ImportScheduleAsync(
        ImportTransporterScheduleRequest request,
        string importedBy);

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// All riders (with and without shifts) for a specific calendar date.
    /// Shows each rider's shift blocks, break status, and total hours.
    /// </summary>
    Task<Result<DayScheduleSummaryResponse>> GetDayScheduleAsync(DateOnly date);

    /// <summary>
    /// Riders whose shift window is active at the given date + time.
    /// Also returns riders who are NOT active at that moment (off-shift or break).
    /// </summary>
    Task<Result<TimeSlotRidersResponse>> GetActiveAtTimeAsync(DateOnly date, TimeOnly time);

    /// <summary>Full monthly breakdown for one rider.</summary>
    Task<Result<RiderMonthlyScheduleResponse>> GetRiderMonthlyScheduleAsync(
        int riderId,
        int year,
        int month);

    /// <summary>Monthly breakdown for ALL riders (Company 3).</summary>
    Task<Result<List<RiderMonthlyScheduleResponse>>> GetAllRidersMonthlyScheduleAsync(
        int year,
        int month);

    // ── Edits ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create or fully replace a shift block.
    /// If a shift with (RiderId, ShiftDate, ShiftIndex) already exists it is overwritten
    /// and flagged IsManuallyEdited = true.
    /// </summary>
    Task<Result<ShiftBlockResponse>> UpsertShiftAsync(
        UpsertShiftRequest request,
        string updatedBy);

    /// <summary>Patch only the timing fields of an existing shift.</summary>
    Task<Result<ShiftBlockResponse>> PatchShiftTimesAsync(PatchShiftTimesRequest request);

    /// <summary>Remove a shift block entirely.</summary>
    Task<Result> DeleteShiftAsync(int shiftId, string deletedBy);

    /// <summary>Mark an entire day as a break day for a rider (clears any existing shift blocks).</summary>
    Task<Result> MarkBreakDayAsync(int riderId, DateOnly date, string updatedBy);
}