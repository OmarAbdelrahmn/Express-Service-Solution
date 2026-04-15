using Domain.Entities.Petrol;

namespace Application.Contracts.Petrol;

// ═══════════════════════════════════════════════════════════════════════════════
// UPLOAD
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>One row parsed from the uploaded Excel file.</summary>
public record PetrolExcelRow(
    string PlateNumberE,
    decimal Cost
);

/// <summary>Result returned to the caller after processing an Excel upload.</summary>
public record PetrolUploadResult(
    DateOnly ReportDate,
    int TotalRows,
    int SuccessfullyAttributed,
    int Unattributed,
    int UnresolvedVehicles,
    IReadOnlyList<PetrolUploadRowDetail> Rows
);

public record PetrolUploadRowDetail(
    string PlateNumberE,
    string? ResolvedVehicleNumber,
    decimal Cost,
    bool VehicleResolved,
    int AttributedRiderCount,
    string? ErrorMessage
);

// ═══════════════════════════════════════════════════════════════════════════════
// RIDER REPORT  — "give me all petrol costs for rider X in month Y"
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Full monthly petrol report for a single rider.
/// Each VehicleEntry represents one vehicle the rider used during that month,
/// with a day-by-day breakdown.
/// </summary>
public record RiderPetrolMonthlyReport(
    long RiderIqamaNo,
    string RiderNameEN,
    string RiderNameAR,
    int Year,
    int Month,
    decimal TotalCost,
    int TotalDaysWithCost,
    int UniqueVehiclesUsed,
    IReadOnlyList<RiderVehicleEntry> VehicleEntries
);

/// <summary>One vehicle the rider used, with per-day breakdown.</summary>
public record RiderVehicleEntry(
    string VehicleNumber,
    string PlateNumberE,
    decimal VehicleTotalCost,
    int DaysUsed,
    IReadOnlyList<RiderDailyPetrolEntry> DailyEntries
);

/// <summary>A single day's petrol cost for a rider on a specific vehicle.</summary>
public record RiderDailyPetrolEntry(
    DateOnly Date,
    decimal Cost,
    PetrolAttributionSource AttributionSource,
    string? Notes
);

// ═══════════════════════════════════════════════════════════════════════════════
// VEHICLE REPORT  — "give me all riders who used vehicle X in month Y"
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Full monthly petrol report for a single vehicle.
/// Each RiderEntry represents one rider who used the vehicle during that month.
/// </summary>
public record VehiclePetrolMonthlyReport(
    string VehicleNumber,
    string PlateNumberE,
    int Year,
    int Month,
    decimal TotalCost,
    int TotalDaysWithCost,
    int UniqueRidersCount,
    IReadOnlyList<VehicleRiderEntry> RiderEntries,

    /// <summary>Days where no rider could be resolved (cost is unattributed).</summary>
    IReadOnlyList<VehicleUnattributedEntry> UnattributedEntries
);

/// <summary>One rider who used this vehicle, with per-day breakdown.</summary>
public record VehicleRiderEntry(
    long RiderIqamaNo,
    string RiderNameEN,
    string RiderNameAR,
    decimal RiderTotalCost,
    int DaysUsed,
    IReadOnlyList<VehicleDailyPetrolEntry> DailyEntries
);

/// <summary>A single day's petrol cost attributed to a rider for this vehicle.</summary>
public record VehicleDailyPetrolEntry(
    DateOnly Date,
    decimal Cost,
    PetrolAttributionSource AttributionSource,
    string? Notes
);

/// <summary>A day where the vehicle had a cost but no rider could be found.</summary>
public record VehicleUnattributedEntry(
    DateOnly Date,
    decimal Cost,
    string? Notes
);

// ═══════════════════════════════════════════════════════════════════════════════
// SUMMARY REPORTS  — lightweight list views for dashboards
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>One row in a cross-rider petrol summary for a given month.</summary>
public record RiderPetrolSummaryRow(
    long RiderIqamaNo,
    string RiderNameEN,
    string RiderNameAR,
    decimal TotalCost,
    int UniqueVehiclesUsed,
    int TotalDaysWithCost
);

/// <summary>One row in a cross-vehicle petrol summary for a given month.</summary>
public record VehiclePetrolSummaryRow(
    string VehicleNumber,
    string PlateNumberE,
    decimal TotalCost,
    int UniqueRidersCount,
    int TotalDaysWithCost,
    int UnattributedDays
);