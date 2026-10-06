using Domain.Entities;
using Domain.Entities.Petrol;

namespace Application.Service.Petrol;

// Permission dates may backdate an imported assignment. Possession is still ended
// by a return, a blocking status, or the next take by either the rider or vehicle.
internal static class PetrolAssignmentTimeline
{
    internal sealed class Window(RiderVehicleStatus status, DateTime start)
    {
        public RiderVehicleStatus Status { get; } = status;
        public DateTime Start { get; } = start;
        public DateTime End { get; set; } = DateTime.MaxValue;
    }

    internal readonly record struct RiderShare(long IqamaNo, int StatusId,
        PetrolAttributionSource Source, long DurationTicks);

    internal static List<Window> Build(IEnumerable<RiderVehicleStatus> statuses)
    {
        var windows = new List<Window>();
        var vehicles = new Dictionary<string, Window>(StringComparer.Ordinal);
        var riders = new Dictionary<long, Window>();

        void Close(Window window, DateTime at)
        {
            if (at < window.End) window.End = at;
            if (vehicles.TryGetValue(window.Status.VehicleNumber, out var vehicleWindow)
                && ReferenceEquals(window, vehicleWindow))
                vehicles.Remove(window.Status.VehicleNumber);
            var iqama = window.Status.EmployeeIqamaNo!.Value;
            if (riders.TryGetValue(iqama, out var riderWindow) && ReferenceEquals(window, riderWindow))
                riders.Remove(iqama);
        }

        foreach (var status in statuses.OrderBy(EffectiveTime).ThenBy(s => s.Timestamp).ThenBy(s => s.Id))
        {
            var at = EffectiveTime(status);
            if (IsTake(status) && status.EmployeeIqamaNo.HasValue)
            {
                if (vehicles.TryGetValue(status.VehicleNumber, out var previousVehicle))
                    Close(previousVehicle, at);
                if (riders.TryGetValue(status.EmployeeIqamaNo.Value, out var previousRider))
                    Close(previousRider, at);

                var window = new Window(status, at);
                windows.Add(window);
                vehicles[status.VehicleNumber] = window;
                riders[status.EmployeeIqamaNo.Value] = window;
            }
            else if (EndsPossession(status.StatusType)
                && vehicles.TryGetValue(status.VehicleNumber, out var holder)
                // A late return by the former rider must not revoke the new rider's take.
                && (status.StatusType != VehicleStatusType.Returned
                    || !status.EmployeeIqamaNo.HasValue
                    || status.EmployeeIqamaNo == holder.Status.EmployeeIqamaNo))
            {
                Close(holder, at);
            }
        }

        // A recorded release is the possession boundary, even if the permit
        // expired earlier. An inactive legacy row without a release can only
        // supply its explicit end date as a fallback.
        foreach (var window in windows.Where(w => w.End == DateTime.MaxValue
            && !w.Status.IsActive && w.Status.PermissionEndDate.HasValue))
            window.End = window.Status.PermissionEndDate!.Value;
        return windows;
    }

    internal static IReadOnlyList<RiderShare> OnDate(IEnumerable<Window> windows,
        string vehicleNumber, DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        return windows.Where(w => w.Status.VehicleNumber == vehicleNumber && w.Start < end && w.End > start)
            .Select(w => new
            {
                Window = w,
                Ticks = (Min(w.End, end) - Max(w.Start, start)).Ticks
            })
            .Where(w => w.Ticks > 0)
            .GroupBy(w => w.Window.Status.EmployeeIqamaNo!.Value)
            .Select(g =>
            {
                var latest = g.OrderByDescending(w => w.Window.Start)
                    .ThenByDescending(w => w.Window.Status.Id).First().Window.Status;
                return new RiderShare(g.Key, latest.Id,
                    latest.PermissionStartDate.HasValue
                        ? PetrolAttributionSource.Permission
                        : PetrolAttributionSource.VehicleStatusTimeline,
                    g.Sum(w => w.Ticks));
            })
            .OrderBy(r => r.IqamaNo)
            .ToList();
    }

    internal static bool IsTake(RiderVehicleStatus status) =>
        status.StatusType is VehicleStatusType.Taken or VehicleStatusType.switched;

    internal static DateTime EffectiveTime(RiderVehicleStatus status) =>
        IsTake(status) ? status.PermissionStartDate ?? status.Timestamp : status.Timestamp;

    private static bool EndsPossession(VehicleStatusType type) => type is
        VehicleStatusType.Returned or VehicleStatusType.Problem or VehicleStatusType.BreakUp
        or VehicleStatusType.Stolen or VehicleStatusType.OutOfService;

    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
}
