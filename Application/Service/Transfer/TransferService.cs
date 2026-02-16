using Application.Abstraction;
using Application.Contracts.SupplierCon;
using Domain;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Transfer;

public class TransferService(ApplicationDbcontext dbcontext) : ITransferService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;
    private const string MAIN_LOCATION = "الشركة";



    public async Task<Result<bool>> DeleteTransferAsync(int transferId)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            // Get transfer with items
            var transfer = await _dbcontext.Transfers
                .Include(t => t.TransferItems)
                .FirstOrDefaultAsync(t => t.Id == transferId);

            if (transfer == null)
                return Result.Failure<bool>(
                    new Error("TransferNotFound", "Transfer not found", 404));

            // Reverse the transfer for each item
            foreach (var item in transfer.TransferItems)
            {
                var reverseResult = await ReverseTransferItem(item, transfer.ToLocation);

                if (!reverseResult)
                    return Result.Failure<bool>(
                        new Error("ReverseTransferFailed",
                            $"Failed to reverse transfer for item {item.ItemName}. " +
                            $"The item may not exist in housing location or has insufficient quantity.", 400));
            }

            // Delete transfer items first (due to foreign key)
            _dbcontext.Set<TransferItem>().RemoveRange(transfer.TransferItems);

            // Delete the transfer record
            _dbcontext.Transfers.Remove(transfer);

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<bool>(
                new Error("DeleteTransferError", $"Failed to delete transfer: {ex.Message}", 500));
        }
    }

    private async Task<bool> ReverseTransferItem(TransferItem item, string housingLocation)
    {
        if (item.ItemType == TransferItemType.SparePart)
        {
            return await ReverseSparePartTransfer(item, housingLocation);
        }
        else if (item.ItemType == TransferItemType.Accessory)
        {
            return await ReverseAccessoryTransfer(item, housingLocation);
        }

        return false;
    }

    private async Task<bool> ReverseSparePartTransfer(TransferItem item, string housingLocation)
    {
        // Find item in housing location
        var housingSparePart = await _dbcontext.SpareParts
            .FirstOrDefaultAsync(sp => sp.Name == item.ItemName &&
                                      sp.Location == housingLocation);

        if (housingSparePart == null || housingSparePart.Quantity < item.Quantity)
            return false;

        // Find or create item in main location
        var mainSparePart = await _dbcontext.SpareParts
            .FirstOrDefaultAsync(sp => sp.Name == item.ItemName &&
                                      sp.Location == MAIN_LOCATION);

        if (mainSparePart != null)
        {
            // Add back to main location
            mainSparePart.Quantity += item.Quantity;
        }
        else
        {
            // Create new in main location (shouldn't normally happen, but handle it)
            mainSparePart = new Domain.Entities.Spare.SparePart
            {
                Name = item.ItemName,
                Quantity = item.Quantity,
                Price = housingSparePart.Price,
                Location = MAIN_LOCATION,
                CreatedAt = DateTime.UtcNow.AddHours(3)
            };
            await _dbcontext.SpareParts.AddAsync(mainSparePart);
        }

        // Reduce from housing location
        housingSparePart.Quantity -= item.Quantity;

        // Remove housing item if quantity becomes zero
        if (housingSparePart.Quantity == 0)
        {
            _dbcontext.SpareParts.Remove(housingSparePart);
        }

        return true;
    }
    private async Task<bool> ReverseAccessoryTransfer(TransferItem item, string housingLocation)
    {
        // Find item in housing location
        var housingAccessory = await _dbcontext.RiderAccessories
            .FirstOrDefaultAsync(a => a.Name == item.ItemName &&
                                     a.Location == housingLocation);

        if (housingAccessory == null || housingAccessory.Quantity < item.Quantity)
            return false;

        // Find or create item in main location
        var mainAccessory = await _dbcontext.RiderAccessories
            .FirstOrDefaultAsync(a => a.Name == item.ItemName &&
                                     a.Location == MAIN_LOCATION);

        if (mainAccessory != null)
        {
            // Add back to main location
            mainAccessory.Quantity += item.Quantity;
        }
        else
        {
            // Create new in main location (shouldn't normally happen, but handle it)
            mainAccessory = new Domain.Entities.Spare.RiderAccessory
            {
                Name = item.ItemName,
                Quantity = item.Quantity,
                Price = housingAccessory.Price,
                Location = MAIN_LOCATION,
                CreatedAt = DateTime.UtcNow.AddHours(3)
            };
            await _dbcontext.RiderAccessories.AddAsync(mainAccessory);
        }

        // Reduce from housing location
        housingAccessory.Quantity -= item.Quantity;

        // Remove housing item if quantity becomes zero
        if (housingAccessory.Quantity == 0)
        {
            _dbcontext.RiderAccessories.Remove(housingAccessory);
        }

        return true;
    }

    public async Task<Result<TransferResponse>> TransferToHousingAsync(
        TransferRequest request,
        string transferredBy)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            // Validate housing
            var housing = await _dbcontext.Housings.FindAsync(request.HousingId);
            if (housing == null)
                return Result.Failure<TransferResponse>(
                    new Error("HousingNotFound", "Housing not found", 404));

            if (request.Items == null || !request.Items.Any())
                return Result.Failure<TransferResponse>(
                    new Error("NoItems", "Transfer must contain at least one item", 400));

            var transferItems = new List<TransferItem>();

            foreach (var item in request.Items)
            {
                var transferItem = await ProcessTransferItem(item, housing.Name);

                if (transferItem == null)
                    return Result.Failure<TransferResponse>(
                        new Error("ItemNotFound",
                            $"Item with ID {item.ItemId} and type {item.ItemType} not found in main location", 404));

                transferItems.Add(transferItem);
            }

            // Create transfer record
            var transfer = new Domain.Entities.Spare.Transfer
            {
                FromLocation = MAIN_LOCATION,
                ToLocation = housing.Name,
                HousingId = request.HousingId,
                TransferredBy = transferredBy,
                TransferredAt = DateTime.UtcNow.AddHours(3),
                TransferItems = transferItems
            };

            await _dbcontext.Transfers.AddAsync(transfer);
            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            // Reload for response
            transfer = await _dbcontext.Transfers
                .Include(t => t.TransferItems)
                .FirstAsync(t => t.Id == transfer.Id);

            return Result.Success(MapToResponse(transfer));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<TransferResponse>(
                new Error("TransferError", $"Failed to transfer items: {ex.Message}", 500));
        }
    }

    private async Task<TransferItem?> ProcessTransferItem(
        TransferItemRequest request,
        string housingName)
    {
        if (request.ItemType == TransferItemType.SparePart)
        {
            return await ProcessSparePartTransfer(request, housingName);
        }
        else if (request.ItemType == TransferItemType.Accessory)
        {
            return await ProcessAccessoryTransfer(request, housingName);
        }

        return null;
    }

    private async Task<TransferItem?> ProcessSparePartTransfer(
        TransferItemRequest request,
        string housingName)
    {
        // Get from main location
        var mainSparePart = await _dbcontext.SpareParts
            .FirstOrDefaultAsync(sp => sp.Id == request.ItemId &&
                                      sp.Location == MAIN_LOCATION);

        if (mainSparePart == null || mainSparePart.Quantity < request.Quantity)
            return null;

        // Check if item exists in housing location
        var housingSparePart = await _dbcontext.SpareParts
            .FirstOrDefaultAsync(sp => sp.Name == mainSparePart.Name &&
                                      sp.Location == housingName);

        if (housingSparePart != null)
        {
            // Add to existing
            housingSparePart.Quantity += request.Quantity;
        }
        else
        {
            // Create new in housing
            housingSparePart = new Domain.Entities.Spare.SparePart
            {
                Name = mainSparePart.Name,
                Quantity = request.Quantity,
                Price = mainSparePart.Price,
                Location = housingName,
                CreatedAt = DateTime.UtcNow.AddHours(3)
            };
            await _dbcontext.SpareParts.AddAsync(housingSparePart);
        }

        // Reduce from main location
        mainSparePart.Quantity -= request.Quantity;

        return new TransferItem
        {
            ItemId = mainSparePart.Id,
            ItemName = mainSparePart.Name,
            ItemType = TransferItemType.SparePart,
            Quantity = request.Quantity
        };
    }

    private async Task<TransferItem?> ProcessAccessoryTransfer(
        TransferItemRequest request,
        string housingName)
    {
        // Get from main location
        var mainAccessory = await _dbcontext.RiderAccessories
            .FirstOrDefaultAsync(a => a.Id == request.ItemId &&
                                     a.Location == MAIN_LOCATION);

        if (mainAccessory == null || mainAccessory.Quantity < request.Quantity)
            return null;

        // Check if item exists in housing location
        var housingAccessory = await _dbcontext.RiderAccessories
            .FirstOrDefaultAsync(a => a.Name == mainAccessory.Name &&
                                     a.Location == housingName);

        if (housingAccessory != null)
        {
            // Add to existing
            housingAccessory.Quantity += request.Quantity;
        }
        else
        {
            // Create new in housing
            housingAccessory = new Domain.Entities.Spare.RiderAccessory
            {
                Name = mainAccessory.Name,
                Quantity = request.Quantity,
                Price = mainAccessory.Price,
                Location = housingName,
                CreatedAt = DateTime.UtcNow.AddHours(3)
            };
            await _dbcontext.RiderAccessories.AddAsync(housingAccessory);
        }

        // Reduce from main location
        mainAccessory.Quantity -= request.Quantity;

        return new TransferItem
        {
            ItemId = mainAccessory.Id,
            ItemName = mainAccessory.Name,
            ItemType = TransferItemType.Accessory,
            Quantity = request.Quantity
        };
    }

    public async Task<Result<IEnumerable<TransferResponse>>> GetAllTransfersAsync()
    {
        var transfers = await _dbcontext.Transfers
            .Include(t => t.TransferItems)
            .OrderByDescending(t => t.TransferredAt)
            .AsNoTracking()
            .ToListAsync();

        var response = transfers.Select(MapToResponse);
        return Result.Success<IEnumerable<TransferResponse>>(response);
    }

    public async Task<Result<TransferResponse>> GetTransferByIdAsync(int id)
    {
        var transfer = await _dbcontext.Transfers
            .Include(t => t.TransferItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null)
            return Result.Failure<TransferResponse>(
                new Error("NotFound", "Transfer not found", 404));

        return Result.Success(MapToResponse(transfer));
    }

    public async Task<Result<IEnumerable<TransferResponse>>> GetTransfersByHousingAsync(int housingId)
    {
        var transfers = await _dbcontext.Transfers
            .Include(t => t.TransferItems)
            .Where(t => t.HousingId == housingId)
            .OrderByDescending(t => t.TransferredAt)
            .AsNoTracking()
            .ToListAsync();

        var response = transfers.Select(MapToResponse);
        return Result.Success<IEnumerable<TransferResponse>>(response);
    }

    private static TransferResponse MapToResponse(Domain.Entities.Spare.Transfer transfer)
    {
        var items = transfer.TransferItems.Select(ti => new TransferItemResponse(
            ti.ItemId,
            ti.ItemName,
            ti.ItemType,
            ti.Quantity
        )).ToList();

        return new TransferResponse(
            transfer.Id,
            transfer.FromLocation,
            transfer.ToLocation,
            transfer.HousingId,
            transfer.TransferItems.Sum(ti => ti.Quantity),
            transfer.TransferredBy,
            transfer.TransferredAt,
            items
        );
    }
}