using Application.Abstraction;
using Application.Contracts.Employees;
using Domain;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Empolyee;

public sealed class VehicleDocumentService(ApplicationDbcontext db, TimeProvider clock) : IVehicleDocumentService
{
    public Task<Result<VehicleDocumentReport>> GetExpiringAsync(VehicleDocumentQuery query, CancellationToken ct = default)
        => GetReportAsync(query, ReportKind.Expiring, ct);

    public Task<Result<VehicleDocumentReport>> GetMissingAsync(VehicleDocumentQuery query, CancellationToken ct = default)
        => GetReportAsync(query, ReportKind.Missing, ct);

    public Task<Result<VehicleDocumentReport>> GetRemindersAsync(VehicleDocumentQuery query, CancellationToken ct = default)
        => GetReportAsync(query, ReportKind.Reminders, ct);

    public async Task<Result<VehicleResponse>> UpdateAsync(
        string vehicleNumber, UpdateVehicleDocumentsRequest request, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.VehicleNumber == vehicleNumber, ct);
        if (vehicle is null)
            return Result.Failure<VehicleResponse>(new Error("Vehicle.NotFound", "Vehicle was not found.", 404));

        vehicle.LicenseExpiryDate = Normalize(request.LicenseExpiryDate);
        vehicle.InsuranceExpiryDate = Normalize(request.InsuranceExpiryDate);
        vehicle.InspectionExpiryDate = Normalize(request.InspectionExpiryDate);
        await db.SaveChangesAsync(ct);
        return Result.Success(vehicle.Adapt<VehicleResponse>());
    }

    private async Task<Result<VehicleDocumentReport>> GetReportAsync(
        VehicleDocumentQuery query, ReportKind kind, CancellationToken ct)
    {
        if (query.DaysBeforeExpiry is < 0 or > 3650 || query.Page < 1 || query.PageSize is < 1 or > 200 ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue ||
            (query.DocumentType.HasValue && !Enum.IsDefined(query.DocumentType.Value)))
            return Result.Failure<VehicleDocumentReport>(new Error(
                "VehicleDocuments.InvalidQuery", "Use a valid document type, days 0–3650, page >= 1 and page size 1–200.", 400));

        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3)).DateTime);
        var cutoff = today.AddDays(query.DaysBeforeExpiry);
        var documents = BuildDocumentsQuery(query.DocumentType);
        documents = kind switch
        {
            ReportKind.Missing => documents.Where(d => d.ExpiryDate == null),
            ReportKind.Expiring => documents.Where(d => d.ExpiryDate >= today && d.ExpiryDate <= cutoff),
            _ => documents.Where(d => d.ExpiryDate == null || d.ExpiryDate <= cutoff)
        };

        // Counts cover the entire filtered result, before pagination.
        var counts = await documents.GroupBy(d => 1).Select(g => new
        {
            Total = g.Count(),
            Missing = g.Count(d => d.ExpiryDate == null),
            Expired = g.Count(d => d.ExpiryDate < today),
            DueToday = g.Count(d => d.ExpiryDate == today),
            Upcoming = g.Count(d => d.ExpiryDate > today)
        }).SingleOrDefaultAsync(ct);

        var rows = await documents.OrderBy(d => d.ExpiryDate)
            .ThenBy(d => d.VehicleNumber).ThenBy(d => d.DocumentType)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);

        var items = rows.Select(d =>
        {
            int? days = d.ExpiryDate?.DayNumber - today.DayNumber;
            var status = days switch
            {
                null => VehicleDocumentStatus.Missing,
                < 0 => VehicleDocumentStatus.Expired,
                0 => VehicleDocumentStatus.DueToday,
                _ => VehicleDocumentStatus.Upcoming
            };
            var name = d.DocumentType switch
            {
                VehicleDocumentType.Registration => "استمارة المركبة",
                VehicleDocumentType.Insurance => "تأمين المركبة",
                _ => "الفحص الدوري للمركبة"
            };
            var message = status switch
            {
                VehicleDocumentStatus.Missing => $"تاريخ انتهاء {name} غير مسجل للمركبة {d.PlateNumberA}.",
                VehicleDocumentStatus.Expired => $"انتهى {name} للمركبة {d.PlateNumberA} منذ {-days} يوم.",
                VehicleDocumentStatus.DueToday => $"ينتهي {name} للمركبة {d.PlateNumberA} اليوم.",
                _ => $"ينتهي {name} للمركبة {d.PlateNumberA} خلال {days} يوم."
            };
            return new VehicleDocumentReminder(d.VehicleNumber, d.PlateNumberA, d.Location,
                d.DocumentType, d.ExpiryDate, days, status, message);
        }).ToList();

        return Result.Success(new VehicleDocumentReport(today, query.DaysBeforeExpiry,
            query.Page, query.PageSize, counts?.Total ?? 0, counts?.Missing ?? 0,
            counts?.Expired ?? 0, counts?.DueToday ?? 0, counts?.Upcoming ?? 0, items));
    }

    // Keep the same projection shape for SQL UNION ALL and apply filtering/paging in the database.
    internal IQueryable<DocumentRow> BuildDocumentsQuery(VehicleDocumentType? type)
    {
        var vehicles = db.Vehicles.AsNoTracking();
        var registration = vehicles.Select(v => new DocumentRow
        {
            VehicleNumber = v.VehicleNumber, PlateNumberA = v.PlateNumberA, Location = v.Location,
            DocumentType = VehicleDocumentType.Registration,
            ExpiryDate = v.LicenseExpiryDate == DateOnly.MinValue ? null : v.LicenseExpiryDate
        });
        var insurance = vehicles.Select(v => new DocumentRow
        {
            VehicleNumber = v.VehicleNumber, PlateNumberA = v.PlateNumberA, Location = v.Location,
            DocumentType = VehicleDocumentType.Insurance,
            ExpiryDate = v.InsuranceExpiryDate == DateOnly.MinValue ? null : v.InsuranceExpiryDate
        });
        var inspection = vehicles.Select(v => new DocumentRow
        {
            VehicleNumber = v.VehicleNumber, PlateNumberA = v.PlateNumberA, Location = v.Location,
            DocumentType = VehicleDocumentType.Inspection,
            ExpiryDate = v.InspectionExpiryDate == DateOnly.MinValue ? null : v.InspectionExpiryDate
        });
        return type switch
        {
            VehicleDocumentType.Registration => registration,
            VehicleDocumentType.Insurance => insurance,
            VehicleDocumentType.Inspection => inspection,
            _ => registration.Concat(insurance).Concat(inspection)
        };
    }

    private static DateOnly? Normalize(DateOnly? value) => value == DateOnly.MinValue ? null : value;
    private enum ReportKind { Expiring, Missing, Reminders }

    internal sealed class DocumentRow
    {
        public string VehicleNumber { get; set; } = "";
        public string PlateNumberA { get; set; } = "";
        public string Location { get; set; } = "";
        public VehicleDocumentType DocumentType { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }
}
