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

    // ─────────────────────────────────────────────────────────────────────
    // Endpoint 3 – SYNC PRICES FROM EXCEL (writes)
    // Reads each row (Name, Price, optional Type) and updates the Price on
    // every matching record across ALL locations in the database.
    //   • SparePart and RiderAccessory are both handled.
    //   • When Type column is absent the service auto-detects (SP preferred).
    //   • Rows whose name is not found anywhere are reported as NotFound.
    // ─────────────────────────────────────────────────────────────────────
    Task<Result<SyncPriceFromExcelResponse>> SyncPricesFromExcelAsync(
        IFormFile file,
        string syncedBy);


    /// <summary>
    /// Copies prices from the main company stock (الشركة) to every housing
    /// location that holds the same item BUT at a different price.
    /// Only records whose price differs from the الشركة master are updated.
    /// Items that exist only in housings (not in الشركة) are skipped and reported.
    /// </summary>
    Task<Result<SyncPricesFromCompanyStockResponse>> SyncPricesFromCompanyStockAsync(string syncedBy);

    public record SyncPricesFromCompanyStockResponse(
        int TotalCompanyStockItems,           // distinct names in الشركة
        int SparePartsUpdated,                // housing spare part records changed
        int AccessoriesUpdated,               // housing accessory records changed
        int AlreadyInSync,                    // records that already matched
        int NotFoundInHousings,               // company items with no housing copies
        List<CompanyStockSyncDetail> Details,
        DateTime ProcessedAt
    );

    public record CompanyStockSyncDetail(
        string ItemName,
        InventoryItemType ItemType,
        decimal CompanyStockPrice,            // the master price from الشركة
        List<HousingPriceUpdate> HousingUpdates
    );

    public record HousingPriceUpdate(
        int ItemId,
        string Location,
        decimal OldPrice,
        decimal NewPrice,
        bool WasUpdated                       // false when price already matched
    );

    public record SyncPriceFromExcelResponse(
        int TotalRowsInExcel,
        int SparePartRowsSynced,
        int AccessoryRowsSynced,
        int TotalRecordsUpdated,   // individual DB rows touched (one name = many locations)
        int NotFound,
        int ValidationErrors,
        List<SyncPriceRowResult> Results,
        List<string> ProcessingErrors,
        DateTime ProcessedAt
    );

    public record SyncPriceRowResult(
        int RowNumber,
        string ItemName,
        InventoryItemType? ItemType,
        decimal? NewPrice,
        SyncPriceAction Action,
        List<SyncPriceDetail> UpdatedRecords,  // one entry per DB row touched
        string? ErrorMessage
    );

    public record SyncPriceDetail(
        int Id,
        string Location,
        decimal OldPrice,
        decimal NewPrice
    );

    public enum SyncPriceAction
    {
        Updated = 1,       // at least one record had its price changed
        NoChange = 2,      // all matching records already had this price
        NotFound = 3,      // name not in DB at all
        ValidationError = 4
    }

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