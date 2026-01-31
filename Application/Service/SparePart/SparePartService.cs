using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.SparePartCo;
using Domain;
using Domain.Entities;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.SparePart;

public class SparePartService(ApplicationDbcontext dbcontext) : ISparePartService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;


    public async Task<Result<BatchUsageResponse>> RecordBatchSparePartUsageAsync(
        BatchSparePartUsageRequest request)
    {
        var details = new List<UsageResultDetail>();
        int successCount = 0;
        int failureCount = 0;

        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            foreach (var usage in request.Usages)
            {
                try
                {
                    var sparePart = await _dbcontext.SpareParts.FindAsync(usage.SparePartId);

                    if (sparePart == null)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            $"ID: {usage.SparePartId}",
                            usage.VehicleNumber,
                            "Spare part not found"
                        ));
                        failureCount++;
                        continue;
                    }

                    if (sparePart.Quantity < usage.QuantityUsed)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            sparePart.Name,
                            usage.VehicleNumber,
                            $"Insufficient quantity. Available: {sparePart.Quantity}, Requested: {usage.QuantityUsed}"
                        ));
                        failureCount++;
                        continue;
                    }

                    var vehicle = await _dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleNumber == usage.VehicleNumber);

                    if (vehicle == null)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            sparePart.Name,
                            usage.VehicleNumber,
                            "Vehicle not found"
                        ));
                        failureCount++;
                        continue;
                    }

                    // Record usage
                    var sparePartUsage = new SparePartUsage
                    {
                        SparePartId = usage.SparePartId,
                        VehicleNumber = usage.VehicleNumber,
                        QuantityUsed = usage.QuantityUsed,
                        UsedAt = DateTime.UtcNow.AddHours(3),
                        Cost = sparePart.Price * usage.QuantityUsed
                    };

                    await _dbcontext.SparePartUsages.AddAsync(sparePartUsage);

                    // Update quantity
                    sparePart.Quantity -= usage.QuantityUsed;

                    details.Add(new UsageResultDetail(
                        true,
                        sparePart.Name,
                        usage.VehicleNumber,
                        $"Successfully recorded {usage.QuantityUsed} units"
                    ));
                    successCount++;
                }
                catch (Exception ex)
                {
                    details.Add(new UsageResultDetail(
                        false,
                        $"ID: {usage.SparePartId}",
                        usage.VehicleNumber,
                        $"Error: {ex.Message}"
                    ));
                    failureCount++;
                }
            }

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new BatchUsageResponse(
                request.Usages.Count,
                successCount,
                failureCount,
                details
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<BatchUsageResponse>(
                new Error("BatchError", $"Batch operation failed: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<SparePartResponse>>> GetAllAsync()
    {
        var spareParts = await _dbcontext.SpareParts
            .AsNoTracking()
            .OrderBy(sp => sp.Name)
            .ToListAsync();

        var response = spareParts.Select(MapToResponse);
        return Result.Success<IEnumerable<SparePartResponse>>(response);
    }
    public async Task<Result<IEnumerable<SparePartResponse>>> GetAllAsync2()
    {
        var spareParts = await _dbcontext.SpareParts
            .Where(c=>c.Location=="الشركة")
            .AsNoTracking()
            .OrderBy(sp => sp.Name)
            .ToListAsync();

        var response = spareParts.Select(MapToResponse);
        return Result.Success<IEnumerable<SparePartResponse>>(response);
    }

    public async Task<Result<SparePartResponse>> GetByIdAsync(int id)
    {
        var sparePart = await _dbcontext.SpareParts
            .AsNoTracking()
            .FirstOrDefaultAsync(sp => sp.Id == id);

        if (sparePart == null)
            return Result.Failure<SparePartResponse>(
                new Error("NotFound", "Spare part not found", 404));

        return Result.Success(MapToResponse(sparePart));
    }

    public async Task<Result<SparePartResponse>> CreateAsync(SparePartRequest request)
    {
        var sparePart = new Domain.Entities.Spare.SparePart
        {
            Name = request.Name,
            Quantity = request.Quantity,
            Price = request.Price,
            Location = request.Location,
            CreatedAt = DateTime.UtcNow.AddHours(3)
        };

        await _dbcontext.SpareParts.AddAsync(sparePart);
        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(sparePart));
    }

    public async Task<Result<SparePartResponse>> UpdateAsync(int id, SparePartRequest request)
    {
        var sparePart = await _dbcontext.SpareParts.FindAsync(id);

        if (sparePart == null)
            return Result.Failure<SparePartResponse>(
                new Error("NotFound", "Spare part not found", 404));

        sparePart.Name = request.Name;
        sparePart.Quantity = request.Quantity;
        sparePart.Price = request.Price;
        sparePart.Location = request.Location;

        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(sparePart));
    }

    public async Task<Result> DeleteAsync(int id)
    {
        var sparePart = await _dbcontext.SpareParts.FindAsync(id);

        if (sparePart == null)
            return Result.Failure(
                new Error("NotFound", "Spare part not found", 404));

        _dbcontext.SpareParts.Remove(sparePart);
        await _dbcontext.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<IEnumerable<SparePartResponse>>> SearchAsync(string keyword)
    {
        keyword = keyword.ToLower();

        var spareParts = await _dbcontext.SpareParts
            .Where(sp => sp.Name.ToLower().Contains(keyword) ||
                        sp.Location.ToLower().Contains(keyword))
            .AsNoTracking()
            .ToListAsync();

        var response = spareParts.Select(MapToResponse);
        return Result.Success<IEnumerable<SparePartResponse>>(response);
    }

    public async Task<Result<SparePartResponse>> RecordUsageAsync(int sparePartId, SparePartUsageRequest request)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            var sparePart = await _dbcontext.SpareParts.FindAsync(sparePartId);

            if (sparePart == null)
                return Result.Failure<SparePartResponse>(
                    new Error("NotFound", "Spare part not found", 404));

            if (sparePart.Quantity < request.QuantityUsed)
                return Result.Failure<SparePartResponse>(
                    new Error("InsufficientQuantity",
                        $"Only {sparePart.Quantity} units available", 400));

            var vehicle = await _dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleNumber == request.VehicleNumber);

            if (vehicle == null)
                return Result.Failure<SparePartResponse>(
                    new Error("VehicleNotFound", "Vehicle not found", 404));

            // Record usage
            var usage = new SparePartUsage
            {
                SparePartId = sparePartId,
                VehicleNumber = request.VehicleNumber,
                QuantityUsed = request.QuantityUsed,
                UsedAt = DateTime.UtcNow.AddHours(3)
            };

            await _dbcontext.SparePartUsages.AddAsync(usage);

            // Update quantity
            sparePart.Quantity -= request.QuantityUsed;

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success(MapToResponse(sparePart));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<SparePartResponse>(
                new Error("UsageError", $"Failed to record usage: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<SparePartUsageResponse>>> GetUsageHistoryAsync(int sparePartId)
    {
        var usages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.SparePartId == sparePartId)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new SparePartUsageResponse(
            u.Id,
            u.SparePartId,
            u.SparePart.Name,
            u.VehicleNumber,
            u.QuantityUsed,
            u.UsedAt,
            u.Cost
        ));

        return Result.Success<IEnumerable<SparePartUsageResponse>>(response);
    }

    public async Task<Result<IEnumerable<SparePartUsageResponse>>> GetVehicleUsageHistoryAsync(string vehicleNumber)
    {
        var usages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.VehicleNumber == vehicleNumber)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new SparePartUsageResponse(
            u.Id,
            u.SparePartId,
            u.SparePart.Name,
            u.VehicleNumber,
            u.QuantityUsed,
            u.UsedAt,
            u.Cost
        ));

        return Result.Success<IEnumerable<SparePartUsageResponse>>(response);
    }

    private static SparePartResponse MapToResponse(Domain.Entities.Spare.SparePart sparePart)
    {
        return new SparePartResponse(
            sparePart.Id,
            sparePart.Name,
            sparePart.Quantity,
            sparePart.Price,
            sparePart.Location,
            sparePart.CreatedAt
        );
    }
}