using Application.Abstraction;
using Application.Service.KetaValidation;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.MonthlyValidity;

public class MonthlyValidityService(ApplicationDbcontext db) : IMonthlyValidityService
{
    private readonly ApplicationDbcontext _db = db;

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
        try
        {
            // ── 1. Load validity records (all years OR filtered year) ─────
            var validityQuery = _db.RiderMonthlyValidities.AsNoTracking();

            if (year.HasValue)
                validityQuery = validityQuery.Where(v => v.Year == year.Value);

            var validityRecords = await validityQuery.ToListAsync();

            // ── 2. Determine available years and build (year, month) ranges ─
            var today = DateTime.Now;

            var availableYears = validityRecords
                .Select(v => v.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            // For each year: start = earliest month in DB, end = today's month if current year else 12
            var yearRanges = availableYears.ToDictionary(
                y => y,
                y =>
                {
                    int start = validityRecords.Where(v => v.Year == y).Min(v => v.Month);
                    int end = y == today.Year ? today.Month : 12;
                    return (Start: start, End: end);
                });

            // ── 3. Load only riders who have validity records ─────────────
            var iqamasWithRecords = validityRecords
                .Select(v => v.EmployeeIqamaNo)
                .Distinct()
                .ToHashSet();

            var riders = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .Where(r => iqamasWithRecords.Contains(r.EmployeeIqamaNo))
                .AsNoTracking()
                .ToListAsync();

            // Build validity lookup: (iqamaNo, year, month) → record
            var validityMap = validityRecords
                .ToDictionary(v => (v.EmployeeIqamaNo, v.Year, v.Month), v => v);

            // ── 4. Build month details for every rider ────────────────────
            var riderSummaries = riders.Select(rider =>
            {
                var monthDetails = new List<MonthValidityDetail>();

                foreach (var y in availableYears)
                {
                    var (start, end) = yearRanges[y];

                    for (int m = start; m <= end; m++)
                    {
                        validityMap.TryGetValue((rider.EmployeeIqamaNo, y, m), out var validity);
                        monthDetails.Add(BuildMonthDetail(y, m, validity));
                    }
                }

                return new RiderValiditySummary(
                    IqamaNo: rider.EmployeeIqamaNo,
                    NameAR: rider.Employee.NameAR,
                    NameEN: rider.Employee.NameEN,
                    WorkingId: rider.WorkingId,
                    CompanyName: rider.Company?.Name,
                    Months: monthDetails
                );
            }).ToList();

            // ── 5. Aggregate counters ─────────────────────────────────────
            int totalValid = validityRecords.Count(v => v.Status == ValidityStatus.Valid);
            int totalInvalid = validityRecords.Count(v => v.Status == ValidityStatus.Invalid);
            int totalFreelancer = validityRecords.Count(v => v.Status == ValidityStatus.Freelancer);
            int unclassified = riders.Count(r =>
                !validityRecords.Any(v => v.EmployeeIqamaNo == r.EmployeeIqamaNo));

            return Result.Success(new AllRidersValidityResponse(
                TotalRiders: riders.Count,
                TotalValidRecords: totalValid,
                TotalInvalidRecords: totalInvalid,
                TotalFreelancerRecords: totalFreelancer,
                TotalUnclassifiedRiders: unclassified,
                AvailableYears: availableYears,
                Riders: riderSummaries,
                RetrievedAt: DateTime.UtcNow.AddHours(3)
            ));
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
        try
        {
            // ── 1. Find rider ─────────────────────────────────────────────
            var rider = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo);

            if (rider == null)
            {
                var empExists = await _db.Employees.AnyAsync(e => e.IqamaNo == iqamaNo);
                return Result.Failure<RiderValidityResponse>(
                    new Error(
                        empExists ? "NoRiderDetails" : "NotFound",
                        empExists
                            ? $"Employee {iqamaNo} found but has no RiderDetails record"
                            : $"No employee found with IqamaNo {iqamaNo}",
                        404));
            }

            // ── 2. Load validity records (all years OR filtered year) ─────
            var validityQuery = _db.RiderMonthlyValidities
                .Where(v => v.EmployeeIqamaNo == iqamaNo)
                .AsNoTracking();

            if (year.HasValue)
                validityQuery = validityQuery.Where(v => v.Year == year.Value);

            var validityRecords = await validityQuery.ToListAsync();

            // ── 3. Determine available years and month ranges ─────────────
            var today = DateTime.Now;

            var availableYears = validityRecords
                .Select(v => v.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            var yearRanges = availableYears.ToDictionary(
                y => y,
                y =>
                {
                    int start = validityRecords.Where(v => v.Year == y).Min(v => v.Month);
                    int end = y == today.Year ? today.Month : 12;
                    return (Start: start, End: end);
                });

            var validityMap = validityRecords
                .ToDictionary(v => (v.Year, v.Month), v => v);

            // ── 4. Build month details ────────────────────────────────────
            var monthDetails = new List<MonthValidityDetail>();

            foreach (var y in availableYears)
            {
                var (start, end) = yearRanges[y];
                for (int m = start; m <= end; m++)
                {
                    validityMap.TryGetValue((y, m), out var validity);
                    monthDetails.Add(BuildMonthDetail(y, m, validity));
                }
            }

            return Result.Success(new RiderValidityResponse(
                IqamaNo: iqamaNo,
                NameAR: rider.Employee.NameAR,
                NameEN: rider.Employee.NameEN,
                WorkingId: rider.WorkingId,
                CompanyName: rider.Company?.Name,
                AvailableYears: availableYears,
                Months: monthDetails,
                RetrievedAt: DateTime.UtcNow.AddHours(3)
            ));
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
        int year, int month, RiderMonthlyValidity? validity)
    {
        string statusLabel = validity?.Status switch
        {
            ValidityStatus.Valid => "صالح",
            ValidityStatus.Invalid => "غير صالح",
            ValidityStatus.Freelancer => "فري لانسر",
            _ => "غير مصنف"
        };

        return new MonthValidityDetail(
            Year: year,
            Month: month,
            MonthName: MonthNames.GetValueOrDefault(month, month.ToString()),
            Status: validity?.Status,
            StatusLabel: statusLabel,
            RecordedOrders: validity?.TotalOrders ?? 0
        );
    }
}