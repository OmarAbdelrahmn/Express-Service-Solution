// Application/Service/RiderAccessory/RiderAccessoryService.cs
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.RiderAccessoryCon;
using Domain;
using Domain.Entities;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.RiderAccessory;

public class RiderAccessoryService(ApplicationDbcontext dbcontext) : IRiderAccessoryService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<IEnumerable<RiderAccessoryResponse>>> GetAllAsync()
    {
        var accessories = await _dbcontext.RiderAccessories
            .Include(a => a.RiderAccessoryUsages)
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync();

        var response = accessories.Select(MapToResponse);
        return Result.Success<IEnumerable<RiderAccessoryResponse>>(response);
    }

    public async Task<Result<RiderAccessoryResponse>> GetByIdAsync(int id)
    {
        var accessory = await _dbcontext.RiderAccessories
            .Include(a => a.RiderAccessoryUsages)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (accessory == null)
            return Result.Failure<RiderAccessoryResponse>(
                new Error("NotFound", "Accessory not found", 404));

        return Result.Success(MapToResponse(accessory));
    }

    public async Task<Result<RiderAccessoryResponse>> CreateAsync(RiderAccessoryRequest request)
    {
        var accessory = new Domain.Entities.Spare.RiderAccessory
        {
            Name = request.Name,
            Quantity = request.Quantity,
            Price = request.Price,
            Location = request.Location,
            CreatedAt = DateTime.UtcNow.AddHours(3)
        };

        await _dbcontext.RiderAccessories.AddAsync(accessory);
        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(accessory));
    }

    public async Task<Result<RiderAccessoryResponse>> UpdateAsync(int id, RiderAccessoryRequest request)
    {
        var accessory = await _dbcontext.RiderAccessories.FindAsync(id);

        if (accessory == null)
            return Result.Failure<RiderAccessoryResponse>(
                new Error("NotFound", "Accessory not found", 404));

        accessory.Name = request.Name;
        accessory.Quantity = request.Quantity;
        accessory.Price = request.Price;
        accessory.Location = request.Location;

        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(accessory));
    }

    public async Task<Result> DeleteAsync(int id)
    {
        var accessory = await _dbcontext.RiderAccessories.FindAsync(id);

        if (accessory == null)
            return Result.Failure(
                new Error("NotFound", "Accessory not found", 404));

        _dbcontext.RiderAccessories.Remove(accessory);
        await _dbcontext.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<IEnumerable<RiderAccessoryResponse>>> SearchAsync(string keyword)
    {
        keyword = keyword.ToLower();

        var accessories = await _dbcontext.RiderAccessories
            .Include(a => a.RiderAccessoryUsages)
            .Where(a => a.Name.ToLower().Contains(keyword) ||
                       a.Location.ToLower().Contains(keyword))
            .AsNoTracking()
            .ToListAsync();

        var response = accessories.Select(MapToResponse);
        return Result.Success<IEnumerable<RiderAccessoryResponse>>(response);
    }

    public async Task<Result<RiderAccessoryUsageResponse>> IssueToRiderAsync(
        int accessoryId,
        IssueAccessoryRequest request)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            var accessory = await _dbcontext.RiderAccessories.FindAsync(accessoryId);

            if (accessory == null)
                return Result.Failure<RiderAccessoryUsageResponse>(
                    new Error("NotFound", "Accessory not found", 404));

            if (accessory.Quantity <= 0)
                return Result.Failure<RiderAccessoryUsageResponse>(
                    new Error("OutOfStock", "Accessory is out of stock", 400));

            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.Id == request.RiderId);

            if (rider == null)
                return Result.Failure<RiderAccessoryUsageResponse>(
                    new Error("RiderNotFound", "Rider not found", 404));

            // Check if rider already has this accessory
            var existingUsage = await _dbcontext.RiderAccessoryUsages
                .AnyAsync(u => u.RiderAccessoryId == accessoryId &&
                              u.RiderId == request.RiderId);

            if (existingUsage)
                return Result.Failure<RiderAccessoryUsageResponse>(
                    new Error("AlreadyIssued",
                        "Rider already has this accessory", 400));

            // Create usage record
            var usage = new RiderAccessoryUsage
            {
                RiderAccessoryId = accessoryId,
                RiderId = request.RiderId,
                IssuedAt = DateTime.UtcNow.AddHours(3),
            };

            await _dbcontext.RiderAccessoryUsages.AddAsync(usage);

            // Update quantity
            accessory.Quantity--;

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            // Reload with includes for response
            usage = await _dbcontext.RiderAccessoryUsages
                .Include(u => u.RiderAccessory)
                .Include(u => u.Rider)
                    .ThenInclude(r => r.Employee)
                .FirstAsync(u => u.Id == usage.Id);

            return Result.Success(MapUsageToResponse(usage));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<RiderAccessoryUsageResponse>(
                new Error("IssueError", $"Failed to issue accessory: {ex.Message}", 500));
        }
    }
    public async Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetRiderAccessoriesAsync(int riderId)
    {
        var usages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => u.RiderId == riderId)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(MapUsageToResponse);
        return Result.Success<IEnumerable<RiderAccessoryUsageResponse>>(response);
    }

    public async Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetAccessoryHistoryAsync(int accessoryId)
    {
        var usages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => u.RiderAccessoryId == accessoryId)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(MapUsageToResponse);
        return Result.Success<IEnumerable<RiderAccessoryUsageResponse>>(response);
    }

    private static RiderAccessoryResponse MapToResponse(Domain.Entities.Spare.RiderAccessory accessory)
    {
        int available = accessory.Quantity;

        return new RiderAccessoryResponse(
            accessory.Id,
            accessory.Name,
            accessory.Quantity, // Total
            available,
            accessory.Price,
            accessory.Location,
            accessory.CreatedAt
        );
    }

    private static RiderAccessoryUsageResponse MapUsageToResponse(RiderAccessoryUsage usage)
    {
        return new RiderAccessoryUsageResponse(
            usage.Id,
            usage.RiderAccessoryId,
            usage.RiderAccessory.Name,
            usage.RiderId,
            usage.Rider.Employee.NameEN,
            usage.Rider.Employee.NameAR,
            usage.IssuedAt
        );
    }
}