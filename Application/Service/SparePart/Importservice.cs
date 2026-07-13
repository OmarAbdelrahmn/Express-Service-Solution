using Application.Abstraction;
using ClosedXML.Excel;
using Domain;
using Domain.Entities.Spare;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static Application.Service.HousingInventory.IHousingInventorySyncService;
using InventoryItemType = Application.Service.HousingInventory.IHousingInventorySyncService.InventoryItemType;

namespace Application.Service.HousingInventory;

/// <summary>
/// Provides two operations driven by an Excel file:
///
///   1. CheckInventoryFromExcelAsync  – read-only scan; tells the caller whether
///      each item exists anywhere in the database and what stock levels look like.
///
///   2. SyncHousingInventoryFromExcelAsync – write operation; sets the quantity of
///      every listed item at the specified housing location.  If the housing does
///      not yet have a record for that item a new one is created (same pattern as
///      TransferService).  An empty / zero cell means the housing quantity should
///      be zero.
///
/// Excel format (both endpoints share the same layout):
///   Column A  – Item name  (required)
///   Column B  – Quantity   (integer ≥ 0; blank / missing = 0)
///   Column C  – Type       (optional: "SparePart" / "قطعة غيار" / "Accessory" / "اكسسوار")
///               When omitted the service auto-detects by searching both tables.
/// </summary>
public class HousingInventorySyncService(ApplicationDbcontext dbContext) : IHousingInventorySyncService
{
    private readonly ApplicationDbcontext _db = dbContext;

    // ══════════════════════════════════════════════════════════════════════
    //  ENDPOINT 1 – CHECK (read-only)
    // ══════════════════════════════════════════════════════════════════════

    private const string COMPANY_STOCK = "الشركة";

    public async Task<Result<SyncPricesFromCompanyStockResponse>> SyncPricesFromCompanyStockAsync(
        string syncedBy)
    {
        try
        {
            Console.WriteLine($"[SyncPricesFromCompanyStock] Started by: {syncedBy}");

            // ── 1. Load ALL spare parts and accessories in one shot ────────────
            var allSpareParts = await _db.SpareParts.ToListAsync();
            var allAccessories = await _db.RiderAccessories.ToListAsync();

            // ── 2. Separate company-stock master records from housing records ──
            var companySpParts = allSpareParts
                .Where(sp => sp.Location == COMPANY_STOCK)
                .ToList();

            var companyAccessories = allAccessories
                .Where(a => a.Location == COMPANY_STOCK)
                .ToList();

            Console.WriteLine(
                $"[SyncPricesFromCompanyStock] Company stock: " +
                $"{companySpParts.Count} spare parts, {companyAccessories.Count} accessories.");

            if (!companySpParts.Any() && !companyAccessories.Any())
                return Result.Success(new SyncPricesFromCompanyStockResponse(
                    0, 0, 0, 0, 0, [], DateTime.UtcNow.AddHours(3)));

            // ── 3. Build name → company price lookups ──────────────────────────
            // When الشركة has multiple records for the same name (shouldn't happen
            // but defensive), use the first one found.
            var companySpPriceByName = companySpParts
                .GroupBy(sp => sp.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Price);

            var companyAcPriceByName = companyAccessories
                .GroupBy(a => a.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Price);

            // ── 4. Separate housing records (everything that is NOT الشركة) ────
            var housingSpParts = allSpareParts
                .Where(sp => sp.Location != COMPANY_STOCK)
                .ToList();

            var housingAccessories = allAccessories
                .Where(a => a.Location != COMPANY_STOCK)
                .ToList();

            // ── 5. Group housing records by name for easy lookup ───────────────
            var housingSpByName = housingSpParts
                .GroupBy(sp => sp.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            var housingAcByName = housingAccessories
                .GroupBy(a => a.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            var details = new List<CompanyStockSyncDetail>();
            int spUpdated = 0, acUpdated = 0, alreadyInSync = 0, notFoundInHousings = 0;

            // ── 6. Process spare parts ─────────────────────────────────────────
            foreach (var (nameKey, companyPrice) in companySpPriceByName)
            {
                if (!housingSpByName.TryGetValue(nameKey, out var housingRecords))
                {
                    // This item exists in الشركة but has no housing copies at all.
                    notFoundInHousings++;
                    details.Add(new CompanyStockSyncDetail(
                        companySpParts.First(sp => sp.Name.Trim().ToLowerInvariant() == nameKey).Name,
                        InventoryItemType.SparePart,
                        companyPrice,
                        []
                    ));
                    continue;
                }

                var housingUpdates = new List<HousingPriceUpdate>();

                foreach (var record in housingRecords)
                {
                    bool pricesDiffer = record.Price != companyPrice;

                    housingUpdates.Add(new HousingPriceUpdate(
                        record.Id,
                        record.Location,
                        record.Price,
                        companyPrice,
                        pricesDiffer
                    ));

                    if (pricesDiffer)
                    {
                        record.Price = companyPrice;
                        spUpdated++;
                    }
                    else
                    {
                        alreadyInSync++;
                    }
                }

                details.Add(new CompanyStockSyncDetail(
                    housingRecords.First().Name,
                    InventoryItemType.SparePart,
                    companyPrice,
                    housingUpdates
                ));
            }

            // ── 7. Process accessories ─────────────────────────────────────────
            foreach (var (nameKey, companyPrice) in companyAcPriceByName)
            {
                if (!housingAcByName.TryGetValue(nameKey, out var housingRecords))
                {
                    notFoundInHousings++;
                    details.Add(new CompanyStockSyncDetail(
                        companyAccessories.First(a => a.Name.Trim().ToLowerInvariant() == nameKey).Name,
                        InventoryItemType.Accessory,
                        companyPrice,
                        []
                    ));
                    continue;
                }

                var housingUpdates = new List<HousingPriceUpdate>();

                foreach (var record in housingRecords)
                {
                    bool pricesDiffer = record.Price != companyPrice;

                    housingUpdates.Add(new HousingPriceUpdate(
                        record.Id,
                        record.Location,
                        record.Price,
                        companyPrice,
                        pricesDiffer
                    ));

                    if (pricesDiffer)
                    {
                        record.Price = companyPrice;
                        acUpdated++;
                    }
                    else
                    {
                        alreadyInSync++;
                    }
                }

                details.Add(new CompanyStockSyncDetail(
                    housingRecords.First().Name,
                    InventoryItemType.Accessory,
                    companyPrice,
                    housingUpdates
                ));
            }

            // ── 8. Persist all changes in a single SaveChanges call ────────────
            await _db.SaveChangesAsync();

            int totalCompanyItems = companySpPriceByName.Count + companyAcPriceByName.Count;

            Console.WriteLine($"[SyncPricesFromCompanyStock] Complete:");
            Console.WriteLine($"  CompanyStockItems : {totalCompanyItems}");
            Console.WriteLine($"  SparePartsUpdated : {spUpdated}");
            Console.WriteLine($"  AccessoriesUpdated: {acUpdated}");
            Console.WriteLine($"  AlreadyInSync     : {alreadyInSync}");
            Console.WriteLine($"  NotFoundInHousings: {notFoundInHousings}");

            return Result.Success(new SyncPricesFromCompanyStockResponse(
                totalCompanyItems,
                spUpdated,
                acUpdated,
                alreadyInSync,
                notFoundInHousings,
                details.OrderBy(d => d.ItemName).ToList(),
                DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SyncPricesFromCompanyStock] FATAL: {ex}");
            return Result.Failure<SyncPricesFromCompanyStockResponse>(
                new Error("SyncError",
                    $"Failed to sync prices from company stock: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ENDPOINT 3 – SYNC PRICES FROM EXCEL (writes)
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<SyncPriceFromExcelResponse>> SyncPricesFromExcelAsync(
        IFormFile file,
        string syncedBy)
    {
        var validationError = ValidateFile(file);
        if (validationError != null)
            return Result.Failure<SyncPriceFromExcelResponse>(validationError);

        var rowResults = new List<SyncPriceRowResult>();
        var processingErrors = new List<string>();

        int spRowsSynced = 0, acRowsSynced = 0;
        int totalRecordsUpdated = 0, notFoundCount = 0, validationErrors = 0;

        try
        {
            Console.WriteLine($"[SyncPricesFromExcel] Starting for file: {file.FileName}");

            // ── Pre-load entire inventory into memory (same pattern as Endpoint 2) ──
            var allSpareParts = await _db.SpareParts.ToListAsync();
            var allAccessories = await _db.RiderAccessories.ToListAsync();

            var spByName = allSpareParts
                .GroupBy(sp => sp.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            var acByName = allAccessories
                .GroupBy(a => a.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            Console.WriteLine(
                $"[SyncPricesFromExcel] Loaded {allSpareParts.Count} spare part records " +
                $"({spByName.Count} unique names) and " +
                $"{allAccessories.Count} accessory records ({acByName.Count} unique names).");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet(1);
            if (ws == null)
                return Result.Failure<SyncPriceFromExcelResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            var headerRow = FindPriceHeaderRow(ws);
            if (headerRow == null)
                return Result.Failure<SyncPriceFromExcelResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            var colMap = BuildPriceColumnMapping(headerRow);
            if (!colMap.IsValid)
                return Result.Failure<SyncPriceFromExcelResponse>(
                    new Error("InvalidColumns", colMap.ErrorMessage!, 400));

            var dataRows = ws.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            int totalRows = dataRows.Count;
            Console.WriteLine($"[SyncPricesFromExcel] Data rows: {totalRows}");

            if (totalRows == 0)
                return Result.Failure<SyncPriceFromExcelResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            int rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var parsed = ParsePriceRow(row, colMap, rowNumber);

                    if (!parsed.IsValid)
                    {
                        validationErrors++;
                        rowResults.Add(new SyncPriceRowResult(
                            rowNumber, parsed.Name ?? "N/A",
                            null, parsed.Price,
                            SyncPriceAction.ValidationError, [],
                            parsed.ErrorMessage));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    var nameKey = parsed.Name!.Trim().ToLowerInvariant();
                    decimal newPrice = parsed.Price!.Value;

                    // ── Resolve type (honours explicit column-C hint) ──────────────
                    InventoryItemType? itemType = ResolveType(parsed.TypeHint, nameKey, spByName, acByName);

                    if (itemType == null)
                    {
                        notFoundCount++;
                        rowResults.Add(new SyncPriceRowResult(
                            rowNumber, parsed.Name!, null, newPrice,
                            SyncPriceAction.NotFound, [],
                            "Item name not found in spare parts or accessories"));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    var updatedDetails = new List<SyncPriceDetail>();

                    // ── SPARE PART branch ─────────────────────────────────────────
                    if (itemType == InventoryItemType.SparePart)
                    {
                        if (!spByName.TryGetValue(nameKey, out var spRecords))
                        {
                            notFoundCount++;
                            rowResults.Add(new SyncPriceRowResult(
                                rowNumber, parsed.Name!, itemType, newPrice,
                                SyncPriceAction.NotFound, [],
                                "Spare part name not found"));
                            await transaction.RollbackAsync();
                            continue;
                        }

                        foreach (var record in spRecords)
                        {
                            updatedDetails.Add(new SyncPriceDetail(
                                record.Id, record.Location, record.Price, newPrice));
                            record.Price = newPrice;
                        }

                        await _db.SaveChangesAsync();
                        await transaction.CommitAsync();

                        int changed = updatedDetails.Count(d => d.OldPrice != d.NewPrice);
                        totalRecordsUpdated += changed;
                        spRowsSynced++;

                        var action = changed > 0 ? SyncPriceAction.Updated : SyncPriceAction.NoChange;

                        rowResults.Add(new SyncPriceRowResult(
                            rowNumber, parsed.Name!, itemType, newPrice,
                            action, updatedDetails, null));

                        Console.WriteLine(
                            $"[SyncPricesFromExcel] SparePart '{parsed.Name}' → {newPrice} " +
                            $"({spRecords.Count} records, {changed} changed)");
                    }
                    // ── ACCESSORY branch ──────────────────────────────────────────
                    else
                    {
                        if (!acByName.TryGetValue(nameKey, out var acRecords))
                        {
                            notFoundCount++;
                            rowResults.Add(new SyncPriceRowResult(
                                rowNumber, parsed.Name!, itemType, newPrice,
                                SyncPriceAction.NotFound, [],
                                "Accessory name not found"));
                            await transaction.RollbackAsync();
                            continue;
                        }

                        foreach (var record in acRecords)
                        {
                            updatedDetails.Add(new SyncPriceDetail(
                                record.Id, record.Location, record.Price, newPrice));
                            record.Price = newPrice;
                        }

                        await _db.SaveChangesAsync();
                        await transaction.CommitAsync();

                        int changed = updatedDetails.Count(d => d.OldPrice != d.NewPrice);
                        totalRecordsUpdated += changed;
                        acRowsSynced++;

                        var action = changed > 0 ? SyncPriceAction.Updated : SyncPriceAction.NoChange;

                        rowResults.Add(new SyncPriceRowResult(
                            rowNumber, parsed.Name!, itemType, newPrice,
                            action, updatedDetails, null));

                        Console.WriteLine(
                            $"[SyncPricesFromExcel] Accessory '{parsed.Name}' → {newPrice} " +
                            $"({acRecords.Count} records, {changed} changed)");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    validationErrors++;
                    processingErrors.Add($"Row {rowNumber}: {ex.Message}");
                    rowResults.Add(new SyncPriceRowResult(
                        rowNumber, "N/A", null, null,
                        SyncPriceAction.ValidationError, [],
                        $"Exception: {ex.Message}"));
                    Console.WriteLine($"[SyncPricesFromExcel] ERROR row {rowNumber}: {ex.Message}");
                }
            }

            Console.WriteLine($"[SyncPricesFromExcel] Complete:");
            Console.WriteLine($"  SparePartRows:    {spRowsSynced}");
            Console.WriteLine($"  AccessoryRows:    {acRowsSynced}");
            Console.WriteLine($"  RecordsUpdated:   {totalRecordsUpdated}");
            Console.WriteLine($"  NotFound:         {notFoundCount}");
            Console.WriteLine($"  Errors:           {validationErrors}");

            return Result.Success(new SyncPriceFromExcelResponse(
                totalRows,
                spRowsSynced, acRowsSynced,
                totalRecordsUpdated,
                notFoundCount, validationErrors,
                rowResults, processingErrors,
                DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SyncPricesFromExcel] FATAL: {ex}");
            return Result.Failure<SyncPriceFromExcelResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    public async Task<Result<HousingInventoryCheckResponse>> CheckInventoryFromExcelAsync(
        IFormFile file,
        string checkedBy)
    {
        var validationError = ValidateFile(file);
        if (validationError != null)
            return Result.Failure<HousingInventoryCheckResponse>(validationError);

        var rowResults = new List<HousingInventoryCheckRowResult>();
        var processingErrors = new List<string>();

        int sparePartsFound = 0, accessoriesFound = 0, notFound = 0, validationErrors = 0;
        // add this line
        var notFoundNames = new List<string>();

        try
        {
            Console.WriteLine($"[HousingInventoryCheck] Starting check for file: {file.FileName}");

            // ── Load entire inventory into memory for O(1) lookups ────────
            var allSpareParts = await _db.SpareParts
                .AsNoTracking()
                .ToListAsync();

            var allAccessories = await _db.RiderAccessories
                .AsNoTracking()
                .ToListAsync();

            // Group by name (lower-case) so we can find all location records for an item
            var sparePartsByName = allSpareParts
                .GroupBy(sp => sp.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            var accessoriesByName = allAccessories
                .GroupBy(a => a.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            Console.WriteLine(
                $"[HousingInventoryCheck] Loaded {allSpareParts.Count} spare part records " +
                $"({sparePartsByName.Count} unique names) and " +
                $"{allAccessories.Count} accessory records ({accessoriesByName.Count} unique names).");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet(1);
            if (ws == null)
                return Result.Failure<HousingInventoryCheckResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            var headerRow = FindInventoryHeaderRow(ws);
            if (headerRow == null)
                return Result.Failure<HousingInventoryCheckResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            var colMap = BuildInventoryColumnMapping(headerRow);
            if (!colMap.IsValid)
                return Result.Failure<HousingInventoryCheckResponse>(
                    new Error("InvalidColumns", colMap.ErrorMessage!, 400));

            var dataRows = ws.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            int totalRows = dataRows.Count;
            Console.WriteLine($"[HousingInventoryCheck] Data rows: {totalRows}");

            if (totalRows == 0)
                return Result.Failure<HousingInventoryCheckResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            int rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                try
                {
                    var parsed = ParseInventoryRow(row, colMap, rowNumber);

                    if (!parsed.IsValid)
                    {
                        validationErrors++;
                        rowResults.Add(new HousingInventoryCheckRowResult(
                            rowNumber, parsed.Name ?? "N/A",
                            null, false, false,
                            parsed.Quantity, [],
                            parsed.ErrorMessage));
                        continue;
                    }

                    var nameKey = parsed.Name!.Trim().ToLowerInvariant();

                    bool foundSP = sparePartsByName.TryGetValue(nameKey, out var spRecords);
                    bool foundAC = accessoriesByName.TryGetValue(nameKey, out var acRecords);

                    var stock = new List<ItemLocationStock>();

                    if (foundSP)
                    {
                        stock.AddRange(spRecords!.Select(sp => new ItemLocationStock(
                            sp.Id, sp.Location, sp.Quantity, sp.Price)));
                    }

                    if (foundAC)
                    {
                        stock.AddRange(acRecords!.Select(a => new ItemLocationStock(
                            a.Id, a.Location, a.Quantity, a.Price)));
                    }

                    InventoryItemType? detectedType = null;
                    if (foundSP && !foundAC) { detectedType = InventoryItemType.SparePart; sparePartsFound++; }
                    else if (foundAC && !foundSP) { detectedType = InventoryItemType.Accessory; accessoriesFound++; }
                    else if (foundSP && foundAC) { detectedType = InventoryItemType.SparePart; sparePartsFound++; } // prefer SP when both match
                    
                    else
                    {
                        notFound++;
                        notFoundNames.Add(parsed.Name!);   // ← ADD THIS
                    }
                    rowResults.Add(new HousingInventoryCheckRowResult(
                        rowNumber, parsed.Name!,
                        detectedType, foundSP, foundAC,
                        parsed.Quantity, stock,
                        null));
                }
                catch (Exception ex)
                {
                    validationErrors++;
                    processingErrors.Add($"Row {rowNumber}: {ex.Message}");
                    rowResults.Add(new HousingInventoryCheckRowResult(
                        rowNumber, "N/A",
                        null, false, false,
                        0, [],
                        $"Exception: {ex.Message}"));
                }
            }

            Console.WriteLine($"[HousingInventoryCheck] Complete: " +
                              $"SparePartsFound={sparePartsFound}, AccessoriesFound={accessoriesFound}, " +
                              $"NotFound={notFound}, Errors={validationErrors}");
          
            return Result.Success(new HousingInventoryCheckResponse(
                totalRows, sparePartsFound, accessoriesFound,
                notFound, validationErrors,
                notFoundNames,                  // ← ADD THIS
                rowResults, processingErrors,
                DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HousingInventoryCheck] FATAL: {ex}");
            return Result.Failure<HousingInventoryCheckResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Price-specific header detection
    // Name column  → same variants as inventory check
    // Price column → "Price" / "السعر" / "Unit Price" / "سعر الوحدة" / "p"
    // Type column  → same optional variants as inventory sync
    // ─────────────────────────────────────────────────────────────────────
    private static IXLRow? FindPriceHeaderRow(IXLWorksheet ws)
    {
        var nameVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "name", "الاسم", "اسم القطعة", "اسم العنصر", "اسم المنتج",
        "part name", "item name", "a"
    };

        var priceVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "price", "السعر", "unit price", "سعر الوحدة", "سعر", "p"
    };

        for (int i = 1; i <= Math.Min(10, ws.RowsUsed().Count()); i++)
        {
            var row = ws.Row(i);
            var values = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            bool hasName = values.Any(v => nameVariants.Contains(v));
            bool hasPrice = values.Any(v => priceVariants.Contains(v));

            if (hasName || hasPrice)
                return row;
        }

        return ws.Row(1);
    }

    private static PriceColumnMapping BuildPriceColumnMapping(IXLRow headerRow)
    {
        var mapping = new PriceColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.NameCol = FindCol(cells,
            "name", "الاسم", "اسم القطعة", "اسم العنصر", "اسم المنتج",
            "part name", "item name", "a");

        mapping.PriceCol = FindCol(cells,
            "price", "السعر", "unit price", "سعر الوحدة", "سعر", "p");

        mapping.TypeCol = FindCol(cells,
            "type", "النوع", "item type", "نوع العنصر", "c");

        var missing = new List<string>();
        if (mapping.NameCol == 0) missing.Add("Name / الاسم");
        if (mapping.PriceCol == 0) missing.Add("Price / السعر");

        mapping.IsValid = !missing.Any();
        mapping.ErrorMessage = missing.Any()
            ? $"Required columns not found: {string.Join(", ", missing)}"
            : null;

        return mapping;
    }

    private static PriceRowData ParsePriceRow(
        IXLRow row,
        PriceColumnMapping map,
        int rowNumber)
    {
        var data = new PriceRowData { RowNumber = rowNumber };

        try
        {
            data.Name = GetCell(row, map.NameCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.Name))
            {
                data.IsValid = false;
                data.ErrorMessage = "Item name is required";
                return data;
            }

            var priceStr = GetCell(row, map.PriceCol);
            if (string.IsNullOrWhiteSpace(priceStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Price is required";
                return data;
            }

            var clean = priceStr.Trim().Replace(",", "").Replace(" ", "");
            if (!decimal.TryParse(clean, out decimal price))
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid price value: '{priceStr}'";
                return data;
            }

            if (price < 0)
            {
                data.IsValid = false;
                data.ErrorMessage = "Price cannot be negative";
                return data;
            }

            data.Price = price;

            // Optional type hint (column C) — reuses the same token logic
            var typeStr = GetCell(row, map.TypeCol)?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(typeStr))
            {
                if (typeStr.Contains("spare") || typeStr.Contains("قطعة") || typeStr == "1")
                    data.TypeHint = InventoryItemType.SparePart;
                else if (typeStr.Contains("access") || typeStr.Contains("اكسسوار") ||
                         typeStr.Contains("ملحق") || typeStr == "2")
                    data.TypeHint = InventoryItemType.Accessory;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    internal class PriceColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int NameCol { get; set; }
        public int PriceCol { get; set; }
        public int TypeCol { get; set; } // 0 = absent
    }

    internal class PriceRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Name { get; set; }
        public decimal? Price { get; set; }
        public InventoryItemType? TypeHint { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ENDPOINT 2 – SYNC (writes)
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<HousingInventorySyncResponse>> SyncHousingInventoryFromExcelAsync(
        IFormFile file,
        int housingId,
        string syncedBy)
    {
        var validationError = ValidateFile(file);
        if (validationError != null)
            return Result.Failure<HousingInventorySyncResponse>(validationError);

        // ── Resolve housing ───────────────────────────────────────────────
        var housing = await _db.Housings.FirstOrDefaultAsync(h => h.Id == housingId);
        if (housing == null)
            return Result.Failure<HousingInventorySyncResponse>(
                new Error("HousingNotFound", $"Housing with ID {housingId} not found", 404));

        string housingName = housing.Name;

        Console.WriteLine($"[HousingInventorySync] Target housing: [{housingId}] {housingName}");

        var rowResults = new List<HousingInventorySyncRowResult>();
        var processingErrors = new List<string>();

        int sparePartsSynced = 0, accessoriesSynced = 0;
        int recordsCreated = 0, recordsUpdated = 0, zeroedOut = 0, unchangedCount = 0;
        int notFoundCount = 0, validationErrors = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet(1);
            if (ws == null)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            var headerRow = FindInventoryHeaderRow(ws);
            if (headerRow == null)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            var colMap = BuildInventoryColumnMapping(headerRow);
            if (!colMap.IsValid)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("InvalidColumns", colMap.ErrorMessage!, 400));

            var dataRows = ws.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            int totalRows = dataRows.Count;
            Console.WriteLine($"[HousingInventorySync] Data rows: {totalRows}");

            if (totalRows == 0)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            // ── Pre-load all inventory for fast lookups ───────────────────
            // Spare parts – all locations (we need both housing + "الشركة")
            var allSpareParts = await _db.SpareParts.ToListAsync();
            var spByNameAndLocation = allSpareParts
                .GroupBy(sp => (sp.Name.Trim().ToLowerInvariant(), sp.Location.Trim()))
                .ToDictionary(g => g.Key, g => g.First());

            var spByName = allSpareParts
                .GroupBy(sp => sp.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            // Accessories – all locations
            var allAccessories = await _db.RiderAccessories.ToListAsync();
            var acByNameAndLocation = allAccessories
                .GroupBy(a => (a.Name.Trim().ToLowerInvariant(), a.Location.Trim()))
                .ToDictionary(g => g.Key, g => g.First());

            var acByName = allAccessories
                .GroupBy(a => a.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            int rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var parsed = ParseInventoryRow(row, colMap, rowNumber);

                    if (!parsed.IsValid)
                    {
                        validationErrors++;
                        rowResults.Add(new HousingInventorySyncRowResult(
                            rowNumber, parsed.Name ?? "N/A",
                            null, parsed.Quantity, null, 0,
                            SyncAction.ValidationError, parsed.ErrorMessage));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    var nameKey = parsed.Name!.Trim().ToLowerInvariant();
                    int targetQty = parsed.Quantity;

                    // ── Auto-detect type (honouring explicit hint from column C) ──
                    InventoryItemType? itemType = ResolveType(parsed.TypeHint, nameKey, spByName, acByName);

                    if (itemType == null)
                    {
                        notFoundCount++;
                        rowResults.Add(new HousingInventorySyncRowResult(
                            rowNumber, parsed.Name!,
                            null, targetQty, null, 0,
                            SyncAction.NotFound,
                            "Item name not found in spare parts or accessories"));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // ─────────────────────────────────────────────────────
                    // SPARE PART branch
                    // ─────────────────────────────────────────────────────
                    if (itemType == InventoryItemType.SparePart)
                    {
                        var housingKey = (nameKey, housingName.Trim());
                       Domain.Entities.Spare.SparePart? housingRecord;

                        if (spByNameAndLocation.TryGetValue(housingKey, out housingRecord))
                        {
                            // Record for this housing already exists → update qty
                            int previousQty = housingRecord.Quantity;

                            if (previousQty == targetQty)
                            {
                                unchangedCount++;
                                rowResults.Add(new HousingInventorySyncRowResult(
                                    rowNumber, parsed.Name!, itemType,
                                    targetQty, previousQty, targetQty,
                                    SyncAction.NoChange, null));
                                await transaction.CommitAsync();
                                sparePartsSynced++;
                                continue;
                            }

                            housingRecord.Quantity = targetQty;

                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            var action = targetQty == 0 ? SyncAction.ZeroedOut : SyncAction.Updated;
                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsUpdated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, previousQty, targetQty,
                                action, null));

                            Console.WriteLine(
                                $"[HousingInventorySync] SparePart '{parsed.Name}' @ {housingName}: " +
                                $"{previousQty} → {targetQty}");
                        }
                        else
                        {
                            // No housing record yet → create one mirroring TransferService pattern
                            // Grab unit price from any existing record for this item
                            decimal price = spByName.TryGetValue(nameKey, out var existingRecords)
                                ? existingRecords.First().Price
                                : 0m;

                            var newRecord = new Domain.Entities.Spare.SparePart
                            {
                                Name = parsed.Name!,
                                Quantity = targetQty,
                                Price = price,
                                Location = housingName,
                                CreatedAt = DateTime.UtcNow.AddHours(3)
                            };

                            await _db.SpareParts.AddAsync(newRecord);
                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            // Keep in-memory map in sync for subsequent rows in same file
                            spByNameAndLocation[housingKey] = newRecord;

                            var action = targetQty == 0 ? SyncAction.ZeroedOut : SyncAction.Created;
                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsCreated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, null, targetQty,
                                action, null));

                            Console.WriteLine(
                                $"[HousingInventorySync] SparePart '{parsed.Name}' @ {housingName}: " +
                                $"created with qty={targetQty}");
                        }

                        sparePartsSynced++;
                    }
                    // ─────────────────────────────────────────────────────
                    // ACCESSORY branch
                    // ─────────────────────────────────────────────────────
                    else // InventoryItemType.Accessory
                    {
                        var housingKey = (nameKey, housingName.Trim());
                        Domain.Entities.Spare.RiderAccessory? housingRecord;

                        if (acByNameAndLocation.TryGetValue(housingKey, out housingRecord))
                        {
                            int previousQty = housingRecord.Quantity;

                            if (previousQty == targetQty)
                            {
                                unchangedCount++;
                                rowResults.Add(new HousingInventorySyncRowResult(
                                    rowNumber, parsed.Name!, itemType,
                                    targetQty, previousQty, targetQty,
                                    SyncAction.NoChange, null));
                                await transaction.CommitAsync();
                                accessoriesSynced++;
                                continue;
                            }

                            housingRecord.Quantity = targetQty;

                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            var action = targetQty == 0 ? SyncAction.ZeroedOut : SyncAction.Updated;
                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsUpdated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, previousQty, targetQty,
                                action, null));

                            Console.WriteLine(
                                $"[HousingInventorySync] Accessory '{parsed.Name}' @ {housingName}: " +
                                $"{previousQty} → {targetQty}");
                        }
                        else
                        {
                            decimal price = acByName.TryGetValue(nameKey, out var existingRecords)
                                ? existingRecords.First().Price
                                : 0m;

                            var newRecord = new Domain.Entities.Spare.RiderAccessory
                            {
                                Name = parsed.Name!,
                                Quantity = targetQty,
                                Price = price,
                                Location = housingName,
                                CreatedAt = DateTime.UtcNow.AddHours(3)
                            };

                            await _db.RiderAccessories.AddAsync(newRecord);
                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            acByNameAndLocation[housingKey] = newRecord;

                            var action = targetQty == 0 ? SyncAction.ZeroedOut : SyncAction.Created;
                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsCreated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, null, targetQty,
                                action, null));

                            Console.WriteLine(
                                $"[HousingInventorySync] Accessory '{parsed.Name}' @ {housingName}: " +
                                $"created with qty={targetQty}");
                        }

                        accessoriesSynced++;
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    validationErrors++;
                    processingErrors.Add($"Row {rowNumber}: {ex.Message}");
                    rowResults.Add(new HousingInventorySyncRowResult(
                        rowNumber, "N/A", null, 0, null, 0,
                        SyncAction.ValidationError, $"Exception: {ex.Message}"));
                    Console.WriteLine($"[HousingInventorySync] ERROR row {rowNumber}: {ex.Message}");
                }
            }

            Console.WriteLine($"[HousingInventorySync] Sync complete for housing [{housingId}] {housingName}:");
            Console.WriteLine($"  SparePartsSynced: {sparePartsSynced}");
            Console.WriteLine($"  AccessoriesSynced: {accessoriesSynced}");
            Console.WriteLine($"  Created:          {recordsCreated}");
            Console.WriteLine($"  Updated:          {recordsUpdated}");
            Console.WriteLine($"  ZeroedOut:        {zeroedOut}");
            Console.WriteLine($"  NoChange:         {unchangedCount}");
            Console.WriteLine($"  NotFound:         {notFoundCount}");
            Console.WriteLine($"  Errors:           {validationErrors}");

            return Result.Success(new HousingInventorySyncResponse(
                housingId, housingName,
                dataRows.Count,
                sparePartsSynced, accessoriesSynced,
                recordsCreated, recordsUpdated,
                zeroedOut, notFoundCount, validationErrors,
                rowResults, processingErrors,
                DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HousingInventorySync] FATAL: {ex}");
            return Result.Failure<HousingInventorySyncResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    public async Task<Result<HousingInventorySyncResponse>> SyncCompanyStockFromExcelAsync(
    IFormFile file,
    string syncedBy)
    {
        var validationError = ValidateFile(file);
        if (validationError != null)
            return Result.Failure<HousingInventorySyncResponse>(validationError);

        // The target is always the company main stock — no housing lookup needed
        const string targetLocation = COMPANY_STOCK; // "الشركة"
        const int fakeHousingId = 0; // no real housing row; we use 0 as a sentinel

        Console.WriteLine($"[SyncCompanyStock] Started by: {syncedBy}, target: {targetLocation}");

        var rowResults = new List<HousingInventorySyncRowResult>();
        var processingErrors = new List<string>();

        int sparePartsSynced = 0, accessoriesSynced = 0;
        int recordsCreated = 0, recordsUpdated = 0;
        int zeroedOut = 0, unchangedCount = 0;
        int notFoundCount = 0, validationErrors = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet(1);

            if (ws == null)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            var headerRow = FindInventoryHeaderRow(ws);
            if (headerRow == null)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            var colMap = BuildInventoryColumnMapping(headerRow);
            if (!colMap.IsValid)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("InvalidColumns", colMap.ErrorMessage!, 400));

            var dataRows = ws.RowsUsed()
                              .Where(r => r.RowNumber() > headerRow.RowNumber())
                              .ToList();

            int totalRows = dataRows.Count;
            Console.WriteLine($"[SyncCompanyStock] Data rows: {totalRows}");

            if (totalRows == 0)
                return Result.Failure<HousingInventorySyncResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            // ── Pre-load ALL inventory once (same pattern as SyncHousingInventory) ──
            var allSpareParts = await _db.SpareParts.ToListAsync();

            var spByNameAndLocation = allSpareParts
                .GroupBy(sp => (sp.Name.Trim().ToLowerInvariant(), sp.Location.Trim()))
                .ToDictionary(g => g.Key, g => g.First());

            var spByName = allSpareParts
                .GroupBy(sp => sp.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            var allAccessories = await _db.RiderAccessories.ToListAsync();

            var acByNameAndLocation = allAccessories
                .GroupBy(a => (a.Name.Trim().ToLowerInvariant(), a.Location.Trim()))
                .ToDictionary(g => g.Key, g => g.First());

            var acByName = allAccessories
                .GroupBy(a => a.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            int rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var parsed = ParseInventoryRow(row, colMap, rowNumber);

                    if (!parsed.IsValid)
                    {
                        validationErrors++;
                        rowResults.Add(new HousingInventorySyncRowResult(
                            rowNumber, parsed.Name ?? "N/A",
                            null, parsed.Quantity, null, 0,
                            SyncAction.ValidationError, parsed.ErrorMessage));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    var nameKey = parsed.Name!.Trim().ToLowerInvariant();
                    int targetQty = parsed.Quantity;

                    InventoryItemType? itemType =
                        ResolveType(parsed.TypeHint, nameKey, spByName, acByName);

                    if (itemType == null)
                    {
                        notFoundCount++;
                        rowResults.Add(new HousingInventorySyncRowResult(
                            rowNumber, parsed.Name!,
                            null, targetQty, null, 0,
                            SyncAction.NotFound,
                            "Item name not found in spare parts or accessories"));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // ── SPARE PART ────────────────────────────────────────────────
                    if (itemType == InventoryItemType.SparePart)
                    {
                        var housingKey = (nameKey, targetLocation.Trim());

                        if (spByNameAndLocation.TryGetValue(housingKey, out var existing))
                        {
                            int prev = existing.Quantity;

                            if (prev == targetQty)
                            {
                                unchangedCount++;
                                rowResults.Add(new HousingInventorySyncRowResult(
                                    rowNumber, parsed.Name!, itemType,
                                    targetQty, prev, targetQty,
                                    SyncAction.NoChange, null));
                                await transaction.CommitAsync();
                                sparePartsSynced++;
                                continue;
                            }

                            existing.Quantity = targetQty;
                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            var action = targetQty == 0
                                ? SyncAction.ZeroedOut
                                : SyncAction.Updated;

                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsUpdated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, prev, targetQty, action, null));

                            Console.WriteLine(
                                $"[SyncCompanyStock] SP '{parsed.Name}': {prev} → {targetQty}");
                        }
                        else
                        {
                            // No الشركة record yet → create one
                            decimal price = spByName.TryGetValue(nameKey, out var anyRecords)
                                ? anyRecords.First().Price
                                : 0m;

                            var newRecord = new Domain.Entities.Spare.SparePart
                            {
                                Name = parsed.Name!,
                                Quantity = targetQty,
                                Price = price,
                                Location = targetLocation,
                                CreatedAt = DateTime.UtcNow.AddHours(3)
                            };

                            await _db.SpareParts.AddAsync(newRecord);
                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            // Keep in-memory map current for the rest of this file
                            spByNameAndLocation[housingKey] = newRecord;

                            var action = targetQty == 0
                                ? SyncAction.ZeroedOut
                                : SyncAction.Created;

                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsCreated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, null, targetQty, action, null));

                            Console.WriteLine(
                                $"[SyncCompanyStock] SP '{parsed.Name}': created qty={targetQty}");
                        }

                        sparePartsSynced++;
                    }
                    // ── ACCESSORY ─────────────────────────────────────────────────
                    else
                    {
                        var housingKey = (nameKey, targetLocation.Trim());

                        if (acByNameAndLocation.TryGetValue(housingKey, out var existing))
                        {
                            int prev = existing.Quantity;

                            if (prev == targetQty)
                            {
                                unchangedCount++;
                                rowResults.Add(new HousingInventorySyncRowResult(
                                    rowNumber, parsed.Name!, itemType,
                                    targetQty, prev, targetQty,
                                    SyncAction.NoChange, null));
                                await transaction.CommitAsync();
                                accessoriesSynced++;
                                continue;
                            }

                            existing.Quantity = targetQty;
                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            var action = targetQty == 0
                                ? SyncAction.ZeroedOut
                                : SyncAction.Updated;

                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsUpdated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, prev, targetQty, action, null));

                            Console.WriteLine(
                                $"[SyncCompanyStock] AC '{parsed.Name}': {prev} → {targetQty}");
                        }
                        else
                        {
                            decimal price = acByName.TryGetValue(nameKey, out var anyRecords)
                                ? anyRecords.First().Price
                                : 0m;

                            var newRecord = new Domain.Entities.Spare.RiderAccessory
                            {
                                Name = parsed.Name!,
                                Quantity = targetQty,
                                Price = price,
                                Location = targetLocation,
                                CreatedAt = DateTime.UtcNow.AddHours(3)
                            };

                            await _db.RiderAccessories.AddAsync(newRecord);
                            await _db.SaveChangesAsync();
                            await transaction.CommitAsync();

                            acByNameAndLocation[housingKey] = newRecord;

                            var action = targetQty == 0
                                ? SyncAction.ZeroedOut
                                : SyncAction.Created;

                            if (action == SyncAction.ZeroedOut) zeroedOut++;
                            else recordsCreated++;

                            rowResults.Add(new HousingInventorySyncRowResult(
                                rowNumber, parsed.Name!, itemType,
                                targetQty, null, targetQty, action, null));

                            Console.WriteLine(
                                $"[SyncCompanyStock] AC '{parsed.Name}': created qty={targetQty}");
                        }

                        accessoriesSynced++;
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    validationErrors++;
                    processingErrors.Add($"Row {rowNumber}: {ex.Message}");
                    rowResults.Add(new HousingInventorySyncRowResult(
                        rowNumber, "N/A", null, 0, null, 0,
                        SyncAction.ValidationError, $"Exception: {ex.Message}"));
                    Console.WriteLine($"[SyncCompanyStock] ERROR row {rowNumber}: {ex.Message}");
                }
            }

            Console.WriteLine($"[SyncCompanyStock] Complete:");
            Console.WriteLine($"  SparePartsSynced: {sparePartsSynced}");
            Console.WriteLine($"  AccessoriesSynced: {accessoriesSynced}");
            Console.WriteLine($"  Created:  {recordsCreated}");
            Console.WriteLine($"  Updated:  {recordsUpdated}");
            Console.WriteLine($"  Zeroed:   {zeroedOut}");
            Console.WriteLine($"  NoChange: {unchangedCount}");
            Console.WriteLine($"  NotFound: {notFoundCount}");
            Console.WriteLine($"  Errors:   {validationErrors}");

            return Result.Success(new HousingInventorySyncResponse(
                fakeHousingId,
                targetLocation,
                dataRows.Count,
                sparePartsSynced,
                accessoriesSynced,
                recordsCreated,
                recordsUpdated,
                zeroedOut,
                notFoundCount,
                validationErrors,
                rowResults,
                processingErrors,
                DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SyncCompanyStock] FATAL: {ex}");
            return Result.Failure<HousingInventorySyncResponse>(
                new Error("ProcessingError",
                    $"Failed to process file: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════════════════

    private static Error? ValidateFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return new Error("InvalidFile", "File is empty or null", 400);

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            return new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400);

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Header detection
    // Expected headers (any row in first 10):
    //   Name column  → "Name" / "الاسم" / "اسم القطعة" / "اسم العنصر" / "a"
    //   Qty column   → "Quantity" / "الكمية" / "Qty" / "b"
    //   Type column  → "Type" / "النوع" / "c"  (optional)
    // ─────────────────────────────────────────────────────────────────────
    private static IXLRow? FindInventoryHeaderRow(IXLWorksheet ws)
    {
        var nameVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "الاسم", "اسم القطعة", "اسم العنصر", "اسم المنتج",
            "part name", "item name", "a"
        };

        var qtyVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "quantity", "الكمية", "qty", "stock", "المخزون", "b"
        };

        for (int i = 1; i <= Math.Min(10, ws.RowsUsed().Count()); i++)
        {
            var row = ws.Row(i);
            var values = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            bool hasName = values.Any(v => nameVariants.Contains(v));
            bool hasQty = values.Any(v => qtyVariants.Contains(v));

            // Accept a row that has at least Name column recognised.
            // Qty is optional (we default to 0 when absent).
            if (hasName || (hasQty && values.Count >= 1))
                return row;
        }

        // Fallback: treat first row as header
        return ws.Row(1);
    }

    // ─────────────────────────────────────────────────────────────────────

    private static InventoryColumnMapping BuildInventoryColumnMapping(IXLRow headerRow)
    {
        var mapping = new InventoryColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.NameCol = FindCol(cells,
            "name", "الاسم", "اسم القطعة", "اسم العنصر", "اسم المنتج",
            "part name", "item name", "a");

        mapping.QuantityCol = FindCol(cells,
            "quantity", "الكمية", "qty", "stock", "المخزون", "b");

        mapping.TypeCol = FindCol(cells,
            "type", "النوع", "item type", "نوع العنصر", "c");

        var missing = new List<string>();
        if (mapping.NameCol == 0) missing.Add("Name / الاسم");

        mapping.IsValid = !missing.Any();
        mapping.ErrorMessage = missing.Any()
            ? $"Required columns not found: {string.Join(", ", missing)}"
            : null;

        return mapping;
    }

    // ─────────────────────────────────────────────────────────────────────

    private static InventoryRowData ParseInventoryRow(
        IXLRow row,
        InventoryColumnMapping map,
        int rowNumber)
    {
        var data = new InventoryRowData { RowNumber = rowNumber };

        try
        {
            data.Name = GetCell(row, map.NameCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.Name))
            {
                data.IsValid = false;
                data.ErrorMessage = "Item name is required";
                return data;
            }

            // Quantity – missing / empty / non-numeric → 0
            var qtyStr = GetCell(row, map.QuantityCol);
            if (string.IsNullOrWhiteSpace(qtyStr))
            {
                data.Quantity = 0;
            }
            else if (TryParseQuantity(qtyStr, out int qty))
            {
                if (qty < 0)
                {
                    data.IsValid = false;
                    data.ErrorMessage = "Quantity cannot be negative";
                    return data;
                }
                data.Quantity = qty;
            }
            else
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid quantity value: '{qtyStr}'";
                return data;
            }

            // Type hint (optional)
            var typeStr = GetCell(row, map.TypeCol)?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(typeStr))
            {
                if (typeStr.Contains("spare") || typeStr.Contains("قطعة") || typeStr == "1")
                    data.TypeHint = InventoryItemType.SparePart;
                else if (typeStr.Contains("access") || typeStr.Contains("اكسسوار") ||
                         typeStr.Contains("ملحق") || typeStr == "2")
                    data.TypeHint = InventoryItemType.Accessory;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Resolve the item type using the optional hint from column C, falling
    // back to DB lookups.  Returns null when name is not found in either table.
    // ─────────────────────────────────────────────────────────────────────
    private static InventoryItemType? ResolveType(
        InventoryItemType? hint,
        string nameKey,
        Dictionary<string, List<Domain.Entities.Spare.SparePart>> spByName,
        Dictionary<string, List<Domain.Entities.Spare.RiderAccessory>> acByName)
    {
        bool inSP = spByName.ContainsKey(nameKey);
        bool inAC = acByName.ContainsKey(nameKey);

        if (hint.HasValue)
        {
            // Honour explicit hint if item exists in the hinted table
            if (hint == InventoryItemType.SparePart && inSP) return InventoryItemType.SparePart;
            if (hint == InventoryItemType.Accessory && inAC) return InventoryItemType.Accessory;
        }

        // Auto-detect: prefer SparePart when found in both
        if (inSP) return InventoryItemType.SparePart;
        if (inAC) return InventoryItemType.Accessory;

        return null; // not found anywhere
    }

    // ─────────────────────────────────────────────────────────────────────
    // Shared cell / column helpers
    // ─────────────────────────────────────────────────────────────────────

    private static int FindCol(List<IXLCell> cells, params string[] names)
    {
        foreach (var cell in cells)
        {
            try
            {
                if (cell.IsEmpty()) continue;

                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString().Trim()
                    : cell.GetString().Trim();

                if (string.IsNullOrWhiteSpace(val)) continue;

                foreach (var name in names)
                {
                    if (val.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        val.Replace(" ", "").Equals(name.Replace(" ", ""),
                            StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }
            }
            catch { /* skip */ }
        }
        return 0;
    }

    private static string? GetCell(IXLRow row, int col)
    {
        if (col == 0) return null;

        try
        {
            var cell = row.Cell(col);
            if (cell.IsEmpty()) return null;

            if (cell.IsMerged())
                cell = cell.MergedRange().FirstCell();

            if (cell.DataType == XLDataType.Number)
            {
                var d = cell.GetDouble();
                return d == Math.Floor(d) ? ((long)d).ToString() : d.ToString();
            }

            if (cell.DataType == XLDataType.Text)
                return cell.GetText().Trim();

            return cell.GetString().Trim();
        }
        catch { return null; }
    }

    private static bool TryParseQuantity(string value, out int result)
    {
        result = 0;
        value = value.Trim().Replace(",", "").Replace(" ", "");

        if (int.TryParse(value, out result)) return true;

        if (double.TryParse(value, out double d))
        {
            result = (int)Math.Round(d);
            return true;
        }

        return false;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INTERNAL TYPES
    // ══════════════════════════════════════════════════════════════════════

    internal class InventoryColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int NameCol { get; set; }
        public int QuantityCol { get; set; }
        public int TypeCol { get; set; } // 0 = absent
    }

    internal class InventoryRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }          // default 0
        public InventoryItemType? TypeHint { get; set; } // from column C, may be null
    }
}