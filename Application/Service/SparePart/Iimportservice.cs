using Application.Abstraction;
using Microsoft.AspNetCore.Http;

namespace Application.Service.HousingInventory;

public interface IHousingInventorySyncService
{
    // ─────────────────────────────────────────────────────────────────────
    // Endpoint 1 – CHECK ONLY (no writes)
    // Reads every row in the Excel and tells you whether each item exists
    // anywhere in the database (all locations), what its current quantities
    // are across locations, and whether it is a SparePart, an Accessory, or
    // neither (not found at all).
    // ─────────────────────────────────────────────────────────────────────
    Task<Result<HousingInventoryCheckResponse>> CheckInventoryFromExcelAsync(
        IFormFile file,
        string checkedBy);

    // ─────────────────────────────────────────────────────────────────────
    // Endpoint 2 – SYNC (writes)
    // Sets the quantity of every item listed in the Excel at the given
    // housing location.
    //   • Empty / 0 cell  → quantity becomes 0 for that housing record
    //   • Item found in DB but no housing record yet → new record created
    //     at the housing location (mirrors TransferService pattern)
    //   • Item NOT found anywhere in DB → reported as not found, skipped
    // ─────────────────────────────────────────────────────────────────────
    Task<Result<HousingInventorySyncResponse>> SyncHousingInventoryFromExcelAsync(
        IFormFile file,
        int housingId,
        string syncedBy);

    // ── Response records ──────────────────────────────────────────────────

    public record HousingInventoryCheckResponse(
        int TotalRowsInExcel,
        int SparePartsFound,
        int AccessoriesFound,
        int NotFound,
        int ValidationErrors,
        List<string> NotFoundNames,          // ← ADD THIS
        List<HousingInventoryCheckRowResult> Results,
        List<string> ProcessingErrors,
        DateTime ProcessedAt
    );

    public record HousingInventoryCheckRowResult(
        int RowNumber,
        string ItemName,
        InventoryItemType? DetectedType,   // null when not found
        bool FoundInSpareParts,
        bool FoundInAccessories,
        int ExcelQuantity,                 // 0 when cell was empty
        List<ItemLocationStock> CurrentStock,  // all locations this item has stock in
        string? ErrorMessage
    );

    /// <summary>One row per (Name, Location) combination found in the DB.</summary>
    public record ItemLocationStock(
        int ItemId,
        string Location,
        int CurrentQuantity,
        decimal UnitPrice
    );

    // ─────────────────────────────────────────────────────────────────────

    public record HousingInventorySyncResponse(
        int HousingId,
        string HousingName,
        int TotalRowsInExcel,
        int SparePartsSynced,
        int AccessoriesSynced,
        int RecordsCreated,      // new housing-location entries
        int RecordsUpdated,      // existing housing-location entries
        int ZeroedOut,           // set to 0 (Excel cell was empty / 0)
        int NotFound,            // item name not found anywhere in DB
        int ValidationErrors,
        List<HousingInventorySyncRowResult> Results,
        List<string> ProcessingErrors,
        DateTime ProcessedAt
    );

    public record HousingInventorySyncRowResult(
        int RowNumber,
        string ItemName,
        InventoryItemType? ItemType,
        int ExcelQuantity,
        int? PreviousQuantity,   // null when record was created from scratch
        int NewQuantity,
        SyncAction Action,
        string? ErrorMessage
    );

    public enum InventoryItemType
    {
        SparePart = 1,
        Accessory = 2
    }

    public enum SyncAction
    {
        Created = 1,    // new housing-location record inserted
        Updated = 2,    // existing housing-location record quantity changed
        ZeroedOut = 3,  // quantity set to 0 (was non-zero before, or newly created at 0)
        NoChange = 4,   // quantity was already equal to Excel value
        NotFound = 5,   // item name not in DB at all
        ValidationError = 6
    }
}