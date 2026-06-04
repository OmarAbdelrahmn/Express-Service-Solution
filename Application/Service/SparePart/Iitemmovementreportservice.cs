using Application.Abstraction;

namespace Application.Service.SparePart;

/// <summary>
/// Produces a comprehensive movement report for spare parts and accessories over a
/// date range.  "Movement" covers three event types:
///   1. Transfers  – stock moved between locations (الشركة ↔ housing).
///   2. Usages     – stock consumed for a vehicle (spare parts) or issued to a rider (accessories).
///   3. Snapshots  – current quantity per location at the time of the query.
///
/// Every item that had ANY movement in the period is included.
/// Items with no movement but optional filter applied are excluded.
/// </summary>
public interface IItemMovementReportService
{
    // ──────────────────────────────────────────────────────────────────────
    // Full report (both spare parts AND accessories)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a unified report for every spare part and accessory that had
    /// at least one transfer or usage within [fromDate, toDate].
    /// Pass itemName to filter to a single item across all locations.
    /// Pass location to scope to a single housing / "الشركة".
    /// </summary>
    Task<Result<FullItemMovementReport>> GetFullReportAsync(
        DateTime fromDate,
        DateTime toDate,
        string? itemName = null,
        string? location = null);

    // ──────────────────────────────────────────────────────────────────────
    // Spare-parts only
    // ──────────────────────────────────────────────────────────────────────

    Task<Result<SparePartMovementReport>> GetSparePartReportAsync(
        DateTime fromDate,
        DateTime toDate,
        string? itemName = null,
        string? location = null);

    // ──────────────────────────────────────────────────────────────────────
    // Accessories only
    // ──────────────────────────────────────────────────────────────────────

    Task<Result<AccessoryMovementReport>> GetAccessoryReportAsync(
        DateTime fromDate,
        DateTime toDate,
        string? itemName = null,
        string? location = null);

    // ══════════════════════════════════════════════════════════════════════
    //  RESPONSE RECORDS
    // ══════════════════════════════════════════════════════════════════════

    // ── Top-level wrappers ────────────────────────────────────────────────

    public record FullItemMovementReport(
        DateTime FromDate,
        DateTime ToDate,
        string? AppliedItemFilter,
        string? AppliedLocationFilter,
        ReportTotals Totals,
        List<SparePartItemMovement> SparePartMovements,
        List<AccessoryItemMovement> AccessoryMovements,
        DateTime GeneratedAt
    );

    public record SparePartMovementReport(
        DateTime FromDate,
        DateTime ToDate,
        string? AppliedItemFilter,
        string? AppliedLocationFilter,
        SparePartReportTotals Totals,
        List<SparePartItemMovement> Items,
        DateTime GeneratedAt
    );

    public record AccessoryMovementReport(
        DateTime FromDate,
        DateTime ToDate,
        string? AppliedItemFilter,
        string? AppliedLocationFilter,
        AccessoryReportTotals Totals,
        List<AccessoryItemMovement> Items,
        DateTime GeneratedAt
    );

    // ── Totals ────────────────────────────────────────────────────────────

    public record ReportTotals(
        int TotalSparePartItems,          // distinct spare part names with activity
        int TotalAccessoryItems,          // distinct accessory names with activity
        int TotalTransferEvents,          // individual TransferItem rows in period
        int TotalUsageEvents,             // SparePartUsage + RiderAccessoryUsage rows
        decimal TotalSparePartsCost,      // sum(qty × price) for all SP usages
        decimal TotalAccessoriesCost      // sum(price) for all accessory issuances
    );

    public record SparePartReportTotals(
        int TotalItems,
        int TotalTransferEvents,
        int TotalUsageEvents,
        decimal TotalCostOfUsages,
        int TotalQuantityTransferred,
        int TotalQuantityUsed
    );

    public record AccessoryReportTotals(
        int TotalItems,
        int TotalTransferEvents,
        int TotalUsageEvents,
        decimal TotalCostOfIssuances,
        int TotalQuantityTransferred,
        int TotalTimesIssued
    );

    // ── Spare-part item movement block ────────────────────────────────────

    /// <summary>
    /// All movement for ONE spare-part name in the report period.
    /// A spare part may exist at multiple locations simultaneously (each has its own Id).
    /// </summary>
    public record SparePartItemMovement(
        string ItemName,

        // Current stock snapshot across ALL locations (not period-filtered)
        List<ItemLocationSnapshot> CurrentStock,

        // Transfers that touched items matching this name
        List<SparePartTransferEvent> Transfers,

        // Usage records within the period
        List<SparePartUsageEvent> Usages,

        // Per-item summary
        SparePartItemSummary Summary
    );

    public record SparePartItemSummary(
        int TotalQuantityTransferred,
        int TotalQuantityUsed,
        decimal TotalUsageCost,
        int TransferEventCount,
        int UsageEventCount,
        /// <summary>All distinct locations this item moved through during the period.</summary>
        List<string> LocationsInvolved,
        /// <summary>The location that consumed the most quantity via usages.</summary>
        string? HighestUsageLocation,
        decimal AveragePriceAcrossLocations
    );

    // ── Accessory item movement block ─────────────────────────────────────

    public record AccessoryItemMovement(
        string ItemName,

        List<ItemLocationSnapshot> CurrentStock,

        List<AccessoryTransferEvent> Transfers,

        List<AccessoryUsageEvent> Usages,

        AccessoryItemSummary Summary
    );

    public record AccessoryItemSummary(
        int TotalQuantityTransferred,
        int TotalTimesIssued,
        decimal TotalIssuanceCost,
        int TransferEventCount,
        int UsageEventCount,
        List<string> LocationsInvolved,
        /// <summary>The housing with the most accessory issuances.</summary>
        string? HighestIssuanceLocation,
        decimal AveragePriceAcrossLocations
    );

    // ── Shared location snapshot ───────────────────────────────────────────

    /// <summary>Current quantity + price at a single (Name, Location) record.</summary>
    public record ItemLocationSnapshot(
        int ItemId,
        string Location,
        int CurrentQuantity,
        decimal CurrentPrice
    );

    // ── Transfer events ───────────────────────────────────────────────────

    /// <summary>One spare-part line inside a Transfer document.</summary>
    public record SparePartTransferEvent(
        int TransferId,
        int TransferItemId,
        /// <summary>The canonical spare-part Id for this location copy.</summary>
        int ItemId,
        string FromLocation,
        string ToLocation,
        int QuantityTransferred,
        string TransferredBy,
        DateTime TransferredAt
    );

    public record AccessoryTransferEvent(
        int TransferId,
        int TransferItemId,
        int ItemId,
        string FromLocation,
        string ToLocation,
        int QuantityTransferred,
        string TransferredBy,
        DateTime TransferredAt
    );

    // ── Usage events ──────────────────────────────────────────────────────

    public record SparePartUsageEvent(
        int UsageId,
        int SparePartId,
        /// <summary>Location the spare part was drawn from (recorded on SparePartUsage.Location).</summary>
        string? SourceLocation,
        string VehicleNumber,
        string VehiclePlateA,
        string VehiclePlateE,
        string VehicleLocation,
        /// <summary>Rider currently assigned to the vehicle (may be null).</summary>
        long? AssignedRiderIqamaNo,
        string? AssignedRiderNameAR,
        string? AssignedRiderNameEN,
        int QuantityUsed,
        decimal UnitPriceAtUsage,
        decimal TotalCost,
        DateTime UsedAt
    );

    public record AccessoryUsageEvent(
        int UsageId,
        int AccessoryId,
        string? SourceLocation,
        int RiderId,
        long RiderIqamaNo,
        string RiderNameAR,
        string RiderNameEN,
        string WorkingId,
        /// <summary>Housing the rider belongs to.</summary>
        string? RiderHousing,
        decimal PriceAtIssuance,
        DateTime IssuedAt
    );
}