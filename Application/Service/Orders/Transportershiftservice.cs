using Application.Abstraction;
using Application.Contracts.TransporterShifts;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.TransporterShifts;

public class TransporterShiftService(ApplicationDbcontext db) : ITransporterShiftService
{
    // ══════════════════════════════════════════════════════════════════════════
    // Import
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<Result<ImportResultResponse>> ImportScheduleAsync(
        ImportTransporterScheduleRequest request,
        string importedBy)
    {
        try
        {
            // ── Load all Company 3 riders once ────────────────────────────
            var riders = await db.RiderDetails
                .Where(r => r.CompanyId == 3)
                .Include(r => r.Employee)
                .AsNoTracking()
                .ToListAsync();

            var riderByWorkingId = riders
                .Where(r => !string.IsNullOrEmpty(r.WorkingId))
                .ToDictionary(r => r.WorkingId!.Trim(), r => r, StringComparer.OrdinalIgnoreCase);

            int shiftsCreated = 0;
            int breakDays = 0;
            int unmatched = 0;
            var warnings = new List<string>();
            var newShifts = new List<TransporterShift>();
            var keysToDelete = new HashSet<(int riderId, DateOnly date)>();

            foreach (var cell in request.Cells)
            {
                // ── Match TransporterId → RiderDetails ────────────────────
                if (!riderByWorkingId.TryGetValue(cell.TransporterId.Trim(), out var rider))
                {
                    warnings.Add($"TransporterId '{cell.TransporterId}' not found (Associate: {cell.AssociateName}).");
                    unmatched++;
                    continue;
                }

                // ── Parse column header → DateOnly ────────────────────────
                var date = ScheduleHeaderParser.Parse(cell.ColumnHeader, request.OverrideYear);
                if (date is null)
                {
                    warnings.Add($"Could not parse column header '{cell.ColumnHeader}' for rider {cell.TransporterId}.");
                    continue;
                }

                // Track which (riderId, date) pairs we will replace
                keysToDelete.Add((rider.Id, date.Value));

                // ── Parse cell content ────────────────────────────────────
                var parsed = ShiftCellParser.Parse(cell.CellContent);

                if (parsed.IsBreakDay)
                {
                    // One break-day record
                    newShifts.Add(new TransporterShift
                    {
                        RiderId = rider.Id,
                        WorkingId = rider.WorkingId!,
                        ShiftDate = date.Value,
                        ShiftIndex = 1,
                        IsBreakDay = true,
                        RawEntry = cell.CellContent,
                        CreatedAt = DateTime.UtcNow.AddHours(3)
                    });
                    breakDays++;
                    continue;
                }

                foreach (var block in parsed.Shifts)
                {
                    newShifts.Add(new TransporterShift
                    {
                        RiderId = rider.Id,
                        WorkingId = rider.WorkingId!,
                        ShiftDate = date.Value,
                        ShiftIndex = block.ShiftIndex,
                        StartTime = block.StartTime,
                        EndTime = block.EndTime,
                        DurationHours = block.DurationHours,
                        IsBreakDay = false,
                        RawEntry = block.RawEntry,
                        CreatedAt = DateTime.UtcNow.AddHours(3)
                    });
                    shiftsCreated++;
                }
            }

            // ── Delete stale records for the same (riderId, date) pairs ──
            if (keysToDelete.Count > 0)
            {
                var riderIds = keysToDelete.Select(k => k.riderId).Distinct().ToList();
                var dates = keysToDelete.Select(k => k.date).Distinct().ToList();

                var stale = await db.TransporterShifts
                    .Where(s => riderIds.Contains(s.RiderId) && dates.Contains(s.ShiftDate))
                    .ToListAsync();

                // Only remove those actually in our batch
                var staleFiltered = stale
                    .Where(s => keysToDelete.Contains((s.RiderId, s.ShiftDate)))
                    .ToList();

                db.TransporterShifts.RemoveRange(staleFiltered);
            }

            await db.TransporterShifts.AddRangeAsync(newShifts);
            await db.SaveChangesAsync();

            return Result.Success(new ImportResultResponse(
                TotalCellsProcessed: request.Cells.Count,
                ShiftsCreated: shiftsCreated,
                BreakDaysMarked: breakDays,
                UnmatchedTransporterIds: unmatched,
                Warnings: warnings
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<ImportResultResponse>(new Error(
                "TransporterShift.ImportFailed",
                $"Import failed: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Queries
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<Result<DayScheduleSummaryResponse>> GetDayScheduleAsync(DateOnly date)
    {
        try
        {
            var (riders, shifts) = await LoadRidersAndShiftsAsync(date);

            var riderRows = BuildRiderDayShifts(riders, shifts, date);

            return Result.Success(new DayScheduleSummaryResponse(
                Date: date,
                TotalRiders: riders.Count,
                RidersWithShifts: riderRows.Count(r => r.HasShift && !r.IsBreakDay),
                RidersOnBreak: riderRows.Count(r => r.IsBreakDay),
                RidersWithNoData: riderRows.Count(r => !r.HasShift && !r.IsBreakDay),
                Riders: riderRows
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DayScheduleSummaryResponse>(new Error(
                "TransporterShift.DayScheduleFailed", ex.Message, 500));
        }
    }

    public async Task<Result<TimeSlotRidersResponse>> GetActiveAtTimeAsync(DateOnly date, TimeOnly time)
    {
        try
        {
            var (riders, shifts) = await LoadRidersAndShiftsAsync(date);
            var riderRows = BuildRiderDayShifts(riders, shifts, date);

            var active = new List<RiderDayShiftResponse>();
            var inactive = new List<RiderDayShiftResponse>();

            foreach (var row in riderRows)
            {
                bool isActive = row.Shifts.Any(s =>
                    s.StartTime.HasValue &&
                    s.EndTime.HasValue &&
                    IsTimeInShift(s.StartTime.Value, s.EndTime.Value, time));

                if (isActive) active.Add(row);
                else inactive.Add(row);
            }

            return Result.Success(new TimeSlotRidersResponse(
                Date: date,
                Time: time,
                ActiveCount: active.Count,
                InactiveCount: inactive.Count,
                ActiveRiders: active,
                InactiveRiders: inactive
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<TimeSlotRidersResponse>(new Error(
                "TransporterShift.TimeSlotFailed", ex.Message, 500));
        }
    }

    public async Task<Result<RiderMonthlyScheduleResponse>> GetRiderMonthlyScheduleAsync(
        int riderId, int year, int month)
    {
        try
        {
            var rider = await db.RiderDetails
                .Include(r => r.Employee)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == riderId && r.CompanyId == 3);

            if (rider is null)
                return Result.Failure<RiderMonthlyScheduleResponse>(new Error(
                    "TransporterShift.RiderNotFound", "Rider not found in Company 3.", 404));

            var firstDay = new DateOnly(year, month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var shifts = await db.TransporterShifts
                .Where(s => s.RiderId == riderId &&
                            s.ShiftDate >= firstDay &&
                            s.ShiftDate <= lastDay)
                .AsNoTracking()
                .ToListAsync();

            var daily = new List<RiderDayShiftResponse>();
            for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
            {
                var dayShifts = shifts.Where(s => s.ShiftDate == d).ToList();
                daily.Add(MapToRiderDay(rider, d, dayShifts));
            }

            return Result.Success(new RiderMonthlyScheduleResponse(
                RiderId: rider.Id,
                WorkingId: rider.WorkingId ?? "",
                NameEN: rider.Employee?.NameEN ?? "",
                NameAR: rider.Employee?.NameAR ?? "",
                Year: year,
                Month: month,
                TotalWorkingDays: daily.Count(d => d.HasShift && !d.IsBreakDay),
                TotalBreakDays: daily.Count(d => d.IsBreakDay),
                TotalDaysWithNoData: daily.Count(d => !d.HasShift && !d.IsBreakDay),
                TotalScheduledHours: daily.Sum(d => d.TotalHoursScheduled),
                DailyBreakdown: daily
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderMonthlyScheduleResponse>(new Error(
                "TransporterShift.MonthlyFailed", ex.Message, 500));
        }
    }

    public async Task<Result<List<RiderMonthlyScheduleResponse>>> GetAllRidersMonthlyScheduleAsync(
        int year, int month)
    {
        try
        {
            var riders = await db.RiderDetails
                .Where(r => r.CompanyId == 3)
                .Include(r => r.Employee)
                .AsNoTracking()
                .ToListAsync();

            var firstDay = new DateOnly(year, month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var allShifts = await db.TransporterShifts
                .Where(s => s.ShiftDate >= firstDay && s.ShiftDate <= lastDay)
                .AsNoTracking()
                .ToListAsync();

            var result = new List<RiderMonthlyScheduleResponse>();

            foreach (var rider in riders)
            {
                var riderShifts = allShifts.Where(s => s.RiderId == rider.Id).ToList();
                var daily = new List<RiderDayShiftResponse>();

                for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
                {
                    var dayShifts = riderShifts.Where(s => s.ShiftDate == d).ToList();
                    daily.Add(MapToRiderDay(rider, d, dayShifts));
                }

                result.Add(new RiderMonthlyScheduleResponse(
                    RiderId: rider.Id,
                    WorkingId: rider.WorkingId ?? "",
                    NameEN: rider.Employee?.NameEN ?? "",
                    NameAR: rider.Employee?.NameAR ?? "",
                    Year: year,
                    Month: month,
                    TotalWorkingDays: daily.Count(d => d.HasShift && !d.IsBreakDay),
                    TotalBreakDays: daily.Count(d => d.IsBreakDay),
                    TotalDaysWithNoData: daily.Count(d => !d.HasShift && !d.IsBreakDay),
                    TotalScheduledHours: daily.Sum(d => d.TotalHoursScheduled),
                    DailyBreakdown: daily
                ));
            }

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<RiderMonthlyScheduleResponse>>(new Error(
                "TransporterShift.AllMonthlyFailed", ex.Message, 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Edits
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<Result<ShiftBlockResponse>> UpsertShiftAsync(
        UpsertShiftRequest request, string updatedBy)
    {
        try
        {
            var rider = await db.RiderDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RiderId && r.CompanyId == 3);

            if (rider is null)
                return Result.Failure<ShiftBlockResponse>(new Error(
                    "TransporterShift.RiderNotFound", "Rider not found in Company 3.", 404));

            var existing = await db.TransporterShifts
                .FirstOrDefaultAsync(s =>
                    s.RiderId == request.RiderId &&
                    s.ShiftDate == request.ShiftDate &&
                    s.ShiftIndex == request.ShiftIndex);

            TimeOnly? endTime = null;
            if (request.StartTime.HasValue && request.DurationHours > 0)
                endTime = request.StartTime.Value.AddMinutes((int)(request.DurationHours * 60));

            var now = DateTime.UtcNow.AddHours(3);

            if (existing is null)
            {
                existing = new TransporterShift
                {
                    RiderId = request.RiderId,
                    WorkingId = rider.WorkingId ?? "",
                    ShiftDate = request.ShiftDate,
                    ShiftIndex = request.ShiftIndex,
                    IsManuallyEdited = true,
                    CreatedAt = now
                };
                await db.TransporterShifts.AddAsync(existing);
            }

            existing.StartTime = request.StartTime;
            existing.EndTime = endTime;
            existing.DurationHours = request.DurationHours;
            existing.IsBreakDay = request.IsBreakDay;
            existing.Notes = request.Notes;
            existing.IsManuallyEdited = true;
            existing.UpdatedAt = now;
            existing.UpdatedBy = updatedBy;

            await db.SaveChangesAsync();
            return Result.Success(MapToBlock(existing));
        }
        catch (Exception ex)
        {
            return Result.Failure<ShiftBlockResponse>(new Error(
                "TransporterShift.UpsertFailed", ex.Message, 500));
        }
    }

    public async Task<Result<ShiftBlockResponse>> PatchShiftTimesAsync(PatchShiftTimesRequest request)
    {
        try
        {
            var shift = await db.TransporterShifts.FindAsync(request.ShiftId);
            if (shift is null)
                return Result.Failure<ShiftBlockResponse>(new Error(
                    "TransporterShift.NotFound", "Shift not found.", 404));

            var now = DateTime.UtcNow.AddHours(3);

            if (request.NewStartTime.HasValue)
                shift.StartTime = request.NewStartTime;

            if (request.NewDurationHours.HasValue && request.NewDurationHours > 0)
            {
                shift.DurationHours = request.NewDurationHours.Value;
                if (shift.StartTime.HasValue)
                    shift.EndTime = shift.StartTime.Value
                        .AddMinutes((int)(shift.DurationHours * 60));
            }

            if (request.Notes is not null)
                shift.Notes = request.Notes;

            shift.IsManuallyEdited = true;
            shift.UpdatedAt = now;
            shift.UpdatedBy = request.UpdatedBy;

            await db.SaveChangesAsync();
            return Result.Success(MapToBlock(shift));
        }
        catch (Exception ex)
        {
            return Result.Failure<ShiftBlockResponse>(new Error(
                "TransporterShift.PatchFailed", ex.Message, 500));
        }
    }

    public async Task<Result> DeleteShiftAsync(int shiftId, string deletedBy)
    {
        try
        {
            var shift = await db.TransporterShifts.FindAsync(shiftId);
            if (shift is null)
                return Result.Failure(new Error(
                    "TransporterShift.NotFound", "Shift not found.", 404));

            db.TransporterShifts.Remove(shift);
            await db.SaveChangesAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(
                "TransporterShift.DeleteFailed", ex.Message, 500));
        }
    }

    public async Task<Result> MarkBreakDayAsync(int riderId, DateOnly date, string updatedBy)
    {
        try
        {
            var rider = await db.RiderDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == riderId && r.CompanyId == 3);

            if (rider is null)
                return Result.Failure(new Error(
                    "TransporterShift.RiderNotFound", "Rider not found in Company 3.", 404));

            // Remove any existing shifts for that day
            var existing = await db.TransporterShifts
                .Where(s => s.RiderId == riderId && s.ShiftDate == date)
                .ToListAsync();

            db.TransporterShifts.RemoveRange(existing);

            // Add break-day sentinel
            await db.TransporterShifts.AddAsync(new TransporterShift
            {
                RiderId = riderId,
                WorkingId = rider.WorkingId ?? "",
                ShiftDate = date,
                ShiftIndex = 1,
                IsBreakDay = true,
                IsManuallyEdited = true,
                UpdatedAt = DateTime.UtcNow.AddHours(3),
                UpdatedBy = updatedBy,
                CreatedAt = DateTime.UtcNow.AddHours(3)
            });

            await db.SaveChangesAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(
                "TransporterShift.BreakMarkFailed", ex.Message, 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<(List<RiderDetails> riders, List<TransporterShift> shifts)>
        LoadRidersAndShiftsAsync(DateOnly date)
    {
        var riders = await db.RiderDetails
            .Where(r => r.CompanyId == 3)
            .Include(r => r.Employee)
            .AsNoTracking()
            .ToListAsync();

        var riderIds = riders.Select(r => r.Id).ToList();

        var shifts = await db.TransporterShifts
            .Where(s => s.ShiftDate == date && riderIds.Contains(s.RiderId))
            .AsNoTracking()
            .ToListAsync();

        return (riders, shifts);
    }

    private static List<RiderDayShiftResponse> BuildRiderDayShifts(
        List<RiderDetails> riders,
        List<TransporterShift> shifts,
        DateOnly date)
    {
        return riders
            .Select(r =>
            {
                var dayShifts = shifts.Where(s => s.RiderId == r.Id).ToList();
                return MapToRiderDay(r, date, dayShifts);
            })
            .OrderBy(r => r.WorkingId)
            .ToList();
    }

    private static RiderDayShiftResponse MapToRiderDay(
        RiderDetails rider,
        DateOnly date,
        List<TransporterShift> dayShifts)
    {
        bool isBreak = dayShifts.Any(s => s.IsBreakDay);
        bool hasShift = dayShifts.Count > 0;
        float totalHours = isBreak ? 0f : dayShifts.Sum(s => s.DurationHours);

        var blocks = dayShifts
            .Where(s => !s.IsBreakDay)
            .OrderBy(s => s.ShiftIndex)
            .Select(MapToBlock)
            .ToList();

        return new RiderDayShiftResponse(
            RiderId: rider.Id,
            WorkingId: rider.WorkingId ?? "",
            NameEN: rider.Employee?.NameEN ?? "",
            NameAR: rider.Employee?.NameAR ?? "",
            ShiftDate: date,
            HasShift: hasShift,
            IsBreakDay: isBreak,
            TotalHoursScheduled: totalHours,
            Shifts: blocks
        );
    }

    private static ShiftBlockResponse MapToBlock(TransporterShift s) =>
        new(
            Id: s.Id,
            ShiftIndex: s.ShiftIndex,
            StartTime: s.StartTime,
            EndTime: s.EndTime,
            DurationHours: s.DurationHours,
            IsBreakDay: s.IsBreakDay,
            RawEntry: s.RawEntry,
            IsManuallyEdited: s.IsManuallyEdited,
            Notes: s.Notes
        );

    /// <summary>
    /// Returns true if <paramref name="time"/> falls within [start, end).
    /// Handles overnight shifts (end &lt; start) by wrapping around midnight.
    /// </summary>
    private static bool IsTimeInShift(TimeOnly start, TimeOnly end, TimeOnly time)
    {
        if (end >= start)
            return time >= start && time < end;

        // Overnight: start=22:00 end=04:00 → active if time>=22 OR time<04
        return time >= start || time < end;
    }
}