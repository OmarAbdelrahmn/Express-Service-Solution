using Application.Abstraction;
using Application.Contracts.SparePartCo;
using Application.Service.SparePart;
using Domain;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Return;

public class ReturnService(ApplicationDbcontext dbcontext) : IReturnService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<ReturnResponse>> CreateReturnAsync(ReturnRequest request)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            // Validate supplier exists
            var supplier = await _dbcontext.Suppliers.FindAsync(request.SupplierId);
            if (supplier == null)
                return Result.Failure<ReturnResponse>(
                    new Error("NotFound", "Supplier not found", 404));

            // Create return record
            var returnRecord = new Domain.Entities.Spare.Return
            {
                SupplierId = request.SupplierId,
                ReturnNumber = request.ReturnNumber,
                ReturnDate = DateTime.UtcNow.AddHours(3),
                Reason = request.Reason,
                ProcessedBy = request.ProcessedBy,
                Notes = request.Notes,
                TotalAmount = 0
            };

            await _dbcontext.Returns.AddAsync(returnRecord);
            await _dbcontext.SaveChangesAsync();

            decimal totalAmount = 0;

            // Process return items
            foreach (var item in request.Items)
            {
                var lineTotal = item.Quantity * item.UnitPrice;
                totalAmount += lineTotal;

                var returnItem = new ReturnItem
                {
                    ReturnId = returnRecord.Id,
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    ItemType = (ReturnItemType)item.ItemType,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = lineTotal
                };

                await _dbcontext.ReturnItems.AddAsync(returnItem);

                // Decrease quantity in inventory
                if (item.ItemType == 1) // SparePart
                {
                    var sparePart = await _dbcontext.SpareParts.FindAsync(item.ItemId);
                    if (sparePart == null)
                    {
                        await transaction.RollbackAsync();
                        return Result.Failure<ReturnResponse>(
                            new Error("NotFound", $"Spare part with ID {item.ItemId} not found", 404));
                    }

                    if (sparePart.Quantity < item.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return Result.Failure<ReturnResponse>(
                            new Error("InsufficientQuantity",
                                $"Insufficient quantity for {sparePart.Name}. Available: {sparePart.Quantity}", 400));
                    }

                    sparePart.Quantity -= item.Quantity;
                }
                else if (item.ItemType == 2) // Accessory
                {
                    var accessory = await _dbcontext.RiderAccessories.FindAsync(item.ItemId);
                    if (accessory == null)
                    {
                        await transaction.RollbackAsync();
                        return Result.Failure<ReturnResponse>(
                            new Error("NotFound", $"Accessory with ID {item.ItemId} not found", 404));
                    }

                    if (accessory.Quantity < item.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return Result.Failure<ReturnResponse>(
                            new Error("InsufficientQuantity",
                                $"Insufficient quantity for {accessory.Name}. Available: {accessory.Quantity}", 400));
                    }

                    accessory.Quantity -= item.Quantity;
                }
            }

            // Update total amount
            returnRecord.TotalAmount = totalAmount;
            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            // Reload with includes
            returnRecord = await _dbcontext.Returns
                .Include(r => r.Supplier)
                .Include(r => r.ReturnItems)
                .FirstAsync(r => r.Id == returnRecord.Id);

            return Result.Success(MapToResponse(returnRecord));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<ReturnResponse>(
                new Error("CreateError", $"Failed to create return: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<ReturnResponse>>> GetAllReturnsAsync()
    {
        var returns = await _dbcontext.Returns
            .Include(r => r.Supplier)
            .Include(r => r.ReturnItems)
            .OrderByDescending(r => r.ReturnDate)
            .AsNoTracking()
            .ToListAsync();

        var response = returns.Select(MapToResponse);
        return Result.Success<IEnumerable<ReturnResponse>>(response);
    }

    public async Task<Result<ReturnResponse>> GetReturnByIdAsync(int id)
    {
        var returnRecord = await _dbcontext.Returns
            .Include(r => r.Supplier)
            .Include(r => r.ReturnItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (returnRecord == null)
            return Result.Failure<ReturnResponse>(
                new Error("NotFound", "Return not found", 404));

        return Result.Success(MapToResponse(returnRecord));
    }

    public async Task<Result<IEnumerable<ReturnResponse>>> GetReturnsBySupplierAsync(int supplierId)
    {
        var returns = await _dbcontext.Returns
            .Include(r => r.Supplier)
            .Include(r => r.ReturnItems)
            .Where(r => r.SupplierId == supplierId)
            .OrderByDescending(r => r.ReturnDate)
            .AsNoTracking()
            .ToListAsync();

        var response = returns.Select(MapToResponse);
        return Result.Success<IEnumerable<ReturnResponse>>(response);
    }

    public async Task<Result<IEnumerable<ReturnResponse>>> GetReturnsByDateRangeAsync(
        DateTime fromDate, DateTime toDate)
    {
        var returns = await _dbcontext.Returns
            .Include(r => r.Supplier)
            .Include(r => r.ReturnItems)
            .Where(r => r.ReturnDate >= fromDate && r.ReturnDate <= toDate)
            .OrderByDescending(r => r.ReturnDate)
            .AsNoTracking()
            .ToListAsync();

        var response = returns.Select(MapToResponse);
        return Result.Success<IEnumerable<ReturnResponse>>(response);
    }

    private static ReturnResponse MapToResponse(Domain.Entities.Spare.Return returnRecord)
    {
        var items = returnRecord.ReturnItems.Select(item => new ReturnItemResponse(
            item.Id,
            item.ItemName,
            item.ItemType.ToString(),
            item.Quantity,
            item.UnitPrice,
            item.LineTotal
        )).ToList();

        return new ReturnResponse(
            returnRecord.Id,
            returnRecord.SupplierId,
            returnRecord.Supplier.Name,
            returnRecord.ReturnNumber,
            returnRecord.ReturnDate,
            returnRecord.TotalAmount,
            returnRecord.Reason,
            returnRecord.ProcessedBy,
            returnRecord.Notes,
            items
        );
    }
}