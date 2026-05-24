using Application.Abstraction;
using Domain.Entities;

namespace Application.Service.Reminder;

public interface IReminderService
{
    // ══════════════════════════════════════════════════════════════════════
    //  ADMIN – Maintenance Interval Management
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Create a new maintenance interval rule (e.g. "Oil Filter every 5 days").</summary>
    Task<Result<MaintenanceIntervalResponse>> CreateIntervalAsync(
        CreateIntervalRequest request,
        string createdBy);

    /// <summary>Update interval timing, alert window, scope or notes.</summary>
    Task<Result<MaintenanceIntervalResponse>> UpdateIntervalAsync(
        int id,
        UpdateIntervalRequest request,
        string updatedBy);

    /// <summary>Hard-delete an interval (only when no baselines reference it).</summary>
    Task<Result> DeleteIntervalAsync(int id);

    /// <summary>Flip IsActive on an interval without deleting it.</summary>
    Task<Result<MaintenanceIntervalResponse>> ToggleIntervalActiveAsync(int id, string updatedBy);

    /// <summary>List all intervals (active + inactive).</summary>
    Task<Result<IEnumerable<MaintenanceIntervalResponse>>> GetAllIntervalsAsync();

    /// <summary>Get a single interval by id.</summary>
    Task<Result<MaintenanceIntervalResponse>> GetIntervalByIdAsync(int id);

    // ══════════════════════════════════════════════════════════════════════
    //  ADMIN – Baseline Management
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set (or upsert) the "last done" baseline for a vehicle or rider.
    /// Use this to seed historical data before usage tracking began.
    /// </summary>
    Task<Result<BaselineResponse>> SetBaselineAsync(
        SetBaselineRequest request,
        string setBy);

    /// <summary>List all baselines for a given interval.</summary>
    Task<Result<IEnumerable<BaselineResponse>>> GetBaselinesByIntervalAsync(int intervalId);

    /// <summary>List all baselines for a given vehicle (across all intervals).</summary>
    Task<Result<IEnumerable<BaselineResponse>>> GetBaselinesByVehicleAsync(string vehicleNumber);

    /// <summary>List all baselines for a given rider (across all intervals).</summary>
    Task<Result<IEnumerable<BaselineResponse>>> GetBaselinesByRiderAsync(int riderId);

    // ══════════════════════════════════════════════════════════════════════
    //  ADMIN – Global Reminder Dashboard
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Return every vehicle and rider across ALL housings whose maintenance
    /// is due on (or overdue by) the given date.
    /// Passing null uses today (KSA time).
    /// </summary>
    Task<Result<MaintenanceReminderReport>> GetAllDueMaintenanceAsync(DateOnly? checkDate = null);

    // ══════════════════════════════════════════════════════════════════════
    //  MEMBER – Housing Reminder Dashboard
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Return vehicles and riders IN THE MANAGER'S HOUSING whose maintenance
    /// is due on (or overdue by) checkDate.
    /// checkDate null → today.
    /// </summary>
    Task<Result<MaintenanceReminderReport>> GetHousingDueMaintenanceAsync(
        long managerIqamaNo,
        DateOnly? checkDate = null);

    // ══════════════════════════════════════════════════════════════════════
    //  DTOs
    // ══════════════════════════════════════════════════════════════════════

    // ── Interval ──────────────────────────────────────────────────────────

    public record CreateIntervalRequest(
        /// <summary>Set when ItemType = SparePart.</summary>
        int? SparePartId,
        /// <summary>Set when ItemType = Accessory.</summary>
        int? AccessoryId,
        MaintenanceItemType ItemType,
        /// <summary>Maintenance required every N days.</summary>
        int IntervalDays,
        /// <summary>Show alert this many days before the due date (0 = day-of only).</summary>
        int AlertDaysBeforeDue = 0,
        /// <summary>Null = all housings; set to housing name to restrict scope.</summary>
        string? Location = null,
        string? Notes = null
    );

    public record UpdateIntervalRequest(
        int IntervalDays,
        int AlertDaysBeforeDue,
        string? Location,
        string? Notes,
        bool IsActive
    );

    public record MaintenanceIntervalResponse(
        int Id,
        int? SparePartId,
        int? AccessoryId,
        MaintenanceItemType ItemType,
        string ItemName,
        int IntervalDays,
        int AlertDaysBeforeDue,
        string? Location,
        bool IsActive,
        string? Notes,
        string CreatedBy,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        string? UpdatedBy
    );

    // ── Baseline ──────────────────────────────────────────────────────────

    public record SetBaselineRequest(
        int MaintenanceIntervalId,
        /// <summary>Required when the interval is SparePart type.</summary>
        string? VehicleNumber,
        /// <summary>Required when the interval is Accessory type.</summary>
        int? RiderId,
        DateTime LastDoneAt,
        string? Notes = null
    );

    public record BaselineResponse(
        int Id,
        int MaintenanceIntervalId,
        string ItemName,
        MaintenanceItemType ItemType,
        string? VehicleNumber,
        string? VehiclePlate,
        int? RiderId,
        string? RiderName,
        DateTime LastDoneAt,
        DateTime NextDueAt,
        int DaysUntilDue,
        string SetBy,
        DateTime CreatedAt,
        string? Notes
    );

    // ── Reminder Report ───────────────────────────────────────────────────

    public record MaintenanceReminderReport(
        DateOnly CheckDate,
        /// <summary>Total unique vehicles/riders that have at least one due item.</summary>
        int TotalAffectedVehicles,
        int TotalAffectedRiders,
        int TotalOverdueItems,
        int TotalDueTodayItems,
        int TotalUpcomingItems,
        int TotalNeverDoneItems,
        List<VehicleMaintenanceReminder> VehicleReminders,
        List<RiderMaintenanceReminder> RiderReminders
    );

    public record VehicleMaintenanceReminder(
        string VehicleNumber,
        string VehiclePlate,
        string Location,
        long? AssignedRiderIqamaNo,
        string? AssignedRiderName,
        /// <summary>All maintenance items for this vehicle that are due/overdue/upcoming on CheckDate.</summary>
        List<MaintenanceItem> DueItems
    );

    public record RiderMaintenanceReminder(
        int RiderId,
        long RiderIqamaNo,
        string RiderNameAR,
        string RiderNameEN,
        string WorkingId,
        string HousingName,
        /// <summary>All maintenance items for this rider that are due/overdue/upcoming on CheckDate.</summary>
        List<MaintenanceItem> DueItems
    );

    public record MaintenanceItem(
        int IntervalId,
        string ItemName,
        MaintenanceItemType ItemType,
        int IntervalDays,
        int AlertDaysBeforeDue,
        /// <summary>Null when no usage or baseline record exists (NeverDone).</summary>
        DateTime? LastDoneAt,
        /// <summary>
        /// Calculated next due date.  When LastDoneAt is null this is set to
        /// DateTime.MinValue to force NeverDone status.
        /// </summary>
        DateTime NextDueAt,
        /// <summary>
        /// How many days from CheckDate until due.
        /// Negative = already overdue.
        /// Zero     = due today.
        /// Positive = still upcoming.
        /// </summary>
        int DaysUntilDue,
        MaintenanceStatus Status,
        /// <summary>Source that produced LastDoneAt: "Usage", "Baseline", or "None".</summary>
        string RecordSource
    );

    public enum MaintenanceStatus
    {
        /// <summary>Due date is beyond today + AlertDaysBeforeDue.</summary>
        OK = 1,
        /// <summary>Due within the AlertDaysBeforeDue window.</summary>
        Upcoming = 2,
        /// <summary>Due exactly on CheckDate.</summary>
        DueToday = 3,
        /// <summary>Due date has already passed.</summary>
        Overdue = 4,
        /// <summary>No usage record and no baseline – never been serviced.</summary>
        NeverDone = 5
    }
}