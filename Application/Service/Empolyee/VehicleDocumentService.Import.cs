using Application.Abstraction;
using Application.Contracts.Employees;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static Application.Service.Empolyee.VehicleDocumentExpirySheetParser;

namespace Application.Service.Empolyee;

public sealed partial class VehicleDocumentService
{
    public async Task<Result<VehicleDocumentSheetImportReport>> ImportExpiryDatesAsync(
        Stream input, bool dryRun = false, string? worksheetName = null, CancellationToken ct = default)
    {
        var parsed = Parse(input, worksheetName, ct);
        if (parsed.IsFailure) return Result.Failure<VehicleDocumentSheetImportReport>(parsed.Error);

        var sheet = parsed.Value;
        var serials = sheet.Rows.Where(r => r.Errors.Count == 0 && (r.License is not null || r.Inspection is not null))
            .Select(r => r.Serial!.Value).Distinct().ToArray();
        var vehicles = new List<Vehicle>();
        // Keep parameter counts below SQL Server's limit, including larger uploads.
        foreach (var batch in serials.Chunk(1000))
            vehicles.AddRange(await db.Vehicles.Where(v => batch.Contains(v.SerialNumber)).ToListAsync(ct));
        var bySerial = vehicles.GroupBy(v => v.SerialNumber).ToDictionary(g => g.Key, g => g.ToList());
        var results = new List<VehicleDocumentSheetRowResult>();

        foreach (var row in sheet.Rows)
        {
            ct.ThrowIfCancellationRequested();
            if (row.Errors.Count > 0)
            {
                results.Add(RowResult(row, VehicleDocumentSheetRowStatus.Invalid, errors: row.Errors));
                continue;
            }
            if (row.License is null && row.Inspection is null)
            {
                results.Add(RowResult(row, VehicleDocumentSheetRowStatus.Skipped));
                continue;
            }
            if (!bySerial.TryGetValue(row.Serial!.Value, out var matches))
            {
                results.Add(RowResult(row, VehicleDocumentSheetRowStatus.NotFound, errors: ["No existing vehicle matches this serial number."]));
                continue;
            }
            if (matches.Count != 1)
            {
                results.Add(RowResult(row, VehicleDocumentSheetRowStatus.Ambiguous, errors: ["Multiple vehicles share this serial number; none were changed."]));
                continue;
            }

            var vehicle = matches[0];
            var changes = new List<VehicleDocumentDateChange>();
            AddChange(changes, "licenseExpiryDate", vehicle.LicenseExpiryDate, row.License);
            AddChange(changes, "inspectionExpiryDate", vehicle.InspectionExpiryDate, row.Inspection);
            if (changes.Count == 0)
                results.Add(RowResult(row, VehicleDocumentSheetRowStatus.Unchanged, vehicle));
            else
            {
                if (!dryRun)
                {
                    // Mark only supplied date fields as changed; preserve insurance and all other data.
                    if (row.License is not null) vehicle.LicenseExpiryDate = row.License.Value;
                    if (row.Inspection is not null) vehicle.InspectionExpiryDate = row.Inspection.Value;
                }
                results.Add(RowResult(row, dryRun ? VehicleDocumentSheetRowStatus.WouldUpdate : VehicleDocumentSheetRowStatus.Updated,
                    vehicle, changes));
            }
        }

        // One audited commit for all accepted rows. Row-level rejections are reported above.
        if (!dryRun && results.Any(r => r.Status == VehicleDocumentSheetRowStatus.Updated))
            await db.SaveChangesAsync(ct);

        int Count(VehicleDocumentSheetRowStatus status) => results.Count(r => r.Status == status);
        return Result.Success(new VehicleDocumentSheetImportReport(sheet.Name, dryRun, results.Count,
            Count(VehicleDocumentSheetRowStatus.Updated), Count(VehicleDocumentSheetRowStatus.WouldUpdate),
            Count(VehicleDocumentSheetRowStatus.Unchanged), Count(VehicleDocumentSheetRowStatus.Skipped),
            Count(VehicleDocumentSheetRowStatus.NotFound), Count(VehicleDocumentSheetRowStatus.Invalid),
            Count(VehicleDocumentSheetRowStatus.Ambiguous), results));
    }

    private static void AddChange(List<VehicleDocumentDateChange> changes, string field, DateOnly? previous, ParsedDate? incoming)
    {
        if (incoming is not null && incoming.Value != previous)
            changes.Add(new VehicleDocumentDateChange(field, previous, incoming.Value, incoming.SourceValue, incoming.Calendar));
    }

    private static VehicleDocumentSheetRowResult RowResult(
        ParsedRow row, VehicleDocumentSheetRowStatus status, Vehicle? vehicle = null,
        IReadOnlyList<VehicleDocumentDateChange>? changes = null, IReadOnlyList<string>? errors = null)
        => new(row.RowNumber, row.Serial, vehicle?.VehicleNumber, vehicle?.PlateNumberA, status, changes ?? [], errors ?? []);
}
