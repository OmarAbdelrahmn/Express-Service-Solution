using Application.Abstraction;
using Application.Service.KetaValidation;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.MonthlyValidity;

public class MonthlyValidityService(ApplicationDbcontext db) : IMonthlyValidityService
{
    private readonly ApplicationDbcontext _db = db;

    // Arabic month names (Gregorian)
    private static readonly Dictionary<int, string> MonthNames = new()
    {
        { 1,  "يناير"  }, { 2,  "فبراير" }, { 3,  "مارس"   },
        { 4,  "أبريل"  }, { 5,  "مايو"   }, { 6,  "يونيو"  },
        { 7,  "يوليو"  }, { 8,  "أغسطس"  }, { 9,  "سبتمبر" },
        { 10, "أكتوبر" }, { 11, "نوفمبر" }, { 12, "ديسمبر" }
    };

    // ─────────────────────────────────────────────────────────────
    //  GET ALL
    // ─────────────────────────────────────────────────────────────

    public async Task<Result<AllRidersValidityResponse>> GetAllRidersValidityAsync(
        int? year = null)
    {
        int targetYear = year ?? 2025;

        try
        {
            // ── 1. Load all validity records for the year ────────────────
            var validityRecords = await _db.RiderMonthlyValidities
                .Where(v => v.Year == targetYear)
                .AsNoTracking()
                .ToListAsync();

            // ── 2. Load all riders with employee info ────────────────────
            var riders = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .AsNoTracking()
                .ToListAsync();

            // ── 3. Load shift orders for the year (group by rider + month) ─
            var shiftOrders = await _db.RiderShifts
                .Where(s => s.ShiftDate.Year == targetYear)
                .GroupBy(s => new { s.RiderId, s.ShiftDate.Month })
                .Select(g => new
                {
                    g.Key.RiderId,
                    g.Key.Month,
                    TotalOrders = g.Sum(s => s.AcceptedDailyOrders)
                })
                .AsNoTracking()
                .ToListAsync();

            // Build fast lookup: (riderId, month) → orders
            var shiftOrdersMap = shiftOrders
                .ToDictionary(x => (x.RiderId, x.Month), x => x.TotalOrders);

            // Build validity lookup: (iqamaNo, month) → record
            var validityMap = validityRecords
                .ToDictionary(v => (v.EmployeeIqamaNo, v.Month), v => v);

            // ── 4. Build response per rider ───────────────────────────────
            var riderSummaries = new List<RiderValiditySummary>();

            foreach (var rider in riders)
            {
                var monthDetails = new List<MonthValidityDetail>();

                // Collect all months that have either a validity record or shift data
                var monthsForRider = validityRecords
                    .Where(v => v.EmployeeIqamaNo == rider.EmployeeIqamaNo)
                    .Select(v => v.Month)
                    .ToHashSet();

                // Also include months with shift orders even if no validity record
                foreach (var entry in shiftOrders.Where(s => s.RiderId == rider.Id))
                    monthsForRider.Add(entry.Month);

                foreach (var month in monthsForRider.OrderBy(m => m))
                {
                    validityMap.TryGetValue((rider.EmployeeIqamaNo, month), out var validity);
                    shiftOrdersMap.TryGetValue((rider.Id, month), out int actualOrders);

                    monthDetails.Add(BuildMonthDetail(
                        targetYear, month, validity, actualOrders));
                }

                riderSummaries.Add(new RiderValiditySummary(
                    IqamaNo: rider.EmployeeIqamaNo,
                    NameAR: rider.Employee.NameAR,
                    NameEN: rider.Employee.NameEN,
                    WorkingId: rider.WorkingId,
                    CompanyName: rider.Company?.Name,
                    Months: monthDetails
                ));
            }

            // ── 5. Aggregate counters ─────────────────────────────────────
            int totalValid = validityRecords.Count(v => v.Status == ValidityStatus.Valid);
            int totalInvalid = validityRecords.Count(v => v.Status == ValidityStatus.Invalid);
            int totalFreelancer = validityRecords.Count(v => v.Status == ValidityStatus.Freelancer);
            int unclassified = riders.Count(r =>
                !validityRecords.Any(v => v.EmployeeIqamaNo == r.EmployeeIqamaNo));

            var response = new AllRidersValidityResponse(
                TotalRiders: riders.Count,
                TotalValidRecords: totalValid,
                TotalInvalidRecords: totalInvalid,
                TotalFreelancerRecords: totalFreelancer,
                TotalUnclassifiedRiders: unclassified,
                Riders: riderSummaries,
                RetrievedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<AllRidersValidityResponse>(
                new Error("RetrievalError",
                    $"Failed to retrieve validity data: {ex.Message}", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET BY IQAMA
    // ─────────────────────────────────────────────────────────────

    public async Task<Result<RiderValidityResponse>> GetRiderValidityByIqamaAsync(
        long iqamaNo,
        int? year = null)
    {
        int targetYear = year ?? 2025;

        try
        {
            // ── 1. Find rider + employee info ────────────────────────────
            var rider = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo);

            if (rider == null)
            {
                // Employee might exist but not be a rider
                var empExists = await _db.Employees
                    .AnyAsync(e => e.IqamaNo == iqamaNo);

                return Result.Failure<RiderValidityResponse>(
                    new Error(
                        empExists ? "NoRiderDetails" : "NotFound",
                        empExists
                            ? $"Employee {iqamaNo} found but has no RiderDetails record"
                            : $"No employee found with IqamaNo {iqamaNo}",
                        404));
            }

            // ── 2. Load validity records for this rider + year ───────────
            var validityRecords = await _db.RiderMonthlyValidities
                .Where(v => v.EmployeeIqamaNo == iqamaNo && v.Year == targetYear)
                .AsNoTracking()
                .ToListAsync();

            // ── 3. Load shift orders per month ───────────────────────────
            var shiftOrders = await _db.RiderShifts
                .Where(s => s.RiderId == rider.Id && s.ShiftDate.Year == targetYear)
                .GroupBy(s => s.ShiftDate.Month)
                .Select(g => new
                {
                    Month = g.Key,
                    TotalOrders = g.Sum(s => s.AcceptedDailyOrders)
                })
                .AsNoTracking()
                .ToListAsync();

            var shiftOrdersMap = shiftOrders.ToDictionary(x => x.Month, x => x.TotalOrders);
            var validityMap = validityRecords.ToDictionary(v => v.Month, v => v);

            // Union of months with data
            var months = validityRecords.Select(v => v.Month)
                .Union(shiftOrders.Select(s => s.Month))
                .OrderBy(m => m);

            var monthDetails = months.Select(month =>
            {
                validityMap.TryGetValue(month, out var validity);
                shiftOrdersMap.TryGetValue(month, out int actualOrders);
                return BuildMonthDetail(targetYear, month, validity, actualOrders);
            }).ToList();

            var response = new RiderValidityResponse(
                IqamaNo: iqamaNo,
                NameAR: rider.Employee.NameAR,
                NameEN: rider.Employee.NameEN,
                WorkingId: rider.WorkingId,
                CompanyName: rider.Company?.Name,
                Months: monthDetails,
                RetrievedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderValidityResponse>(
                new Error("RetrievalError",
                    $"Failed to retrieve validity data: {ex.Message}", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    private static MonthValidityDetail BuildMonthDetail(
        int year,
        int month,
        RiderMonthlyValidity? validity,
        int actualShiftOrders)
    {
        string statusLabel = validity?.Status switch
        {
            ValidityStatus.Valid => "صالح",
            ValidityStatus.Invalid => "غير صالح",
            ValidityStatus.Freelancer => "فري لانسر",
            _ => "غير مصنف"
        };

        int recordedOrders = validity?.TotalOrders ?? 0;

        return new MonthValidityDetail(
            Year: year,
            Month: month,
            MonthName: MonthNames.GetValueOrDefault(month, month.ToString()),
            Status: validity?.Status,
            StatusLabel: statusLabel,
            RecordedOrders: recordedOrders,
            ActualShiftOrders: actualShiftOrders,
            OrdersMismatch: recordedOrders != actualShiftOrders
        );
    }
}