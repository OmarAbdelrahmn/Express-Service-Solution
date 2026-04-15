using Application.Contracts.Petrol;
using Application.Service.Petrol;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Petrol;
using Domain.Models.Petrol;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Handles the Excel petrol upload flow:
///   1. Parse the Excel file (PlateNumberE + Cost columns).
///   2. Resolve each plate number to a Vehicle record.
///   3. Persist VehiclePetrolCost rows.
///   4. Trigger the attribution engine to write RiderPetrolCost rows.
/// </summary>
public class PetrolUploadService(
    IApplicationDbContext db,
    PetrolCostAttributionService attribution)
{
    /// <summary>
    /// Process an uploaded Excel stream for the given report date.
    /// </summary>
    /// <param name="excelStream">Raw stream from the multipart form or file upload.</param>
    /// <param name="reportDate">
    ///     The operational date this report covers (supplied as a querystring parameter).
    ///     Usually yesterday, but the caller decides.
    /// </param>
    /// <param name="uploadedBy">Username / identity of the uploader.</param>
    public async Task<PetrolUploadResult> ProcessUploadAsync(
        Stream excelStream,
        DateOnly reportDate,
        string uploadedBy,
        CancellationToken ct = default)
    {
        var rows = ParseExcel(excelStream);

        // Load all vehicles once — avoid N+1 queries per plate
        var allVehicles = await db.Vehicles
            .AsNoTracking()
            .ToDictionaryAsync(v => v.PlateNumberE.Trim().ToUpperInvariant(), ct);

        var newCostRecords = new List<VehiclePetrolCost>();
        var rowDetails = new List<PetrolUploadRowDetail>();

        foreach (var row in rows)
        {
            var normalised = row.PlateNumberE.Trim().ToUpperInvariant();
            allVehicles.TryGetValue(normalised, out var vehicle);

            var record = new VehiclePetrolCost
            {
                PlateNumberE = row.PlateNumberE,
                VehicleNumber = vehicle?.VehicleNumber,
                Cost = row.Cost,
                Date = reportDate,
                UploadedAt = DateTime.UtcNow.AddHours(3),
                UploadedBy = uploadedBy,
                IsAttributed = false,
                HasResolutionError = vehicle is null,
                ResolutionErrorMessage = vehicle is null
                    ? $"No vehicle found with English plate '{row.PlateNumberE}'."
                    : null
            };

            newCostRecords.Add(record);
        }

        db.VehiclePetrolCosts.AddRange(newCostRecords);
        await db.SaveChangesAsync(ct);

        // Run attribution for every record that was successfully resolved
        int attributed = 0;
        int unattributed = 0;

        foreach (var record in newCostRecords.Where(r => !r.HasResolutionError))
        {
            await attribution.AttributeSingleAsync(record, ct);

            var hasRider = await db.RiderPetrolCosts
                .AnyAsync(r => r.VehiclePetrolCostId == record.Id
                            && r.RiderIqamaNo != null, ct);

            if (hasRider) attributed++; else unattributed++;

            rowDetails.Add(new PetrolUploadRowDetail(
                PlateNumberE: record.PlateNumberE,
                ResolvedVehicleNumber: record.VehicleNumber,
                Cost: record.Cost,
                VehicleResolved: true,
                AttributedRiderCount: await db.RiderPetrolCosts
                                            .CountAsync(r => r.VehiclePetrolCostId == record.Id
                                                          && r.RiderIqamaNo != null, ct),
                ErrorMessage: null));
        }

        foreach (var record in newCostRecords.Where(r => r.HasResolutionError))
        {
            rowDetails.Add(new PetrolUploadRowDetail(
                PlateNumberE: record.PlateNumberE,
                ResolvedVehicleNumber: null,
                Cost: record.Cost,
                VehicleResolved: false,
                AttributedRiderCount: 0,
                ErrorMessage: record.ResolutionErrorMessage));
        }

        await db.SaveChangesAsync(ct);

        return new PetrolUploadResult(
            ReportDate: reportDate,
            TotalRows: rows.Count,
            SuccessfullyAttributed: attributed,
            Unattributed: unattributed,
            UnresolvedVehicles: newCostRecords.Count(r => r.HasResolutionError),
            Rows: rowDetails);
    }

    // ── Excel parsing ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads the Excel file expecting two columns:
    ///   Column A — English plate number (string)
    ///   Column B — Petrol cost (decimal)
    /// Row 1 is treated as a header and skipped.
    /// </summary>
    private static List<PetrolExcelRow> ParseExcel(Stream stream)
    {
        var result = new List<PetrolExcelRow>();

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();

        foreach (var row in worksheet.RowsUsed().Skip(1)) // skip header
        {
            var plate = row.Cell(1).GetString().Trim();
            var costRaw = row.Cell(2).GetString().Trim();

            if (string.IsNullOrWhiteSpace(plate)) continue;

            if (!decimal.TryParse(costRaw, out var cost)) continue;

            result.Add(new PetrolExcelRow(plate, cost));
        }

        return result;
    }
}