using Application.Abstraction;
using Domain;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using static Application.Service.SparePart.IItemMovementReportService;

namespace Application.Service.SparePart;

/// <summary>
/// Builds comprehensive movement reports for spare parts and accessories.
///
/// Design notes
/// ─────────────
/// • All data is loaded into memory in a small number of bulk queries to avoid
///   N+1 patterns.  EF navigation properties are used only where already eager-
///   loaded; the rest is resolved in-memory via dictionary lookups.
///
/// • "Transfer" events are extracted from the Transfer / TransferItem tables.
///   A TransferItem row with ItemType = SparePart (1) or Accessory (2) is
///   matched to the canonical item name; the Transfer header supplies
///   FromLocation, ToLocation, TransferredBy, and TransferredAt.
///
/// • Usages are from SparePartUsage (per vehicle) and RiderAccessoryUsage
///   (per rider).
///
/// • Filtering by itemName is case-insensitive.
/// • Filtering by location matches either SourceLocation on usages OR
///   FromLocation / ToLocation on transfers OR the item's current Location.
/// </summary>
public class ItemMovementReportService(ApplicationDbcontext db) : IItemMovementReportService
{
    private readonly ApplicationDbcontext _db = db;

    // ══════════════════════════════════════════════════════════════════════
    //  PUBLIC ENDPOINTS
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<FullItemMovementReport>> GetFullReportAsync(
        DateTime fromDate, DateTime toDate,
        string? itemName = null, string? location = null)
    {
        try
        {
            var (spMovements, spTotals) = await BuildSparePartMovementsAsync(
                fromDate, toDate, itemName, location);

            var (acMovements, acTotals) = await BuildAccessoryMovementsAsync(
                fromDate, toDate, itemName, location);

            var totals = new ReportTotals(
                spMovements.Count,
                acMovements.Count,
                spTotals.TotalTransferEvents + acTotals.TotalTransferEvents,
                spTotals.TotalUsageEvents + acTotals.TotalUsageEvents,
                spTotals.TotalCostOfUsages,
                acTotals.TotalCostOfIssuances
            );

            return Result.Success(new FullItemMovementReport(
                fromDate, toDate,
                itemName, location,
                totals,
                spMovements,
                acMovements,
                DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<FullItemMovementReport>(
                new Error("ReportError",
                    $"Failed to build full movement report: {ex.Message}", 500));
        }
    }

    public async Task<Result<SparePartMovementReport>> GetSparePartReportAsync(
        DateTime fromDate, DateTime toDate,
        string? itemName = null, string? location = null)
    {
        try
        {
            var (movements, totals) = await BuildSparePartMovementsAsync(
                fromDate, toDate, itemName, location);

            return Result.Success(new SparePartMovementReport(
                fromDate, toDate,
                itemName, location,
                totals,
                movements,
                DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<SparePartMovementReport>(
                new Error("ReportError",
                    $"Failed to build spare part report: {ex.Message}", 500));
        }
    }

    public async Task<Result<AccessoryMovementReport>> GetAccessoryReportAsync(
        DateTime fromDate, DateTime toDate,
        string? itemName = null, string? location = null)
    {
        try
        {
            var (movements, totals) = await BuildAccessoryMovementsAsync(
                fromDate, toDate, itemName, location);

            return Result.Success(new AccessoryMovementReport(
                fromDate, toDate,
                itemName, location,
                totals,
                movements,
                DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<AccessoryMovementReport>(
                new Error("ReportError",
                    $"Failed to build accessory report: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SPARE PART BUILDER
    // ══════════════════════════════════════════════════════════════════════

    private async Task<(List<SparePartItemMovement> movements, SparePartReportTotals totals)>
        BuildSparePartMovementsAsync(
            DateTime fromDate, DateTime toDate,
            string? itemNameFilter, string? locationFilter)
    {
        // ── 1. Load all spare parts (all locations, no date filter) ────────
        var allSpareParts = await _db.SpareParts
            .AsNoTracking()
            .ToListAsync();

        // ── 2. Load usages in period ───────────────────────────────────────
        var usages = await _db.SparePartUsages
            .Include(u => u.SparePart)
            .Include(u => u.Vehicle)
                .ThenInclude(v => v.RiderDetails)
                    .ThenInclude(r => r!.Employee)
            .Where(u => u.UsedAt >= fromDate && u.UsedAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        // ── 3. Load transfers in period ────────────────────────────────────
        var transfers = await _db.Transfers
            .Include(t => t.TransferItems)
            .Where(t => t.TransferredAt >= fromDate && t.TransferredAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        // Keep only SP transfer items
        var spTransferItems = transfers
            .SelectMany(t => t.TransferItems
                .Where(ti => ti.ItemType == TransferItemType.SparePart)
                .Select(ti => (Transfer: t, Item: ti)))
            .ToList();

        // ── 4. Build name-keyed lookups ────────────────────────────────────
        // All spare-part records grouped by normalised name
        var spByName = allSpareParts
            .GroupBy(sp => sp.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        // Usages grouped by normalised spare-part name
        var usagesByName = usages
            .GroupBy(u => u.SparePart.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        // Transfer items grouped by normalised name (from TransferItem.ItemName)
        var transfersByName = spTransferItems
            .GroupBy(x => x.Item.ItemName.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── 5. Collect every distinct name that has activity in period ─────
        var activeNames = new HashSet<string>(
            usagesByName.Keys.Concat(transfersByName.Keys));

        // ── 6. Apply optional item-name filter ─────────────────────────────
        if (!string.IsNullOrWhiteSpace(itemNameFilter))
        {
            var needle = itemNameFilter.Trim().ToLowerInvariant();
            activeNames = new HashSet<string>(
                activeNames.Where(n => n.Contains(needle)));
        }

        // ── 7. Apply optional location filter ─────────────────────────────
        // We keep an item if ANY of its usages or transfers touch the location,
        // OR if the item currently resides at that location.
        if (!string.IsNullOrWhiteSpace(locationFilter))
        {
            var loc = locationFilter.Trim().ToLowerInvariant();
            activeNames = new HashSet<string>(
                activeNames.Where(name =>
                {
                    bool hasMatchingUsage = usagesByName.TryGetValue(name, out var uList)
                        && uList.Any(u =>
                            (u.Location ?? "").ToLowerInvariant().Contains(loc)
                            || u.Vehicle.Location.ToLowerInvariant().Contains(loc));

                    bool hasMatchingTransfer = transfersByName.TryGetValue(name, out var tList)
                        && tList.Any(x =>
                            x.Transfer.FromLocation.ToLowerInvariant().Contains(loc)
                            || x.Transfer.ToLocation.ToLowerInvariant().Contains(loc));

                    bool currentlyAtLocation = spByName.TryGetValue(name, out var records)
                        && records.Any(r => r.Location.ToLowerInvariant().Contains(loc));

                    return hasMatchingUsage || hasMatchingTransfer || currentlyAtLocation;
                }));
        }

        // ── 8. Build one SparePartItemMovement per active name ─────────────
        var movements = new List<SparePartItemMovement>();

        foreach (var name in activeNames.OrderBy(n => n))
        {
            var spRecords = spByName.TryGetValue(name, out var recs) ? recs : new List<Domain.Entities.Spare.SparePart>();
            var nameUsages = usagesByName.TryGetValue(name, out var ul) ? ul : new List<SparePartUsage>();
            var nameTransfers = transfersByName.TryGetValue(name, out var tl)
                ? tl
                : new List<(Domain.Entities.Spare.Transfer Transfer, TransferItem Item)>();

            // Current stock snapshot (all locations)
            var snapshots = spRecords
                .OrderBy(r => r.Location)
                .Select(r => new ItemLocationSnapshot(r.Id, r.Location, r.Quantity, r.Price))
                .ToList();

            // Transfer events
            var transferEvents = nameTransfers
                .OrderByDescending(x => x.Transfer.TransferredAt)
                .Select(x => new SparePartTransferEvent(
                    x.Transfer.Id,
                    x.Item.Id,
                    x.Item.ItemId,
                    x.Transfer.FromLocation,
                    x.Transfer.ToLocation,
                    x.Item.Quantity,
                    x.Transfer.TransferredBy,
                    x.Transfer.TransferredAt
                ))
                .ToList();

            // Usage events
            var usageEvents = nameUsages
                .OrderByDescending(u => u.UsedAt)
                .Select(u =>
                {
                    var rider = u.Vehicle?.RiderDetails;
                    return new SparePartUsageEvent(
                        u.Id,
                        u.SparePartId,
                        u.Location,
                        u.Vehicle?.PlateNumberA!,
                        u.Vehicle?.PlateNumberA ?? "N/A",
                        u.Vehicle?.PlateNumberE ?? "N/A",
                        u.Vehicle?.Location ?? "N/A",
                        rider?.EmployeeIqamaNo,
                        rider?.Employee?.NameAR,
                        rider?.Employee?.NameEN,
                        u.QuantityUsed,
                        u.SparePart.Price,
                        u.QuantityUsed * u.SparePart.Price,
                        u.UsedAt
                    );
                })
                .ToList();

            // Per-item summary
            var locationsInvolved = transferEvents.SelectMany(t => new[] { t.FromLocation, t.ToLocation })
                .Concat(usageEvents.Select(u => u.SourceLocation ?? "N/A"))
                .Concat(snapshots.Select(s => s.Location))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct()
                .OrderBy(l => l)
                .ToList();

            var highestUsageLoc = usageEvents
                .GroupBy(u => u.SourceLocation ?? u.VehicleLocation)
                .OrderByDescending(g => g.Sum(u => u.QuantityUsed))
                .FirstOrDefault()?.Key;

            var avgPrice = snapshots.Any()
                ? snapshots.Average(s => s.CurrentPrice)
                : 0m;

            var summary = new SparePartItemSummary(
                transferEvents.Sum(t => t.QuantityTransferred),
                usageEvents.Sum(u => u.QuantityUsed),
                usageEvents.Sum(u => u.TotalCost),
                transferEvents.Count,
                usageEvents.Count,
                locationsInvolved,
                highestUsageLoc,
                Math.Round(avgPrice, 2)
            );

            movements.Add(new SparePartItemMovement(
                spRecords.FirstOrDefault()?.Name ?? name,
                snapshots,
                transferEvents,
                usageEvents,
                summary
            ));
        }

        // ── 9. Compute totals ──────────────────────────────────────────────
        var totals = new SparePartReportTotals(
            movements.Count,
            movements.Sum(m => m.Summary.TransferEventCount),
            movements.Sum(m => m.Summary.UsageEventCount),
            movements.Sum(m => m.Summary.TotalUsageCost),
            movements.Sum(m => m.Summary.TotalQuantityTransferred),
            movements.Sum(m => m.Summary.TotalQuantityUsed)
        );

        return (movements, totals);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ACCESSORY BUILDER
    // ══════════════════════════════════════════════════════════════════════

    private async Task<(List<AccessoryItemMovement> movements, AccessoryReportTotals totals)>
        BuildAccessoryMovementsAsync(
            DateTime fromDate, DateTime toDate,
            string? itemNameFilter, string? locationFilter)
    {
        // ── 1. Load all accessories (all locations) ────────────────────────
        var allAccessories = await _db.RiderAccessories
            .AsNoTracking()
            .ToListAsync();

        // ── 2. Load usages in period ───────────────────────────────────────
        var usages = await _db.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(u => u.IssuedAt >= fromDate && u.IssuedAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        // ── 3. Load transfers in period ────────────────────────────────────
        var transfers = await _db.Transfers
            .Include(t => t.TransferItems)
            .Where(t => t.TransferredAt >= fromDate && t.TransferredAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        var acTransferItems = transfers
            .SelectMany(t => t.TransferItems
                .Where(ti => ti.ItemType == TransferItemType.Accessory)
                .Select(ti => (Transfer: t, Item: ti)))
            .ToList();

        // ── 4. Build name-keyed lookups ────────────────────────────────────
        var acByName = allAccessories
            .GroupBy(a => a.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        var usagesByName = usages
            .GroupBy(u => u.RiderAccessory.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        var transfersByName = acTransferItems
            .GroupBy(x => x.Item.ItemName.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── 5. Collect active names ────────────────────────────────────────
        var activeNames = new HashSet<string>(
            usagesByName.Keys.Concat(transfersByName.Keys));

        if (!string.IsNullOrWhiteSpace(itemNameFilter))
        {
            var needle = itemNameFilter.Trim().ToLowerInvariant();
            activeNames = new HashSet<string>(
                activeNames.Where(n => n.Contains(needle)));
        }

        if (!string.IsNullOrWhiteSpace(locationFilter))
        {
            var loc = locationFilter.Trim().ToLowerInvariant();
            activeNames = new HashSet<string>(
                activeNames.Where(name =>
                {
                    bool hasMatchingUsage = usagesByName.TryGetValue(name, out var uList)
                        && uList.Any(u =>
                            (u.Location ?? "").ToLowerInvariant().Contains(loc)
                            || (u.Rider?.Employee?.Housing?.Name ?? "").ToLowerInvariant().Contains(loc));

                    bool hasMatchingTransfer = transfersByName.TryGetValue(name, out var tList)
                        && tList.Any(x =>
                            x.Transfer.FromLocation.ToLowerInvariant().Contains(loc)
                            || x.Transfer.ToLocation.ToLowerInvariant().Contains(loc));

                    bool currentlyAtLocation = acByName.TryGetValue(name, out var records)
                        && records.Any(r => r.Location.ToLowerInvariant().Contains(loc));

                    return hasMatchingUsage || hasMatchingTransfer || currentlyAtLocation;
                }));
        }

        // ── 6. Build one AccessoryItemMovement per active name ─────────────
        var movements = new List<AccessoryItemMovement>();

        foreach (var name in activeNames.OrderBy(n => n))
        {
            var acRecords = acByName.TryGetValue(name, out var recs) ? recs : new List<Domain.Entities.Spare.RiderAccessory>();
            var nameUsages = usagesByName.TryGetValue(name, out var ul) ? ul : new List<RiderAccessoryUsage>();
            var nameTransfers = transfersByName.TryGetValue(name, out var tl)
                ? tl
                : new List<(Domain.Entities.Spare.Transfer Transfer, TransferItem Item)>();

            // Stock snapshot
            var snapshots = acRecords
                .OrderBy(r => r.Location)
                .Select(r => new ItemLocationSnapshot(r.Id, r.Location, r.Quantity, r.Price))
                .ToList();

            // Transfer events
            var transferEvents = nameTransfers
                .OrderByDescending(x => x.Transfer.TransferredAt)
                .Select(x => new AccessoryTransferEvent(
                    x.Transfer.Id,
                    x.Item.Id,
                    x.Item.ItemId,
                    x.Transfer.FromLocation,
                    x.Transfer.ToLocation,
                    x.Item.Quantity,
                    x.Transfer.TransferredBy,
                    x.Transfer.TransferredAt
                ))
                .ToList();

            // Usage events
            var usageEvents = nameUsages
                .OrderByDescending(u => u.IssuedAt)
                .Select(u => new AccessoryUsageEvent(
                    u.Id,
                    u.RiderAccessoryId,
                    u.Location,
                    u.RiderId,
                    u.Rider?.EmployeeIqamaNo ?? 0,
                    u.Rider?.Employee?.NameAR ?? "N/A",
                    u.Rider?.Employee?.NameEN ?? "N/A",
                    u.Rider?.WorkingId ?? "N/A",
                    u.Rider?.Employee?.Housing?.Name,
                    u.RiderAccessory.Price,
                    u.IssuedAt
                ))
                .ToList();

            // Summary
            var locationsInvolved = transferEvents.SelectMany(t => new[] { t.FromLocation, t.ToLocation })
                .Concat(usageEvents.Select(u => u.SourceLocation ?? "N/A"))
                .Concat(usageEvents.Select(u => u.RiderHousing ?? "N/A"))
                .Concat(snapshots.Select(s => s.Location))
                .Where(l => !string.IsNullOrWhiteSpace(l) && l != "N/A")
                .Distinct()
                .OrderBy(l => l)
                .ToList();

            var highestIssuanceLoc = usageEvents
                .GroupBy(u => u.RiderHousing ?? u.SourceLocation ?? "N/A")
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            var avgPrice = snapshots.Any()
                ? snapshots.Average(s => s.CurrentPrice)
                : 0m;

            var summary = new AccessoryItemSummary(
                transferEvents.Sum(t => t.QuantityTransferred),
                usageEvents.Count,
                usageEvents.Sum(u => u.PriceAtIssuance),
                transferEvents.Count,
                usageEvents.Count,
                locationsInvolved,
                highestIssuanceLoc,
                Math.Round(avgPrice, 2)
            );

            movements.Add(new AccessoryItemMovement(
                acRecords.FirstOrDefault()?.Name ?? name,
                snapshots,
                transferEvents,
                usageEvents,
                summary
            ));
        }

        // ── 7. Compute totals ──────────────────────────────────────────────
        var totals = new AccessoryReportTotals(
            movements.Count,
            movements.Sum(m => m.Summary.TransferEventCount),
            movements.Sum(m => m.Summary.UsageEventCount),
            movements.Sum(m => m.Summary.TotalIssuanceCost),
            movements.Sum(m => m.Summary.TotalQuantityTransferred),
            movements.Sum(m => m.Summary.TotalTimesIssued)
        );

        return (movements, totals);
    }
}