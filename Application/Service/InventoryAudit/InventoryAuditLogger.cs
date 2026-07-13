using Domain;
using Domain.Entities.Spare;

namespace Application.Service.InventoryAudit;

/// <summary>
/// Adds InventoryAuditLog rows to the same ApplicationDbcontext instance the
/// caller is already using, so the audit entry is saved atomically together
/// with the SparePart/RiderAccessory change in the caller's existing
/// SaveChangesAsync() call. Nothing here calls SaveChanges itself.
/// </summary>
public static class InventoryAuditLogger
{
    public static void LogSparePartCreate(
        ApplicationDbcontext db,Domain.Entities.Spare.SparePart sparePart, string performedBy, string? notes = null)
    {
        db.InventoryAuditLogs.Add(new InventoryAuditLog
        {
            ItemType = InventoryItemType.SparePart,
            ItemId = sparePart.Id,
            ItemName = sparePart.Name,
            Action = InventoryAuditAction.Create,
            LocationBefore = null,
            LocationAfter = sparePart.Location,
            QuantityBefore = null,
            QuantityAfter = sparePart.Quantity,
            PriceBefore = null,
            PriceAfter = sparePart.Price,
            PerformedBy = performedBy,
            Notes = notes
        });
    }

    /// <param name="before">A snapshot taken BEFORE the entity's properties were mutated.</param>
    /// <param name="after">The same entity AFTER the properties were mutated.</param>
    public static void LogSparePartUpdate(
        ApplicationDbcontext db, SparePartSnapshot before, Domain.Entities.Spare.SparePart after, string performedBy, string? notes = null)
    {
        var nothingChanged =
            before.Quantity == after.Quantity &&
            before.Price == after.Price &&
            before.Location == after.Location &&
            before.Name == after.Name;

        if (nothingChanged)
            return;

        db.InventoryAuditLogs.Add(new InventoryAuditLog
        {
            ItemType = InventoryItemType.SparePart,
            ItemId = after.Id,
            ItemName = after.Name,
            Action = InventoryAuditAction.Update,
            LocationBefore = before.Location,
            LocationAfter = after.Location,
            QuantityBefore = before.Quantity,
            QuantityAfter = after.Quantity,
            PriceBefore = before.Price,
            PriceAfter = after.Price,
            PerformedBy = performedBy,
            Notes = notes
        });
    }

    public static void LogSparePartDelete(
        ApplicationDbcontext db, Domain.Entities.Spare.SparePart sparePart, string performedBy, string? notes = null)
    {
        db.InventoryAuditLogs.Add(new InventoryAuditLog
        {
            ItemType = InventoryItemType.SparePart,
            ItemId = sparePart.Id,
            ItemName = sparePart.Name,
            Action = InventoryAuditAction.Delete,
            LocationBefore = sparePart.Location,
            LocationAfter = null,
            QuantityBefore = sparePart.Quantity,
            QuantityAfter = null,
            PriceBefore = sparePart.Price,
            PriceAfter = null,
            PerformedBy = performedBy,
            Notes = notes
        });
    }

    public static void LogAccessoryCreate(
        ApplicationDbcontext db, Domain.Entities.Spare.RiderAccessory accessory, string performedBy, string? notes = null)
    {
        db.InventoryAuditLogs.Add(new InventoryAuditLog
        {
            ItemType = InventoryItemType.RiderAccessory,
            ItemId = accessory.Id,
            ItemName = accessory.Name,
            Action = InventoryAuditAction.Create,
            LocationBefore = null,
            LocationAfter = accessory.Location,
            QuantityBefore = null,
            QuantityAfter = accessory.Quantity,
            PriceBefore = null,
            PriceAfter = accessory.Price,
            PerformedBy = performedBy,
            Notes = notes
        });
    }

    public static void LogAccessoryUpdate(
        ApplicationDbcontext db, RiderAccessorySnapshot before, Domain.Entities.Spare.RiderAccessory after, string performedBy, string? notes = null)
    {
        var nothingChanged =
            before.Quantity == after.Quantity &&
            before.Price == after.Price &&
            before.Location == after.Location &&
            before.Name == after.Name;

        if (nothingChanged)
            return;

        db.InventoryAuditLogs.Add(new InventoryAuditLog
        {
            ItemType = InventoryItemType.RiderAccessory,
            ItemId = after.Id,
            ItemName = after.Name,
            Action = InventoryAuditAction.Update,
            LocationBefore = before.Location,
            LocationAfter = after.Location,
            QuantityBefore = before.Quantity,
            QuantityAfter = after.Quantity,
            PriceBefore = before.Price,
            PriceAfter = after.Price,
            PerformedBy = performedBy,
            Notes = notes
        });
    }

    public static void LogAccessoryDelete(
        ApplicationDbcontext db, Domain.Entities.Spare.RiderAccessory accessory, string performedBy, string? notes = null)
    {
        db.InventoryAuditLogs.Add(new InventoryAuditLog
        {
            ItemType = InventoryItemType.RiderAccessory,
            ItemId = accessory.Id,
            ItemName = accessory.Name,
            Action = InventoryAuditAction.Delete,
            LocationBefore = accessory.Location,
            LocationAfter = null,
            QuantityBefore = accessory.Quantity,
            QuantityAfter = null,
            PriceBefore = accessory.Price,
            PriceAfter = null,
            PerformedBy = performedBy,
            Notes = notes
        });
    }
}

/// <summary>
/// Plain snapshot of the fields we care about, captured before an EF-tracked
/// SparePart entity is mutated in place (so we still have the "before" values
/// once the entity itself has already been changed to the "after" values).
/// </summary>
public readonly record struct SparePartSnapshot(string Name, int Quantity, decimal Price, string Location)
{
    public static SparePartSnapshot From(Domain.Entities.Spare.SparePart sparePart) =>
        new(sparePart.Name, sparePart.Quantity, sparePart.Price, sparePart.Location);
}

public readonly record struct RiderAccessorySnapshot(string Name, int Quantity, decimal Price, string Location)
{
    public static RiderAccessorySnapshot From(Domain.Entities.Spare.RiderAccessory accessory) =>
        new(accessory.Name, accessory.Quantity, accessory.Price, accessory.Location);
}
