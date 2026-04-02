using Application.Abstraction;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Wallet;

public class WalletService(
    ApplicationDbcontext dbcontext,
    IRiderWorkingIdHistoryService workingIdHistoryService) : IWalletService
{
    // ── Excel column names accepted for each field ───────────────────────────

    private static readonly string[] WorkingIdColumns =
        ExcelColumnConfig.WorkingIdColumns;

    private static readonly string[] AmountColumns =
    {
        "Amount", "Current Wallet", "الراتب", "Salary", "Pay", "Payment",
        "Total", "الإجمالي", "Wallet", "المحفظة"
    };

    // ── Public: Import ───────────────────────────────────────────────────────

    public async Task<Result<WalletImportResult>> ImportFromExcelAsync(
        Stream excelStream,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<WalletImportError>();
        var createdCount = 0;
        var updatedCount = 0;
        var totalRecords = 0;

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheet(1);

            // ── Map header columns ───────────────────────────────────────────
            var (mappingValid, workingIdCol, amountCol, mappingError) =
                FindColumnIndices(worksheet);

            if (!mappingValid)
                return Result.Failure<WalletImportResult>(
                    new Error("InvalidExcel", mappingError!, 400));

            var rows = worksheet.RowsUsed().Skip(1).ToList();
            totalRecords = rows.Count;
            var rowNumber = 1;

            foreach (var row in rows)
            {
                rowNumber++;
                try
                {
                    // ── Parse row ────────────────────────────────────────────
                    var (rowValid, workingId, amount, rowError) =
                        ParseRow(row, workingIdCol, amountCol, rowNumber);

                    if (!rowValid)
                    {
                        errors.Add(new WalletImportError(rowNumber, workingId ?? "N/A", rowError!));
                        continue;
                    }

                    // ── Resolve actual rider (handles substitutions + history)
                    var (riderId, mainRiderId, originalWorkingId, isSubstitution) =
                        await GetActualRiderAsync(workingId!, cancellationToken);

                    if (riderId == -1)
                    {
                        errors.Add(new WalletImportError(rowNumber, workingId!,
                            $"WorkingId '{workingId}' belongs to a deleted employee – skipped."));
                        continue;
                    }

                    if (riderId == 0)
                    {
                        errors.Add(new WalletImportError(rowNumber, workingId!,
                            $"No rider found with WorkingId '{workingId}'."));
                        continue;
                    }

                    // ── Upsert: one record per (WorkedRiderId + Date) ────────
                    var existing = await dbcontext.Wallets
                        .FirstOrDefaultAsync(
                            w => w.WorkedRiderId == riderId && w.Date == date,
                            cancellationToken);

                    if (existing is not null)
                    {
                        existing.Amount = amount!.Value;
                        existing.Date = date;
                        existing.MainRiderId = mainRiderId;
                        existing.UpdatedAt = DateTime.UtcNow.AddHours(3);
                        updatedCount++;
                    }
                    else
                    {
                        var wallet = new Domain.Entities.Wallet
                        {
                            Date = date,
                            Amount = amount!.Value,
                            WorkedRiderId = riderId,
                            MainRiderId = mainRiderId,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };
                        await dbcontext.Wallets.AddAsync(wallet, cancellationToken);
                        createdCount++;
                    }

                    await dbcontext.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(new WalletImportError(rowNumber, "N/A",
                        $"Error processing row: {ex.Message}"));
                }
            }

            return Result.Success(new WalletImportResult(
                totalRecords,
                createdCount,
                updatedCount,
                errors.Count,
                errors));
        }
        catch (Exception ex)
        {
            return Result.Failure<WalletImportResult>(
                new Error("ServerError", $"Error reading Excel file: {ex.Message}", 500));
        }
    }

    // ── Public: GetAll ───────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<WalletResponse>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallets = await dbcontext.Wallets
                .AsNoTracking()
                // Worked rider
                .Include(w => w.WorkedRider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                // Main rider (substitution slot)
                .Include(w => w.MainRider)
                    .ThenInclude(r => r!.Employee)
                .OrderByDescending(w => w.Date)
                .ToListAsync(cancellationToken);

            var responses = wallets.Select(MapToResponse);

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<WalletResponse>>(
                new Error("ServerError", $"Error retrieving wallet records: {ex.Message}", 500));
        }
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static WalletResponse MapToResponse(Domain.Entities.Wallet w)
    {
        var isSubstitution = w.MainRiderId.HasValue;

        return new WalletResponse(
            Id: w.Id,
            Date: w.Date,
            Amount: w.Amount,

            WorkedRiderId: w.WorkedRiderId,
            WorkedRiderWorkingId: w.WorkedRider?.WorkingId ?? "N/A",
            WorkedRiderNameAR: w.WorkedRider?.Employee?.NameAR ?? "Unknown",
            WorkedRiderIqamaNo: w.WorkedRider?.Employee?.IqamaNo ?? 0,
            WorkedRiderHousingName: w.WorkedRider?.Employee?.Housing?.Name,

            MainRiderId: w.MainRiderId,
            MainRiderWorkingId: w.MainRider?.WorkingId,
            MainRiderNameAR: w.MainRider?.Employee?.NameAR,
            MainRiderIqamaNo: w.MainRider?.Employee?.IqamaNo,

            IsSubstitution: isSubstitution,
            CreatedAt: w.CreatedAt,
            UpdatedAt: w.UpdatedAt
        );
    }

    // ── Substitution / history resolution (mirrors RiderShiftService) ────────
    //
    // Returns:
    //   workedRiderId   – RiderDetails.Id of who actually worked
    //                     (-1 = deleted employee sentinel, 0 = not found sentinel)
    //   mainRiderId     – RiderDetails.Id of the original slot holder (substitution only)
    //   originalWorkingId – the Excel WorkingId when it differs from the current one
    //   isSubstitution  – true when a substitution was resolved

    private async Task<(int workedRiderId, int? mainRiderId, string? originalWorkingId, bool isSubstitution)>
        GetActualRiderAsync(string workingId, CancellationToken cancellationToken)
    {
        // 1. Active substitution?
        var substitution = await dbcontext.Set<RiderShiftSubstitution>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.ActualRiderWorkingId == workingId && s.IsActive,
                cancellationToken);

        if (substitution is not null)
        {
            // ActualRiderId is the original slot holder's RiderDetails.Id (nullable on the entity).
            // If it is null, fall back to a WorkingId lookup so MainRiderId is still populated
            // whenever possible.
            int? mainRiderId = substitution.ActualRiderId;

            if (mainRiderId is null)
            {
                var originalRider = await dbcontext.RiderDetails
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);
                mainRiderId = originalRider?.Id;
            }

            return (substitution.SubstituteRiderId, mainRiderId, workingId, true);
        }

        // 2. Current rider holding this WorkingId?
        var rider = await dbcontext.RiderDetails
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

        if (rider is not null)
            return (rider.Id, null, null, false);

        // 3. Check working-id history
        var historyResult = await workingIdHistoryService
            .WhoHasWorkingId(workingId, cancellationToken);

        if (historyResult.IsSuccess && historyResult.Value.IsCurrentlyAssigned)
        {
            var currentRiderResult = await workingIdHistoryService
                .GetRiderByWorkingId(workingId, cancellationToken);

            if (currentRiderResult.IsSuccess && currentRiderResult.Value is not null)
                return (currentRiderResult.Value.Id, null, workingId, false);
        }

        // 4. Deleted employee?
        var deleted = await dbcontext.DeletedEmployees
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.WorkingId == workingId, cancellationToken);

        if (deleted is not null)
            return (-1, null, workingId, false);   // sentinel: deleted, skip

        return (0, null, null, false);             // sentinel: not found
    }

    // ── Excel helpers ────────────────────────────────────────────────────────

    private static (bool IsValid, int WorkingIdCol, int AmountCol, string? Error)
        FindColumnIndices(IXLWorksheet worksheet)
    {
        var headerRow = worksheet.FirstRowUsed();
        if (headerRow is null)
            return (false, 0, 0, "Excel file is empty or has no header row.");

        var cells = headerRow.CellsUsed().ToList();

        var workingIdCol = FindColumn(cells, WorkingIdColumns);
        var amountCol = FindColumn(cells, AmountColumns);

        var missing = new List<string>();
        if (workingIdCol == 0)
            missing.Add($"WorkingId (tried: {string.Join(", ", WorkingIdColumns)})");
        if (amountCol == 0)
            missing.Add($"Amount (tried: {string.Join(", ", AmountColumns)})");

        if (missing.Count > 0)
            return (false, 0, 0, $"Missing required columns: {string.Join(", ", missing)}");

        return (true, workingIdCol, amountCol, null);
    }

    private static int FindColumn(List<IXLCell> cells, string[] possibleNames)
    {
        foreach (var cell in cells)
        {
            var header = cell.Value.ToString().Trim();
            foreach (var name in possibleNames)
                if (header.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cell.Address.ColumnNumber;
        }
        return 0;
    }

    private static (bool IsValid, string? WorkingId, decimal? Amount, string? Error)
        ParseRow(IXLRow row, int workingIdCol, int amountCol, int rowNumber)
    {
        var workingId = row.Cell(workingIdCol).Value.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(workingId))
            return (false, null, null, "Working ID is empty.");

        var amountStr = row.Cell(amountCol).Value.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(amountStr) ||
            !decimal.TryParse(amountStr, out var amount))
            return (false, workingId, null, $"Invalid Amount '{amountStr}' (must be a number).");

        return (true, workingId, amount, null);
    }
}