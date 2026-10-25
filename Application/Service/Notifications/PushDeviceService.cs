using Application.Abstraction;
using Application.Contracts.Notifications;
using Domain;
using Domain.Entities.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Service.Notifications;

public interface IPushDeviceService
{
    Task<Result> RegisterAsync(Guid installationId, int riderId, Guid sessionId, RegisterPushDeviceRequest request, CancellationToken ct);
    Task<Result> UnregisterAsync(Guid installationId, int riderId, Guid sessionId, CancellationToken ct);
}

public static class NotificationDeviceLock
{
    public static async Task<IDbContextTransaction?> BeginAsync(ApplicationDbcontext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serialize ownership/session transitions across all API instances.
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync("""
                    DECLARE @result int;
                    EXEC @result = sp_getapplock @Resource='Notifications:Devices',
                        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                    IF @result < 0 THROW 51000, 'Notification device lock unavailable.', 1;
                    """, ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}

public class PushDeviceService(ApplicationDbcontext db, IDataProtectionProvider protection, TimeProvider clock) : IPushDeviceService
{
    private readonly IDataProtector protector = protection.CreateProtector("ExpressService.PushDestinations.v1");

    public async Task<Result> RegisterAsync(Guid installationId, int riderId, Guid sessionId,
        RegisterPushDeviceRequest request, CancellationToken ct)
    {
        if (installationId == Guid.Empty || string.IsNullOrWhiteSpace(request.FirebaseInstallationId) ||
            request.FirebaseInstallationId.Length > 256 || request.Platform != "android" ||
            request.Permission != "authorized" || request.AppVersion?.Length > 64)
            return Result.Failure(NotificationRules.Invalid("An installation UUID, Android platform, authorized permission and valid FID are required."));
        var now = clock.GetUtcNow().UtcDateTime;
        var fid = request.FirebaseInstallationId.Trim();
        var hash = NotificationRules.Hash(fid);
        await using var transaction = await NotificationDeviceLock.BeginAsync(db, ct);
        if (!await db.RiderMobileSessions.AnyAsync(x => x.Id == sessionId && x.RiderId == riderId &&
            x.RevokedAtUtc == null && x.ExpiresAtUtc > now && !x.Rider.Employee.IsDeleted &&
            x.Rider.Employee.Status == "enable" && x.Rider.Employee.INKSA, ct))
            return Result.Failure(new Error("Notifications.InvalidSession", "The mobile session is no longer valid.", 401));
        var device = await db.RiderPushDevices.SingleOrDefaultAsync(x => x.InstallationId == installationId, ct);
        var duplicates = await db.RiderPushDevices.Where(x => x.DestinationHash == hash && x.IsActive &&
            x.InstallationId != installationId).ToListAsync(ct);
        foreach (var duplicate in duplicates)
        {
            duplicate.IsActive = false;
            duplicate.Generation++;
            duplicate.UpdatedAtUtc = now;
        }
        try
        {
            // Release the filtered unique destination before assigning its new owner.
            if (duplicates.Count > 0) await db.SaveChangesAsync(ct);
            if (device is null)
            {
                device = new RiderPushDevice { InstallationId = installationId };
                db.RiderPushDevices.Add(device);
            }
            if (!device.IsActive || device.RiderId != riderId || device.SessionId != sessionId || device.DestinationHash != hash)
                device.Generation++;
            device.RiderId = riderId;
            device.SessionId = sessionId;
            device.ProtectedDestination = protector.Protect(fid);
            device.DestinationHash = hash;
            device.IsActive = true;
            device.AppVersion = request.AppVersion;
            device.LastSeenAtUtc = device.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result.Failure(NotificationRules.Conflict);
        }
    }

    public async Task<Result> UnregisterAsync(Guid installationId, int riderId, Guid sessionId, CancellationToken ct)
    {
        await using var transaction = await NotificationDeviceLock.BeginAsync(db, ct);
        var device = await db.RiderPushDevices.SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.RiderId == riderId && x.SessionId == sessionId, ct);
        if (device is not null)
        {
            device.IsActive = false;
            device.Generation++;
            device.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Result.Success();
    }
}
