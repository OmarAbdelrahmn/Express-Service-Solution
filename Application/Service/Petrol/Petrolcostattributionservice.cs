using Domain;
using Domain.Entities;
using Domain.Entities.Petrol;
using Domain.Models.Petrol;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Petrol;

/// <summary>
/// Resolves which rider(s) held a given vehicle on a specific date, then writes
/// RiderPetrolCost rows for every VehiclePetrolCost that has not yet been attributed.
///
/// Attribution priority
/// ────────────────────
/// 1. Explicit Permission window  (PermissionStartDate ≤ date ≤ PermissionEndDate)
/// 2. Taken/Returned timeline     (most-recent Taken before midnight of date,
///                                 with no Returned before start-of-date)
/// 3. Unattributed                (stored with Source = Unattributed for manual review)
///
/// Multi-vehicle day  → handled automatically: we run per-vehicle, so one rider
///                      can receive costs from multiple VehiclePetrolCost rows.
/// Multi-rider day    → when a vehicle was switched mid-day, ALL riders who held it
///                      during the calendar day receive a RiderPetrolCost row.
/// </summary>
public class PetrolCostAttributionService(IApplicationDbContext db)
{
    // ── Public entry points ───────────────────────────────────────────────

    /// <summary>
    /// Attributes all pending VehiclePetrolCost rows (IsAttributed = false, no resolution error).
    /// Call this after an Excel upload or on a scheduled retry.
    /// </summary>
    public async Task AttributePendingAsync(CancellationToken ct = default)
    {
        var pending = await db.VehiclePetrolCosts
            .Where(v => !v.IsAttributed && !v.HasResolutionError)
            .ToListAsync(ct);

        foreach (var record in pending)
            await AttributeSingleAsync(record, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Attributes a single VehiclePetrolCost record and persists immediately.
    /// Useful for re-processing a single record after a manual vehicle fix.
    /// </summary>
    public async Task AttributeSingleAsync(VehiclePetrolCost record, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(record.VehicleNumber))
        {
            // Vehicle was never resolved — skip attribution until plate is fixed
            return;
        }

        var dayStart = record.Date.ToDateTime(TimeOnly.MinValue); // 00:00:00
        var dayEnd = record.Date.ToDateTime(TimeOnly.MaxValue); // 23:59:59

        var riders = await ResolveRidersForVehicleOnDateAsync(
            record.VehicleNumber, record.Date, dayStart, dayEnd, ct);

        if (riders.Count == 0)
        {
            db.RiderPetrolCosts.Add(new RiderPetrolCost
            {
                VehiclePetrolCostId = record.Id,
                VehicleNumber = record.VehicleNumber,
                Date = record.Date,
                Cost = record.Cost,
                RiderIqamaNo = null,
                AttributionSource = PetrolAttributionSource.Unattributed,
                Notes = "No active rider found for this vehicle on this date.",
                CreatedAt = DateTime.UtcNow.AddHours(3)
            });
        }
        else
        {
            foreach (var resolved in riders)
            {
                db.RiderPetrolCosts.Add(new RiderPetrolCost
                {
                    VehiclePetrolCostId = record.Id,
                    VehicleNumber = record.VehicleNumber,
                    Date = record.Date,
                    Cost = record.Cost,        // full cost to each rider
                    RiderIqamaNo = resolved.IqamaNo,
                    AttributionSource = resolved.Source,
                    ResolvedFromStatusId = resolved.StatusId,
                    Notes = resolved.Notes,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                });
            }
        }

        record.IsAttributed = true;
    }

    // ── Core resolution logic ─────────────────────────────────────────────

    /// <summary>
    /// Returns every rider who can be confirmed to have held <paramref name="vehicleNumber"/>
    /// during the calendar day represented by <paramref name="reportDate"/>.
    /// </summary>
    private async Task<IReadOnlyList<ResolvedRider>> ResolveRidersForVehicleOnDateAsync(
        string vehicleNumber,
        DateOnly reportDate,
        DateTime dayStart,
        DateTime dayEnd,
        CancellationToken ct)
    {
        // Load all RiderVehicleStatus rows for this vehicle, ordered by time.
        // Pulling all into memory is safe: one vehicle typically has < 200 status rows.
        var allStatuses = await db.RiderVehicleStatuses
            .Where(s => s.VehicleNumber == vehicleNumber)
            .OrderBy(s => s.Timestamp)
            .AsNoTracking()
            .ToListAsync(ct);

        var results = new List<ResolvedRider>();

        // ── Priority 1: Explicit permission window ────────────────────────
        var permissionHolders = allStatuses
            .Where(s => s.EmployeeIqamaNo.HasValue
                     && s.PermissionStartDate.HasValue
                     && s.PermissionEndDate.HasValue
                     && s.PermissionStartDate.Value.Date <= reportDate.ToDateTime(TimeOnly.MinValue).Date
                     && s.PermissionEndDate.Value.Date >= reportDate.ToDateTime(TimeOnly.MaxValue).Date)
            .ToList();

        if (permissionHolders.Count > 0)
        {
            foreach (var status in permissionHolders)
            {
                results.Add(new ResolvedRider(
                    IqamaNo: status.EmployeeIqamaNo!.Value,
                    Source: PetrolAttributionSource.Permission,
                    StatusId: status.Id,
                    Notes: $"Permission window: {status.PermissionStartDate:yyyy-MM-dd} → {status.PermissionEndDate:yyyy-MM-dd}"));
            }

            return Deduplicate(results);
        }

        // ── Priority 2: Taken / Returned timeline ─────────────────────────
        //
        // Walk through all status events. Maintain a "currently holding" set.
        // A rider enters the set on Taken / switched (as substitute).
        // A rider leaves the set on Returned / switched (as original) / BreakUp / OutOfService / Stolen.
        //
        // After processing events up to end-of-day, whoever is still in the set
        // was holding the vehicle at some point during the day.

        var activeAtAnyPointToday = new Dictionary<long, int>(); // iqama → statusId that "opened" the window

        long? currentHolder = null;
        int? currentStatusId = null;

        foreach (var evt in allStatuses)
        {
            if (evt.Timestamp > dayEnd)
                break; // past the day we care about

            switch (evt.StatusType)
            {
                case VehicleStatusType.Taken:
                    // New rider takes the vehicle
                    if (evt.EmployeeIqamaNo.HasValue)
                    {
                        currentHolder = evt.EmployeeIqamaNo.Value;
                        currentStatusId = evt.Id;

                        // If the Taken event falls on or before end-of-day, mark them active
                        if (evt.Timestamp.Date <= reportDate.ToDateTime(TimeOnly.MaxValue).Date)
                            activeAtAnyPointToday[currentHolder.Value] = currentStatusId!.Value;
                    }
                    break;

                case VehicleStatusType.switched:
                    // Vehicle switched to a different rider.
                    // The new holder is in EmployeeIqamaNo.
                    if (evt.EmployeeIqamaNo.HasValue)
                    {
                        currentHolder = evt.EmployeeIqamaNo.Value;
                        currentStatusId = evt.Id;

                        if (evt.Timestamp.Date <= reportDate.ToDateTime(TimeOnly.MaxValue).Date)
                            activeAtAnyPointToday[currentHolder.Value] = currentStatusId!.Value;
                    }
                    break;

                case VehicleStatusType.Returned:
                case VehicleStatusType.BreakUp:
                case VehicleStatusType.Stolen:
                case VehicleStatusType.OutOfService:
                    // Rider no longer holds the vehicle.
                    // Only remove from "active today" if the return happened BEFORE the report day.
                    // If the return was during the report day or after → they still held it during the day.
                    if (evt.Timestamp.Date < reportDate.ToDateTime(TimeOnly.MinValue).Date)
                    {
                        if (currentHolder.HasValue)
                            activeAtAnyPointToday.Remove(currentHolder.Value);

                        currentHolder = null;
                        currentStatusId = null;
                    }
                    break;

                // Problem / fixProblem do not change the holder
                case VehicleStatusType.Problem:
                case VehicleStatusType.fixProblem:
                default:
                    break;
            }
        }

        foreach (var (iqama, statusId) in activeAtAnyPointToday)
        {
            results.Add(new ResolvedRider(
                IqamaNo: iqama,
                Source: PetrolAttributionSource.VehicleStatusTimeline,
                StatusId: statusId,
                Notes: activeAtAnyPointToday.Count > 1
                    ? $"Vehicle had {activeAtAnyPointToday.Count} riders on this date (switch/multi-hold); cost attributed to each."
                    : null));
        }

        return Deduplicate(results);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>If the same rider appears twice (e.g., via two overlapping statuses), keep one.</summary>
    private static IReadOnlyList<ResolvedRider> Deduplicate(List<ResolvedRider> riders) =>
        riders.GroupBy(r => r.IqamaNo)
              .Select(g => g.First())
              .ToList();

    // ── Internal value type ───────────────────────────────────────────────

    private readonly record struct ResolvedRider(
        long IqamaNo,
        PetrolAttributionSource Source,
        int StatusId,
        string? Notes);
}