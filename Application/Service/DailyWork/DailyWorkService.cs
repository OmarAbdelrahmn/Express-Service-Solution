using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.DailyWork;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.DailyWork;

public class DailyWorkService(ApplicationDbcontext db) : IDailyWorkService
{
    public async Task<Result<DailyWorkResponse>> GetAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        if (date == default)
            return Result.Failure<DailyWorkResponse>(DailyWorkErrors.InvalidDate);

        var items = await db.DailyWorkEntries.AsNoTracking()
            .Where(x => x.Date == date)
            .OrderBy(x => x.EmployeeIqamaNo)
            .Select(x => new DailyWorkResponseItem(
                x.Employee.RiderDetails == null ? null : (int?)x.Employee.RiderDetails.Id,
                x.EmployeeIqamaNo, x.Orders, x.WorkingHours))
            .ToListAsync(cancellationToken);

        return Result.Success(new DailyWorkResponse(date, items));
    }

    public async Task<Result<DailyWorkResponse>> EditAsync(
        EditDailyWorkRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Date == default)
            return Result.Failure<DailyWorkResponse>(DailyWorkErrors.InvalidDate);
        if (request.Items is null || request.Items.Count is < 1 or > 1000)
            return Result.Failure<DailyWorkResponse>(DailyWorkErrors.InvalidItems);
        if (request.Items.Any(x => x is null ||
            x.RiderId.HasValue == x.EmployeeIqamaNo.HasValue ||
            x.RiderId is <= 0 || x.EmployeeIqamaNo is <= 0 || x.Orders < 0 ||
            x.WorkingHours is < 0 or > 24 || decimal.Round(x.WorkingHours, 2) != x.WorkingHours))
            return Result.Failure<DailyWorkResponse>(DailyWorkErrors.InvalidItem);

        var riderIds = request.Items.Where(x => x.RiderId.HasValue).Select(x => x.RiderId!.Value).Distinct().ToList();
        var employeeIds = request.Items.Where(x => x.EmployeeIqamaNo.HasValue).Select(x => x.EmployeeIqamaNo!.Value).Distinct().ToList();
        var riders = await db.RiderDetails.AsNoTracking()
            .Where(x => riderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.EmployeeIqamaNo, cancellationToken);
        var employees = await db.Employees.AsNoTracking()
            .Where(x => employeeIds.Contains(x.IqamaNo))
            .Select(x => x.IqamaNo).ToListAsync(cancellationToken);

        if (riders.Count != riderIds.Count || employees.Count != employeeIds.Count)
            return Result.Failure<DailyWorkResponse>(DailyWorkErrors.PersonNotFound);

        // Resolve and validate the entire batch before tracking any changes.
        var resolved = request.Items.Select(x => new
        {
            EmployeeIqamaNo = x.EmployeeIqamaNo ?? riders[x.RiderId!.Value],
            x.Orders,
            x.WorkingHours
        }).ToList();
        if (resolved.Select(x => x.EmployeeIqamaNo).Distinct().Count() != resolved.Count)
            return Result.Failure<DailyWorkResponse>(DailyWorkErrors.DuplicateEmployee);

        var resolvedIds = resolved.Select(x => x.EmployeeIqamaNo).ToList();
        var existing = await db.DailyWorkEntries
            .Where(x => x.Date == request.Date && resolvedIds.Contains(x.EmployeeIqamaNo))
            .ToDictionaryAsync(x => x.EmployeeIqamaNo, cancellationToken);

        foreach (var item in resolved)
        {
            if (!existing.TryGetValue(item.EmployeeIqamaNo, out var entry))
            {
                entry = new DailyWorkEntry { Date = request.Date, EmployeeIqamaNo = item.EmployeeIqamaNo };
                db.DailyWorkEntries.Add(entry);
            }
            entry.Orders = item.Orders;
            entry.WorkingHours = item.WorkingHours;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(request.Date, cancellationToken);
    }
}
