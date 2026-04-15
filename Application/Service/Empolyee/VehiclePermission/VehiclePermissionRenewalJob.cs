using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Service.VehiclePermission;

public interface IVehiclePermissionRenewalJob
{
    Task RunAsync(CancellationToken ct = default);
}

public class VehiclePermissionRenewalJob(
    ApplicationDbcontext db,
    ILogger<VehiclePermissionRenewalJob> logger) : IVehiclePermissionRenewalJob
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        logger.LogInformation("VehiclePermissionRenewalJob starting for {Date}", today);

        // Fetch all active permission records whose PermissionEndDate falls today
        var expiring = await db.RiderVehicleStatus
            .Where(r =>
                r.IsActive &&
                r.Permission != null && r.Permission != string.Empty &&
                r.PermissionEndDate.HasValue &&
                DateOnly.FromDateTime(r.PermissionEndDate.Value) == today)
            .ToListAsync(ct);

        if (expiring.Count == 0)
        {
            logger.LogInformation("No expiring vehicle permissions found for {Date}", today);
            return;
        }

        foreach (var record in expiring)
        {
            var oldEnd = record.PermissionEndDate!.Value;
            var newEnd = oldEnd.AddYears(1).AddDays(-1);

            logger.LogInformation(
                "Renewing permission for VehicleNumber={Vehicle}, RiderIqama={Rider}: {Old} → {New}",
                record.VehicleNumber,
                record.EmployeeIqamaNo,
                oldEnd,
                newEnd);

            record.PermissionEndDate = newEnd;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "VehiclePermissionRenewalJob completed. Renewed {Count} permission(s) for {Date}",
            expiring.Count, today);
    }
}