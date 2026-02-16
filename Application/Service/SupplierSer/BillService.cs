// Application/Service/Bill/BillService.cs
using Application.Abstraction;
using Application.Contracts.SupplierCon;
using Domain;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.SupplierSer;

public class BillService(ApplicationDbcontext dbcontext) : IBillService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;
    private const string MAIN_LOCATION = "الشركة";

    public async Task<Result<BillResponse>> ReceiveBillAsync(ReceiveBillRequest request, string processedBy)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            // Validate supplier
            var supplier = await _dbcontext.Suppliers.FindAsync(request.SupplierId);
            if (supplier == null)
                return Result.Failure<BillResponse>(
                    new Error("SupplierNotFound", "Supplier not found", 404));

            if (!supplier.IsActive)
                return Result.Failure<BillResponse>(
                    new Error("SupplierInactive", "Supplier is not active", 400));

            // Validate items
            if (request.Items == null || !request.Items.Any())
                return Result.Failure<BillResponse>(
                    new Error("NoItems", "Bill must contain at least one item", 400));

            var billItems = new List<BillItem>();
            decimal totalAmount = 0;

            foreach (var item in request.Items)
            {
                var billItem = await ProcessBillItem(item);

                if (billItem == null)
                    return Result.Failure<BillResponse>(
                        new Error("ItemNotFound",
                            $"Item with ID {item.ItemId} and type {item.ItemType} not found in main location (الشركة)", 404));

                billItems.Add(billItem);
                totalAmount += billItem.LineTotal;
            }

            // Create bill
            var bill = new Bill
            {
                SupplierId = request.SupplierId,
                InvoiceNumber = request.InvoiceNumber,
                InvoiceDate = request.InvoiceDate,
                TotalAmount = totalAmount,
                ProcessedBy = processedBy,
                ProcessedAt = DateTime.UtcNow.AddHours(3),
                Notes = request.Notes,
                BillItems = billItems
            };

            await _dbcontext.Bills.AddAsync(bill);
            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            // Reload with all includes for response
            bill = await _dbcontext.Bills
                .Include(b => b.Supplier)
                .Include(b => b.BillItems)
                .FirstAsync(b => b.Id == bill.Id);

            return Result.Success(MapToResponse(bill));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<BillResponse>(
                new Error("ProcessError", $"Failed to process bill: {ex.Message}", 500));
        }
    }

    private async Task<BillItem?> ProcessBillItem(BillItemRequest request)
    {
        if (request.ItemType == BillItemType.SparePart)
        {
            return await ProcessSparePartItem(request);
        }
        else if (request.ItemType == BillItemType.Accessory)
        {
            return await ProcessAccessoryItem(request);
        }

        return null;
    }

    private async Task<BillItem?> ProcessSparePartItem(BillItemRequest request)
    {
        var sparePart = await _dbcontext.SpareParts
            .FirstOrDefaultAsync(sp => sp.Id == request.ItemId &&
                                      sp.Location == MAIN_LOCATION);

        if (sparePart == null)
            return null;

        var oldPrice = sparePart.Price;
        var currentQuantity = sparePart.Quantity;
        var incomingQuantity = request.Quantity;
        var incomingPrice = request.UnitPrice;

        bool priceChanged = false;
        decimal? newAveragePrice = null;

        // Calculate new average price if prices differ
        if (oldPrice != incomingPrice)
        {
            // Weighted average: ((Q1 × P1) + (Q2 × P2)) / (Q1 + Q2)
            if (currentQuantity > 0)
            {
                newAveragePrice = ((currentQuantity * oldPrice) + (incomingQuantity * incomingPrice))
                                 / (currentQuantity + incomingQuantity);
            }
            else
            {
                // If current quantity is 0, just use the incoming price
                newAveragePrice = incomingPrice;
            }

            sparePart.Price = newAveragePrice.Value;
            priceChanged = true;
        }

        // Update quantity
        sparePart.Quantity += incomingQuantity;

        return new BillItem
        {
            ItemId = sparePart.Id,
            ItemName = sparePart.Name,
            ItemType = BillItemType.SparePart,
            Quantity = incomingQuantity,
            UnitPrice = incomingPrice,
            OldPrice = oldPrice,
            PriceChanged = priceChanged,
            NewAveragePrice = newAveragePrice,
            LineTotal = incomingQuantity * incomingPrice
        };
    }

    private async Task<BillItem?> ProcessAccessoryItem(BillItemRequest request)
    {
        var accessory = await _dbcontext.RiderAccessories
            .FirstOrDefaultAsync(a => a.Id == request.ItemId &&
                                     a.Location == MAIN_LOCATION);

        if (accessory == null)
            return null;

        var oldPrice = accessory.Price;
        var currentQuantity = accessory.Quantity;
        var incomingQuantity = request.Quantity;
        var incomingPrice = request.UnitPrice;

        bool priceChanged = false;
        decimal? newAveragePrice = null;

        // Calculate new average price if prices differ
        if (oldPrice != incomingPrice)
        {
            // Weighted average: ((Q1 × P1) + (Q2 × P2)) / (Q1 + Q2)
            if (currentQuantity > 0)
            {
                newAveragePrice = ((currentQuantity * oldPrice) + (incomingQuantity * incomingPrice))
                                 / (currentQuantity + incomingQuantity);
            }
            else
            {
                // If current quantity is 0, just use the incoming price
                newAveragePrice = incomingPrice;
            }

            accessory.Price = newAveragePrice.Value;
            priceChanged = true;
        }

        // Update quantity
        accessory.Quantity += incomingQuantity;

        return new BillItem
        {
            ItemId = accessory.Id,
            ItemName = accessory.Name,
            ItemType = BillItemType.Accessory,
            Quantity = incomingQuantity,
            UnitPrice = incomingPrice,
            OldPrice = oldPrice,
            PriceChanged = priceChanged,
            NewAveragePrice = newAveragePrice,
            LineTotal = incomingQuantity * incomingPrice
        };
    }

    public async Task<Result<IEnumerable<BillSummaryResponse>>> GetAllBillsAsync()
    {
        var bills = await _dbcontext.Bills
            .Include(b => b.Supplier)
            .Include(b => b.BillItems)
            .OrderByDescending(b => b.ProcessedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = bills.Select(MapToSummaryResponse);
        return Result.Success<IEnumerable<BillSummaryResponse>>(response);
    }

    public async Task<Result<BillResponse>> GetBillByIdAsync(int id)
    {
        var bill = await _dbcontext.Bills
            .Include(b => b.Supplier)
            .Include(b => b.BillItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id);

        if (bill == null)
            return Result.Failure<BillResponse>(
                new Error("NotFound", "Bill not found", 404));

        return Result.Success(MapToResponse(bill));
    }

    public async Task<Result<IEnumerable<BillSummaryResponse>>> GetBillsByDateRangeAsync(
        DateTime startDate,
        DateTime endDate)
    {
        var bills = await _dbcontext.Bills
            .Include(b => b.Supplier)
            .Include(b => b.BillItems)
            .Where(b => b.ProcessedAt >= startDate && b.ProcessedAt <= endDate)
            .OrderByDescending(b => b.ProcessedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = bills.Select(MapToSummaryResponse);
        return Result.Success<IEnumerable<BillSummaryResponse>>(response);
    }

    public async Task<Result<IEnumerable<BillSummaryResponse>>> GetBillsBySupplierAsync(int supplierId)
    {
        var supplier = await _dbcontext.Suppliers.FindAsync(supplierId);
        if (supplier == null)
            return Result.Failure<IEnumerable<BillSummaryResponse>>(
                new Error("SupplierNotFound", "Supplier not found", 404));

        var bills = await _dbcontext.Bills
            .Include(b => b.Supplier)
            .Include(b => b.BillItems)
            .Where(b => b.SupplierId == supplierId)
            .OrderByDescending(b => b.ProcessedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = bills.Select(MapToSummaryResponse);
        return Result.Success<IEnumerable<BillSummaryResponse>>(response);
    }

    public async Task<Result> DeleteBillAsync(int id)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            var bill = await _dbcontext.Bills
                .Include(b => b.BillItems)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bill == null)
                return Result.Failure(
                    new Error("NotFound", "Bill not found", 404));

            // Reverse the quantities (don't reverse price changes for data integrity)
            foreach (var item in bill.BillItems)
            {
                if (item.ItemType == BillItemType.SparePart)
                {
                    var sparePart = await _dbcontext.SpareParts
                        .FirstOrDefaultAsync(sp => sp.Id == item.ItemId);

                    if (sparePart != null && sparePart.Quantity >= item.Quantity)
                    {
                        sparePart.Quantity -= item.Quantity;
                    }
                }
                else if (item.ItemType == BillItemType.Accessory)
                {
                    var accessory = await _dbcontext.RiderAccessories
                        .FirstOrDefaultAsync(a => a.Id == item.ItemId);

                    if (accessory != null && accessory.Quantity >= item.Quantity)
                    {
                        accessory.Quantity -= item.Quantity;
                    }
                }
            }

            _dbcontext.Bills.Remove(bill);
            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(
                new Error("DeleteError", $"Failed to delete bill: {ex.Message}", 500));
        }
    }

    private static BillResponse MapToResponse(Bill bill)
    {
        var items = bill.BillItems.Select(bi => new BillItemResponse(
            bi.Id,
            bi.ItemId,
            bi.ItemName,
            bi.ItemType,
            bi.Quantity,
            bi.UnitPrice,
            bi.OldPrice,
            bi.PriceChanged,
            bi.NewAveragePrice,
            bi.LineTotal
        )).ToList();

        return new BillResponse(
            bill.Id,
            bill.SupplierId,
            bill.Supplier.Name,
            bill.InvoiceNumber,
            bill.InvoiceDate,
            bill.TotalAmount,
            bill.BillItems.Sum(bi => bi.Quantity),
            bill.ProcessedBy,
            bill.ProcessedAt,
            bill.Notes,
            items
        );
    }

    private static BillSummaryResponse MapToSummaryResponse(Bill bill)
    {
        return new BillSummaryResponse(
            bill.Id,
            bill.SupplierId,
            bill.Supplier.Name,
            bill.InvoiceNumber,
            bill.InvoiceDate,
            bill.TotalAmount,
            bill.BillItems.Sum(bi => bi.Quantity),
            bill.ProcessedAt,
            bill.ProcessedBy
        );
    }
}