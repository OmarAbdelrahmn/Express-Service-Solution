using System.Globalization;
using System.Text.Json;
using Application.Abstraction;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Accounting;

internal static class AccountingErrors
{
    public static Error NotFound(string name) => new("Accounting.NotFound", $"{name} was not found.", 404);
    public static Error Invalid(string message) => new("Accounting.Invalid", message, 400);
    public static Error ClosedPeriod(int year, int month) => new("Accounting.ClosedPeriod", $"Accounting period {year}-{month:00} is closed.", 409);
}

internal static class AccountingClock
{
    public static DateTime Now => DateTime.UtcNow.AddHours(3);
}

internal static class AccountingAccountIds
{
    public const int CashAndBank = 1;
    public const int CompanyReceivables = 2;
    public const int SupplierPayables = 7;
    public const int RiderPayables = 8;
    public const int CompanyRevenue = 9;
    public const int RiderSalaryExpense = 10;
}

public abstract class AccountingServiceBase(ApplicationDbcontext db)
{
    protected readonly ApplicationDbcontext Db = db;

    protected static DateOnly PeriodStart(int year, int month) => new(year, month, 1);

    protected static DateOnly PeriodEnd(int year, int month) => PeriodStart(year, month).AddMonths(1).AddDays(-1);

    protected async Task<Result> EnsureOpenPeriodAsync(int year, int month, CancellationToken cancellationToken)
    {
        var period = await Db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Year == year && p.Month == month, cancellationToken);

        if (period is { Status: AccountingPeriodStatus.Closed or AccountingPeriodStatus.Locked })
            return Result.Failure(AccountingErrors.ClosedPeriod(year, month));

        if (period is null)
        {
            Db.AccountingPeriods.Add(new AccountingPeriod
            {
                Year = year,
                Month = month,
                StartDate = PeriodStart(year, month),
                EndDate = PeriodEnd(year, month),
                Status = AccountingPeriodStatus.Open
            });
        }

        return Result.Success();
    }

    protected static int ExpenseAccountId(int categoryId) => categoryId switch
    {
        1 => 14,
        2 => 15,
        3 => 11,
        4 => 12,
        5 => 13,
        6 or 7 or 8 => 16,
        _ => 17
    };

    protected async Task<Result<JournalEntry?>> AddJournalEntryAsync(
        DateOnly entryDate,
        string description,
        string sourceType,
        int sourceId,
        string userId,
        CancellationToken cancellationToken,
        params JournalEntryLine[] lines)
    {
        var debit = lines.Sum(l => l.Debit);
        var credit = lines.Sum(l => l.Credit);

        if (debit <= 0 || credit <= 0 || debit != credit)
            return Result.Failure<JournalEntry?>(AccountingErrors.Invalid("Journal entry must be balanced and greater than zero."));

        var exists = await Db.JournalEntries
            .AnyAsync(j => j.SourceType == sourceType
                && j.SourceId == sourceId
                && j.ReversedEntryId == null
                && j.Status == AccountingRecordStatus.Posted,
                cancellationToken);

        if (exists)
            return Result.Success<JournalEntry?>(null);

        var entry = new JournalEntry
        {
            EntryNumber = $"JE-{AccountingClock.Now:yyyyMMddHHmmssfff}-{sourceType}-{sourceId}",
            EntryDate = entryDate,
            Description = description,
            SourceType = sourceType,
            SourceId = sourceId,
            Status = AccountingRecordStatus.Posted,
            CreatedBy = userId,
            PostedBy = userId,
            PostedAt = AccountingClock.Now,
            Lines = lines.ToList()
        };

        Db.JournalEntries.Add(entry);
        AddAuditLog(sourceType, sourceId, "PostJournal", userId, description);
        return Result.Success<JournalEntry?>(entry);
    }

    protected async Task<Result> ReverseJournalEntriesForSourceAsync(
        string sourceType,
        int sourceId,
        DateOnly entryDate,
        string reversedBy,
        string? notes,
        CancellationToken cancellationToken)
    {
        var entries = await Db.JournalEntries
            .Include(j => j.Lines)
            .Where(j => j.SourceType == sourceType
                && j.SourceId == sourceId
                && j.ReversedEntryId == null
                && j.Status == AccountingRecordStatus.Posted)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            var alreadyReversed = await Db.JournalEntries
                .AnyAsync(j => j.ReversedEntryId == entry.Id && j.Status == AccountingRecordStatus.Posted, cancellationToken);

            if (alreadyReversed)
                continue;

            Db.JournalEntries.Add(new JournalEntry
            {
                EntryNumber = $"JE-{AccountingClock.Now:yyyyMMddHHmmssfff}-REV-{entry.Id}",
                EntryDate = entryDate,
                Description = $"Reversal: {entry.Description}",
                SourceType = $"{sourceType}:Reversal",
                SourceId = sourceId,
                ReversedEntryId = entry.Id,
                Status = AccountingRecordStatus.Posted,
                CreatedBy = reversedBy,
                PostedBy = reversedBy,
                PostedAt = AccountingClock.Now,
                Notes = notes,
                Lines = entry.Lines.Select(l => new JournalEntryLine
                {
                    AccountId = l.AccountId,
                    Debit = l.Credit,
                    Credit = l.Debit,
                    CostCenterId = l.CostCenterId,
                    CompanyId = l.CompanyId,
                    RiderId = l.RiderId,
                    EmployeeIqamaNo = l.EmployeeIqamaNo,
                    HousingId = l.HousingId,
                    VehicleNumber = l.VehicleNumber,
                    SupplierId = l.SupplierId,
                    Notes = $"Reversal of {entry.EntryNumber}"
                }).ToList()
            });
        }

        AddAuditLog(sourceType, sourceId, "ReverseJournal", reversedBy, notes);
        return Result.Success();
    }

    protected void AddAuditLog(string entityName, int? entityId, string action, string performedBy, string? notes = null)
    {
        Db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            PerformedBy = performedBy,
            PerformedAt = AccountingClock.Now,
            Notes = notes
        });
    }

    protected static string? AppendNote(string? existing, string? note)
        => string.IsNullOrWhiteSpace(note)
            ? existing
            : string.IsNullOrWhiteSpace(existing) ? note : $"{existing}{Environment.NewLine}{note}";
}

public class AccountingImportService(ApplicationDbcontext db) : AccountingServiceBase(db), IAccountingImportService
{
    public async Task<Result<CompanyBillImportResponse>> ImportCompanyBillAsync(
        ImportCompanyBillRequest request,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        if (request.File is null || request.File.Length == 0)
            return Result.Failure<CompanyBillImportResponse>(AccountingErrors.Invalid("No accounting bill file was uploaded."));

        var periodResult = await EnsureOpenPeriodAsync(request.Year, request.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CompanyBillImportResponse>(periodResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var company = request.CompanyId is null
            ? null
            : await Db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        if (request.CompanyId is not null && company is null)
            return Result.Failure<CompanyBillImportResponse>(AccountingErrors.NotFound("Company"));

        var riders = await Db.RiderDetails
            .Include(r => r.Employee)
            .ToListAsync(cancellationToken);

        var substitutions = await Db.RiderShiftSubstitutions
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);

        await using var stream = request.File.OpenReadStream();
        using var workbook = new XLWorkbook(stream);

        var template = request.TemplateType ?? DetectTemplate(request.File.FileName, workbook);
        var import = new CompanyBillImport
        {
            CompanyId = company?.Id,
            CompanyNameSnapshot = company?.Name ?? string.Empty,
            TemplateType = template,
            Year = request.Year,
            Month = request.Month,
            SourceFileName = request.File.FileName,
            UploadedBy = uploadedBy,
            Notes = request.Notes,
            Status = AccountingRecordStatus.PendingReview
        };

        Db.CompanyBillImports.Add(import);

        foreach (var worksheet in workbook.Worksheets)
        {
            ParseWorksheet(worksheet, import, riders, substitutions, request.Year, request.Month);
        }

        CalculateImportTotals(import);

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetImportAsync(import.Id, cancellationToken);
    }

    public async Task<Result<CompanyBillImportResponse>> ApproveCompanyBillImportAsync(
        int importId,
        string approvedBy,
        CancellationToken cancellationToken = default)
    {
        var import = await Db.CompanyBillImports
            .Include(i => i.RiderSummaries)
            .FirstOrDefaultAsync(i => i.Id == importId, cancellationToken);

        if (import is null)
            return Result.Failure<CompanyBillImportResponse>(AccountingErrors.NotFound("Accounting import"));

        var periodResult = await EnsureOpenPeriodAsync(import.Year, import.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CompanyBillImportResponse>(periodResult.Error);

        if (import.Status == AccountingRecordStatus.Posted)
            return await GetImportAsync(import.Id, cancellationToken);

        if (import.Status is AccountingRecordStatus.Cancelled or AccountingRecordStatus.Reversed)
            return Result.Failure<CompanyBillImportResponse>(AccountingErrors.Invalid("Only pending imports can be approved."));

        var unresolvedIssues = await Db.CompanyBillResolutionIssues
            .AnyAsync(i => i.CompanyBillImportId == import.Id && !i.IsResolved, cancellationToken);

        var unresolvedRows = import.RiderSummaries.Any(s =>
            s.PaidRiderId == null ||
            s.ResolutionStatus is ImportResolutionStatus.Pending
                or ImportResolutionStatus.NeedsAccountantReview
                or ImportResolutionStatus.Unresolved);

        if (unresolvedIssues || unresolvedRows)
            return Result.Failure<CompanyBillImportResponse>(AccountingErrors.Invalid("Import has unresolved accounting issues."));

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        CalculateImportTotals(import);
        import.Status = AccountingRecordStatus.Posted;

        var receivable = await Db.CompanyReceivables
            .FirstOrDefaultAsync(r => r.CompanyBillImportId == import.Id, cancellationToken);

        if (receivable is null)
        {
            receivable = new CompanyReceivable
            {
                CompanyId = import.CompanyId,
                CompanyBillImportId = import.Id,
                Year = import.Year,
                Month = import.Month,
                GrossAmount = import.GrossAmount,
                VatAmount = import.VatAmount,
                NetAmount = import.NetAmount,
                PendingAmount = import.NetAmount,
                Status = AccountingRecordStatus.Posted,
                Notes = $"Receivable from import {import.SourceFileName}"
            };

            Db.CompanyReceivables.Add(receivable);
        }

        var journalResult = await AddJournalEntryAsync(
            PeriodEnd(import.Year, import.Month),
            $"Company bill import {import.SourceFileName}",
            "CompanyBillImport",
            import.Id,
            approvedBy,
            cancellationToken,
            new JournalEntryLine { AccountId = AccountingAccountIds.CompanyReceivables, Debit = import.NetAmount, CompanyId = import.CompanyId },
            new JournalEntryLine { AccountId = AccountingAccountIds.CompanyRevenue, Credit = import.NetAmount - import.VatAmount, CompanyId = import.CompanyId },
            new JournalEntryLine { AccountId = AccountingAccountIds.SupplierPayables, Credit = import.VatAmount, CompanyId = import.CompanyId, Notes = "VAT payable" });

        if (journalResult.IsFailure)
            return Result.Failure<CompanyBillImportResponse>(journalResult.Error);

        AddAuditLog("CompanyBillImport", import.Id, "Approve", approvedBy, import.Notes);
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetImportAsync(import.Id, cancellationToken);
    }

    public async Task<Result<CompanyBillImportResponse>> ReverseCompanyBillImportAsync(
        int importId,
        string reversedBy,
        CancellationToken cancellationToken = default)
    {
        var import = await Db.CompanyBillImports.FirstOrDefaultAsync(i => i.Id == importId, cancellationToken);
        if (import is null)
            return Result.Failure<CompanyBillImportResponse>(AccountingErrors.NotFound("Accounting import"));

        var periodResult = await EnsureOpenPeriodAsync(import.Year, import.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CompanyBillImportResponse>(periodResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var reverseResult = await ReverseJournalEntriesForSourceAsync(
            "CompanyBillImport",
            import.Id,
            PeriodEnd(import.Year, import.Month),
            reversedBy,
            "Company bill import reversed.",
            cancellationToken);

        if (reverseResult.IsFailure)
            return Result.Failure<CompanyBillImportResponse>(reverseResult.Error);

        import.Status = AccountingRecordStatus.Reversed;

        var receivables = await Db.CompanyReceivables
            .Where(r => r.CompanyBillImportId == import.Id)
            .ToListAsync(cancellationToken);

        foreach (var receivable in receivables)
        {
            receivable.Status = AccountingRecordStatus.Reversed;
            receivable.PendingAmount = 0;
        }

        AddAuditLog("CompanyBillImport", import.Id, "Reverse", reversedBy);
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetImportAsync(import.Id, cancellationToken);
    }

    public async Task<Result<CompanyBillImportResponse>> GetImportAsync(
        int importId,
        CancellationToken cancellationToken = default)
    {
        var import = await Db.CompanyBillImports
            .Include(i => i.Sheets)
            .Include(i => i.RiderSummaries)
            .Include(i => i.TransactionLines)
            .FirstOrDefaultAsync(i => i.Id == importId, cancellationToken);

        if (import is null)
            return Result.Failure<CompanyBillImportResponse>(AccountingErrors.NotFound("Accounting import"));

        var sheetIds = import.Sheets.Select(s => s.Id).ToList();
        var rawRowCount = await Db.CompanyBillRawRows.CountAsync(r => sheetIds.Contains(r.CompanyBillSheetId), cancellationToken);
        var rawCellCount = await Db.CompanyBillRawCells
            .CountAsync(c => sheetIds.Contains(c.Row.CompanyBillSheetId), cancellationToken);
        var dailyMetricCount = await Db.CompanyBillDailyMetrics
            .CountAsync(m => m.CompanyBillImportId == import.Id, cancellationToken);
        var issues = await Db.CompanyBillResolutionIssues
            .Where(i => i.CompanyBillImportId == import.Id)
            .Select(i => new CompanyBillResolutionIssueResponse(
                i.Id,
                i.IssueType,
                i.Message,
                i.SourceRowNumber,
                i.SourceRiderId,
                i.IsResolved))
            .ToListAsync(cancellationToken);

        return Result.Success(new CompanyBillImportResponse(
            import.Id,
            import.CompanyId,
            import.CompanyNameSnapshot,
            import.TemplateType,
            import.Year,
            import.Month,
            import.SourceFileName,
            import.Status,
            import.GrossAmount,
            import.VatAmount,
            import.NetAmount,
            import.TotalDeductions,
            import.Sheets.Count,
            rawRowCount,
            rawCellCount,
            import.RiderSummaries.Count,
            import.TransactionLines.Count,
            dailyMetricCount,
            issues.Count,
            import.Sheets
                .OrderBy(s => s.Id)
                .Select(s => new CompanyBillSheetResponse(s.Id, s.SheetName, s.Role, s.RowCount, s.ColumnCount))
                .ToList(),
            issues));
    }

    private void ParseWorksheet(
        IXLWorksheet worksheet,
        CompanyBillImport import,
        IReadOnlyList<RiderDetails> riders,
        IReadOnlyList<RiderShiftSubstitution> substitutions,
        int year,
        int month)
    {
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
            return;

        var headerRowNumber = FindHeaderRow(worksheet);
        var firstRow = usedRange.FirstRowUsed().RowNumber();
        var lastRow = usedRange.LastRowUsed().RowNumber();
        var firstColumn = usedRange.FirstColumnUsed().ColumnNumber();
        var lastColumn = usedRange.LastColumnUsed().ColumnNumber();
        var headers = BuildHeaders(worksheet, headerRowNumber, firstColumn, lastColumn);

        var sheet = new CompanyBillSheet
        {
            SheetName = worksheet.Name,
            Role = DetectSheetRole(worksheet.Name, headers),
            RowCount = lastRow - firstRow + 1,
            ColumnCount = lastColumn - firstColumn + 1
        };

        import.Sheets.Add(sheet);

        for (var rowNumber = firstRow; rowNumber <= lastRow; rowNumber++)
        {
            var rawRow = new CompanyBillRawRow
            {
                RowNumber = rowNumber,
                IsHeader = rowNumber == headerRowNumber
            };

            sheet.RawRows.Add(rawRow);

            for (var columnNumber = firstColumn; columnNumber <= lastColumn; columnNumber++)
            {
                var header = headers.GetValueOrDefault(columnNumber);
                rawRow.Cells.Add(new CompanyBillRawCell
                {
                    ColumnNumber = columnNumber,
                    Header = header,
                    OriginalValue = ReadCell(worksheet.Cell(rowNumber, columnNumber)),
                    NormalizedField = NormalizeField(header)
                });
            }

            if (rowNumber == headerRowNumber)
                continue;

            var rowValues = BuildRowValues(worksheet, rowNumber, headers, firstColumn, lastColumn);
            if (!rowValues.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                continue;

            if (IsTotalRow(rowValues))
                continue;

            AddDailyMetricsIfAny(import, rowValues, headers, riders);

            if (LooksLikeTransaction(headers, rowValues))
                AddTransactionLine(import, sheet, rowNumber, rowValues, riders, substitutions, year, month);

            if (LooksLikeRiderSummary(headers, rowValues))
                AddRiderSummary(import, sheet, rowNumber, rowValues, riders, substitutions, year, month);
        }
    }

    private void AddRiderSummary(
        CompanyBillImport import,
        CompanyBillSheet sheet,
        int rowNumber,
        Dictionary<string, string?> rowValues,
        IReadOnlyList<RiderDetails> riders,
        IReadOnlyList<RiderShiftSubstitution> substitutions,
        int year,
        int month)
    {
        var sourceRiderId = PickString(rowValues,
            "rider id", "driver id", "working id", "transporter id", "row labels",
            "معرّف سائق التوصيل", "معرف سائق التوصيل", "كود السائق", "رقم السائق");

        if (string.IsNullOrWhiteSpace(sourceRiderId))
            return;

        var sourceName = PickString(rowValues, "name", "driver name", "اسم", "الاسم", "اسم سائق التوصيل");
        var original = FindRider(riders, sourceRiderId);
        var resolution = ResolvePaidRider(sourceRiderId, original, null, substitutions, year, month);
        var acceptedOrders = PickInt(rowValues,
            "accepted orders", "completed_orders", "completed orders", "delivered orders", "orders delivered", "grand total",
            "الطلبات المسلمة", "طلبات مكتملة", "اجمالي الطلبات", "إجمالي الطلبات");
        var rejectedOrders = PickInt(rowValues,
            "rejected orders", "rejection", "rejected", "declined", "declined orders", "order rejection", "الطلبات المرفوضة");
        var penalty = PickDecimal(rowValues,
            "penalty", "penalties", "deduction", "deductions", "declined penalty", "missed_days_penalty",
            "discount", "خصم", "غرامة", "مخالفة");
        var bonus = PickDecimal(rowValues,
            "bonus", "bonuses", "incentive", "incentive amount", "support", "subsidy", "دعم", "حافز", "مكافأة");
        var distance = PickDecimal(rowValues,
            "distance payment", "distance_payment", "distance amount", "distance", "google_distance_above_15", "المسافة");
        var basic = PickDecimal(rowValues,
            "basic payment", "basic_payment", "order pricing", "delivery service fee", "رسوم خدمة التوصيل", "base delivery");
        var riderBalance = PickDecimal(rowValues, "rider balance", "wallet", "balance", "رصيد");
        var vat = PickDecimal(rowValues, "vat", "ضريبة", "tga");
        var net = PickDecimal(rowValues,
            "net ftr", "net amount", "amount", "total", "grand total amount", "total due", "الإجمالي", "اجمالي");
        var workingHours = PickDecimal(rowValues, "working hours", "connection hours", "daily connection hours", "ساعات");
        var workingDays = PickInt(rowValues, "working days", "valid connection days", "days", "أيام");
        var validity = PickString(rowValues, "validity", "valid", "صالح");
        var validityReason = PickString(rowValues, "validity reason", "invalid reason", "reason", "سبب");

        var summary = new CompanyBillRiderSummary
        {
            CompanyBillImport = import,
            Sheet = sheet,
            SourceRowNumber = rowNumber,
            SourceRiderId = sourceRiderId.Trim(),
            SourceRiderName = sourceName,
            OriginalRiderId = original?.Id,
            PaidRiderId = resolution.PaidRiderId,
            ResolutionStatus = resolution.Status,
            ResolutionNotes = resolution.Notes,
            AcceptedOrders = acceptedOrders,
            RejectedOrders = rejectedOrders,
            DistanceAmount = distance,
            BasicPayment = basic,
            BonusAmount = bonus,
            PenaltyAmount = Math.Abs(penalty),
            RiderBalance = riderBalance,
            VatAmount = vat,
            NetAmount = net,
            WorkingHours = workingHours,
            WorkingDays = workingDays,
            ValidityStatus = validity,
            ValidityReason = validityReason,
            RawJson = JsonSerializer.Serialize(rowValues)
        };

        import.RiderSummaries.Add(summary);

        if (summary.ResolutionStatus is ImportResolutionStatus.Unresolved or ImportResolutionStatus.NeedsAccountantReview)
        {
            AddIssue(import, "RiderResolution", resolution.Notes ?? "Rider needs accountant review.", rowNumber, sourceRiderId);
        }

    }

    private void AddTransactionLine(
        CompanyBillImport import,
        CompanyBillSheet sheet,
        int rowNumber,
        Dictionary<string, string?> rowValues,
        IReadOnlyList<RiderDetails> riders,
        IReadOnlyList<RiderShiftSubstitution> substitutions,
        int year,
        int month)
    {
        var sourceRiderId = PickString(rowValues,
            "rider id", "driver id", "working id", "معرّف سائق التوصيل", "معرف سائق التوصيل", "كود السائق");

        if (string.IsNullOrWhiteSpace(sourceRiderId))
            return;

        var serviceDate = PickDate(rowValues, "date", "service date", "order date", "business date", "التاريخ");
        var original = FindRider(riders, sourceRiderId);
        var resolution = ResolvePaidRider(sourceRiderId, original, serviceDate, substitutions, year, month);
        var amount = PickDecimal(rowValues, "amount", "amount detail", "total due", "إجمالي المبلغ المستحق", "المبلغ التفصيلي");

        var line = new CompanyBillTransactionLine
        {
            CompanyBillImport = import,
            Sheet = sheet,
            SourceRowNumber = rowNumber,
            ServiceDate = serviceDate,
            SourceRiderId = sourceRiderId.Trim(),
            SourceRiderName = PickString(rowValues, "name", "driver name", "اسم", "اسم سائق التوصيل"),
            OriginalRiderId = original?.Id,
            PaidRiderId = resolution.PaidRiderId,
            ResolutionStatus = resolution.Status,
            TransactionType = PickString(rowValues, "transaction type", "نوع المعاملة"),
            WorkId = PickString(rowValues, "work id", "job id", "business id", "معرّف العمل", "معرف العمل"),
            FeeType = PickString(rowValues, "fee type", "نوع الرسوم"),
            AmountDetail = PickString(rowValues, "amount detail", "المبلغ التفصيلي"),
            Amount = amount,
            DistanceKm = PickDecimal(rowValues, "distance", "distance km", "المسافة"),
            TicketId = PickString(rowValues, "ticket id", "معرف التذكرة"),
            ViolationId = PickString(rowValues, "violation id", "معرف المخالفة"),
            ViolationType = PickString(rowValues, "violation type", "نوع المخالفة"),
            PunishmentMethod = PickString(rowValues, "punishment method", "طريقة العقوبة"),
            FaceVerificationTime = PickDateTime(rowValues, "face verification time", "وقت التحقق من الوجه"),
            FaceVerificationResult = PickString(rowValues, "face verification result", "نتيجة التحقق من الوجه"),
            Notes = PickString(rowValues, "note", "notes", "ملاحظة"),
            RawJson = JsonSerializer.Serialize(rowValues)
        };

        import.TransactionLines.Add(line);

        if (line.ResolutionStatus is ImportResolutionStatus.Unresolved or ImportResolutionStatus.NeedsAccountantReview)
            AddIssue(import, "TransactionResolution", resolution.Notes ?? "Transaction needs accountant review.", rowNumber, sourceRiderId);
    }

    private static void AddDailyMetricsIfAny(
        CompanyBillImport import,
        Dictionary<string, string?> rowValues,
        Dictionary<int, string> headers,
        IReadOnlyList<RiderDetails> riders)
    {
        var sourceRiderId = PickString(rowValues, "row labels", "rider id", "driver id", "working id", "معرّف سائق التوصيل");
        if (string.IsNullOrWhiteSpace(sourceRiderId))
            return;

        var rider = FindRider(riders, sourceRiderId);
        foreach (var header in headers.Values)
        {
            if (!TryParseDate(header, out var metricDate))
                continue;

            var value = GetValueByHeader(rowValues, header);
            var orders = TryParseInt(value);
            import.DailyMetrics.Add(new CompanyBillDailyMetric
            {
                SourceRiderId = sourceRiderId,
                RiderId = rider?.Id,
                MetricDate = metricDate,
                AcceptedOrders = orders,
                Amount = TryParseDecimal(value),
                RawValue = value
            });
        }
    }

    private static void CalculateImportTotals(CompanyBillImport import)
    {
        import.GrossAmount = import.RiderSummaries.Sum(s => s.BasicPayment + s.BonusAmount + s.DistanceAmount);
        import.VatAmount = import.RiderSummaries.Sum(s => s.VatAmount);
        import.TotalDeductions = import.RiderSummaries.Sum(s => s.PenaltyAmount);
        import.NetAmount = import.RiderSummaries.Any(s => s.NetAmount != 0)
            ? import.RiderSummaries.Sum(s => s.NetAmount)
            : import.GrossAmount + import.VatAmount - import.TotalDeductions;
    }

    private static int FindHeaderRow(IXLWorksheet worksheet)
    {
        var used = worksheet.RangeUsed();
        if (used is null)
            return 1;

        var firstRow = used.FirstRowUsed().RowNumber();
        var lastRow = Math.Min(used.LastRowUsed().RowNumber(), firstRow + 10);
        var bestRow = firstRow;
        var bestScore = 0;

        for (var row = firstRow; row <= lastRow; row++)
        {
            var score = worksheet.Row(row).CellsUsed().Count(c => !string.IsNullOrWhiteSpace(ReadCell(c)));
            if (score > bestScore)
            {
                bestScore = score;
                bestRow = row;
            }
        }

        return bestRow;
    }

    private static Dictionary<int, string> BuildHeaders(IXLWorksheet worksheet, int headerRowNumber, int firstColumn, int lastColumn)
    {
        var headers = new Dictionary<int, string>();
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var header = ReadCell(worksheet.Cell(headerRowNumber, column));
            if (string.IsNullOrWhiteSpace(header))
                header = $"Column {column}";
            headers[column] = header.Trim();
        }

        return headers;
    }

    private static Dictionary<string, string?> BuildRowValues(
        IXLWorksheet worksheet,
        int rowNumber,
        Dictionary<int, string> headers,
        int firstColumn,
        int lastColumn)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var header = headers[column];
            var key = values.ContainsKey(header) ? $"{header}_{column}" : header;
            values[key] = ReadCell(worksheet.Cell(rowNumber, column));
        }

        return values;
    }

    private static string? ReadCell(IXLCell cell)
    {
        var source = cell.IsMerged()
            ? cell.MergedRange().FirstCell()
            : cell;

        return source.GetString()?.Trim();
    }

    private static CompanyBillTemplateType DetectTemplate(string fileName, XLWorkbook workbook)
    {
        var text = NormalizeText(fileName + " " + string.Join(' ', workbook.Worksheets.Select(w => w.Name)));
        if (text.Contains("amazon") || text.Contains("anow"))
            return CompanyBillTemplateType.Amazon;
        if (text.Contains("ftr") || text.Contains("hunger") || text.Contains("khedmat"))
            return CompanyBillTemplateType.FtrHunger;
        if (text.Contains("keeta") || text.Contains("نظامالدفع") || text.Contains("الدفعالطلب"))
            return CompanyBillTemplateType.KeetaPayPerOrder;
        if (text.Contains("الشرائح") || text.Contains("segment"))
            return CompanyBillTemplateType.KeetaSegment;

        return CompanyBillTemplateType.Generic;
    }

    private static CompanyBillSheetRole DetectSheetRole(string sheetName, Dictionary<int, string> headers)
    {
        var text = NormalizeText(sheetName + " " + string.Join(' ', headers.Values));
        if (text.Contains("rowlabels") || text.Contains("workingdays"))
            return CompanyBillSheetRole.DailyOrders;
        if (text.Contains("transactiontype") || text.Contains("نوعالمعاملة") || text.Contains("violation") || text.Contains("ticket"))
            return CompanyBillSheetRole.OrderDetail;
        if (text.Contains("completedorders") || text.Contains("الطلباتالمسلمة") || text.Contains("basicpayment") || text.Contains("netftr"))
            return CompanyBillSheetRole.RiderSummary;
        if (text.Contains("workinghours") || text.Contains("misseddays") || text.Contains("inactive"))
            return CompanyBillSheetRole.CostDetail;
        if (text.Contains("partner") || text.Contains("الشركاء"))
            return CompanyBillSheetRole.PartnerSummary;

        return CompanyBillSheetRole.Unknown;
    }

    private static bool LooksLikeTransaction(Dictionary<int, string> headers, Dictionary<string, string?> rowValues)
    {
        var text = NormalizeText(string.Join(' ', headers.Values));
        return text.Contains("transactiontype")
            || text.Contains("نوعالمعاملة")
            || text.Contains("feetype")
            || text.Contains("ticket")
            || text.Contains("violation")
            || !string.IsNullOrWhiteSpace(PickString(rowValues, "transaction type", "نوع المعاملة", "fee type", "ticket id", "violation id"));
    }

    private static bool LooksLikeRiderSummary(Dictionary<int, string> headers, Dictionary<string, string?> rowValues)
    {
        var sourceRiderId = PickString(rowValues,
            "rider id", "driver id", "working id", "transporter id", "row labels",
            "معرّف سائق التوصيل", "معرف سائق التوصيل", "كود السائق");
        if (string.IsNullOrWhiteSpace(sourceRiderId))
            return false;

        var text = NormalizeText(string.Join(' ', headers.Values));
        return text.Contains("completedorders")
            || text.Contains("acceptedorders")
            || text.Contains("الطلباتالمسلمة")
            || text.Contains("grandtotal")
            || text.Contains("basicpayment")
            || text.Contains("رسومخدمةالتوصيل")
            || text.Contains("amount");
    }

    private static bool IsTotalRow(Dictionary<string, string?> rowValues)
    {
        var first = rowValues.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        var normalized = NormalizeText(first);
        return normalized is "grandtotal" or "total" or "الإجمالي" or "اجمالي";
    }

    private static string? NormalizeField(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        var text = NormalizeText(header);
        if (text.Contains("rider") || text.Contains("driver") || text.Contains("سائق"))
            return "rider";
        if (text.Contains("accepted") || text.Contains("completed") || text.Contains("delivered") || text.Contains("الطلبات"))
            return "accepted_orders";
        if (text.Contains("reject") || text.Contains("declined") || text.Contains("مرفوض"))
            return "rejected_orders";
        if (text.Contains("distance") || text.Contains("مسافة"))
            return "distance";
        if (text.Contains("penalty") || text.Contains("deduction") || text.Contains("خصم") || text.Contains("مخالفة"))
            return "deduction";
        if (text.Contains("bonus") || text.Contains("incentive") || text.Contains("حافز"))
            return "bonus";
        if (text.Contains("vat") || text.Contains("ضريبة"))
            return "vat";
        if (text.Contains("amount") || text.Contains("total") || text.Contains("اجمالي") || text.Contains("الإجمالي"))
            return "amount";

        return text;
    }

    private static RiderDetails? FindRider(IReadOnlyList<RiderDetails> riders, string sourceRiderId)
    {
        var key = sourceRiderId.Trim();
        var rider = riders.FirstOrDefault(r => string.Equals(r.WorkingId, key, StringComparison.OrdinalIgnoreCase));
        if (rider is not null)
            return rider;

        return long.TryParse(key, out var iqamaNo)
            ? riders.FirstOrDefault(r => r.EmployeeIqamaNo == iqamaNo)
            : null;
    }

    private static (int? PaidRiderId, ImportResolutionStatus Status, string? Notes) ResolvePaidRider(
        string sourceRiderId,
        RiderDetails? original,
        DateOnly? serviceDate,
        IReadOnlyList<RiderShiftSubstitution> substitutions,
        int year,
        int month)
    {
        if (original is null)
            return (null, ImportResolutionStatus.Unresolved, $"No rider found for source rider id {sourceRiderId}.");

        if (serviceDate is not null)
        {
            var day = serviceDate.Value.ToDateTime(TimeOnly.MinValue);
            var sub = substitutions.FirstOrDefault(s =>
                string.Equals(s.ActualRiderWorkingId, sourceRiderId, StringComparison.OrdinalIgnoreCase)
                && s.StartDate.Date <= day.Date
                && (s.EndDate is null || s.EndDate.Value.Date >= day.Date));

            if (sub is not null)
                return (sub.SubstituteRiderId, ImportResolutionStatus.Resolved, $"Paid to substitute rider {sub.SubstituteWorkingId}.");
        }
        else
        {
            var start = PeriodStart(year, month).ToDateTime(TimeOnly.MinValue);
            var end = PeriodEnd(year, month).ToDateTime(TimeOnly.MaxValue);
            var hasOverlap = substitutions.Any(s =>
                string.Equals(s.ActualRiderWorkingId, sourceRiderId, StringComparison.OrdinalIgnoreCase)
                && s.StartDate <= end
                && (s.EndDate is null || s.EndDate >= start));

            if (hasOverlap)
                return (original.Id, ImportResolutionStatus.NeedsAccountantReview, "A substitution overlaps this monthly row, but the source row has no service date.");
        }

        return (original.Id, ImportResolutionStatus.Resolved, null);
    }

    private static decimal CalculateSalaryAmount(
        CompanyBillTemplateType templateType,
        string companyName,
        int acceptedOrders,
        decimal net,
        decimal basic,
        decimal bonus,
        decimal penalty,
        decimal riderBalance)
    {
        var companyText = NormalizeText(companyName);
        if (templateType == CompanyBillTemplateType.FtrHunger || companyText.Contains("hunger") || companyText.Contains("ftr"))
            return acceptedOrders >= 500
                ? 2000m + (acceptedOrders - 500) * 6m
                : acceptedOrders * 3m;

        return net != 0 ? net : basic + bonus + riderBalance - Math.Abs(penalty);
    }

    private void AddIssue(CompanyBillImport import, string issueType, string message, int? sourceRowNumber, string? sourceRiderId)
    {
        Db.CompanyBillResolutionIssues.Add(new CompanyBillResolutionIssue
        {
            CompanyBillImport = import,
            IssueType = issueType,
            Message = message,
            SourceRowNumber = sourceRowNumber,
            SourceRiderId = sourceRiderId
        });
    }

    private static string? PickString(Dictionary<string, string?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetValueByHeader(values, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static int PickInt(Dictionary<string, string?> values, params string[] keys)
        => keys.Select(key => TryParseInt(GetValueByHeader(values, key))).FirstOrDefault(value => value != 0);

    private static decimal PickDecimal(Dictionary<string, string?> values, params string[] keys)
        => keys.Select(key => TryParseDecimal(GetValueByHeader(values, key))).FirstOrDefault(value => value != 0);

    private static DateOnly? PickDate(Dictionary<string, string?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetValueByHeader(values, key);
            if (TryParseDate(value, out var date))
                return date;
        }

        return null;
    }

    private static DateTime? PickDateTime(Dictionary<string, string?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetValueByHeader(values, key);
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
                return dateTime;
        }

        return null;
    }

    private static string? GetValueByHeader(Dictionary<string, string?> values, string header)
    {
        var key = NormalizeText(header);
        var pair = values.FirstOrDefault(v => NormalizeText(v.Key) == key || NormalizeText(v.Key).Contains(key));
        return pair.Value;
    }

    private static int TryParseInt(string? value)
    {
        var decimalValue = TryParseDecimal(value);
        return decimalValue == 0 ? 0 : (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
    }

    private static decimal TryParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var cleaned = value.Replace(",", string.Empty).Replace("SAR", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            || decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.CurrentCulture, out result)
            ? result
            : 0;
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out dateTime))
        {
            date = DateOnly.FromDateTime(dateTime);
            return true;
        }

        return false;
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Trim().ToLowerInvariant()
            .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != '/' && c != '\\' && c != '#' && c != '\'' && c != '"')
            .ToArray();

        return new string(chars);
    }
}

public class AccountingSalaryService(ApplicationDbcontext db) : AccountingServiceBase(db), IAccountingSalaryService
{
    public async Task<Result<List<SalaryResponse>>> GenerateMonthlySalariesAsync(
        GenerateSalaryRequest request,
        string generatedBy,
        CancellationToken cancellationToken = default)
    {
        var periodResult = await EnsureOpenPeriodAsync(request.Year, request.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<List<SalaryResponse>>(periodResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        await EnsureEarningsFromSummariesAsync(request, cancellationToken);

        var earnings = await Db.RiderEarnings
            .Where(e => e.Year == request.Year && e.Month == request.Month)
            .Where(e => request.CompanyId == null || e.CompanyId == request.CompanyId)
            .Where(e => e.Status != AccountingRecordStatus.Cancelled && e.Status != AccountingRecordStatus.Reversed)
            .ToListAsync(cancellationToken);

        var riderIds = earnings.Select(e => e.PaidRiderId).Distinct().ToList();
        var riders = await Db.RiderDetails
            .Include(r => r.Employee)
            .Where(r => riderIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        var financialItems = await Db.RiderFinancialItems
            .Include(i => i.Type)
            .Where(i => i.Year == request.Year && i.Month == request.Month)
            .Where(i => riderIds.Contains(i.RiderId))
            .Where(i => i.Status == AccountingRecordStatus.Approved || i.Status == AccountingRecordStatus.Posted)
            .Where(i => !i.IsWaived)
            .ToListAsync(cancellationToken);

        var loanInstallments = await Db.RiderLoanInstallments
            .Include(i => i.RiderLoan)
            .Where(i => i.Year == request.Year && i.Month == request.Month)
            .Where(i => riderIds.Contains(i.RiderLoan.RiderId))
            .Where(i => i.Status == AccountingRecordStatus.Approved || i.Status == AccountingRecordStatus.Posted)
            .ToListAsync(cancellationToken);

        var results = new List<SalaryResponse>();

        foreach (var riderGroup in earnings.GroupBy(e => e.PaidRiderId))
        {
            var existing = await Db.RiderMonthlySalaries
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.RiderId == riderGroup.Key && s.Year == request.Year && s.Month == request.Month, cancellationToken);

            if (existing is not null && existing.Status is SalaryStatus.Approved or SalaryStatus.PartiallyPaid or SalaryStatus.Paid or SalaryStatus.Locked)
                continue;

            if (existing is not null && !request.ReplaceDraft)
                continue;

            if (existing is not null)
            {
                Db.RiderMonthlySalaryLines.RemoveRange(existing.Lines);
                Db.RiderMonthlySalaries.Remove(existing);
                var existingAwards = await Db.RiderBonusAwards
                    .Where(a => a.RiderId == riderGroup.Key && a.Year == request.Year && a.Month == request.Month)
                    .ToListAsync(cancellationToken);
                Db.RiderBonusAwards.RemoveRange(existingAwards);
                await Db.SaveChangesAsync(cancellationToken);
            }

            var rider = riders.First(r => r.Id == riderGroup.Key);
            var salary = new RiderMonthlySalary
            {
                RiderId = rider.Id,
                Year = request.Year,
                Month = request.Month,
                PaymentMethod = string.IsNullOrWhiteSpace(rider.Employee?.IBAN)
                    ? RiderPaymentMethod.Cash
                    : RiderPaymentMethod.BankTransfer,
                Status = SalaryStatus.Draft,
                IbanSnapshot = rider.Employee?.IBAN,
                GeneratedBy = generatedBy
            };

            foreach (var earning in riderGroup)
            {
                salary.Lines.Add(new RiderMonthlySalaryLine
                {
                    Type = SalaryLineType.Earning,
                    Description = $"Company earning from {earning.SourceType}",
                    Amount = earning.SalaryAmount,
                    SourceType = earning.SourceType,
                    SourceId = earning.Id,
                    IsEditable = false,
                    Notes = earning.Notes
                });
            }

            var activeBonusRules = await GetActiveBonusRulesAsync(request.Year, request.Month, cancellationToken);
            foreach (var companyGroup in riderGroup.GroupBy(e => e.CompanyId))
            {
                var acceptedOrders = companyGroup.Sum(e => e.AcceptedOrders);
                var rule = activeBonusRules
                    .Where(r => (r.CompanyId == companyGroup.Key || r.CompanyId is null) && acceptedOrders >= r.MinimumAcceptedOrders)
                    .OrderByDescending(r => r.Priority)
                    .ThenByDescending(r => r.MinimumAcceptedOrders)
                    .FirstOrDefault();

                if (rule is null)
                    continue;

                var award = new RiderBonusAward
                {
                    RiderBonusRuleId = rule.Id,
                    RiderId = rider.Id,
                    CompanyId = companyGroup.Key,
                    Year = request.Year,
                    Month = request.Month,
                    AcceptedOrders = acceptedOrders,
                    Amount = rule.BonusAmount,
                    Notes = rule.Notes
                };

                Db.RiderBonusAwards.Add(award);
                salary.Lines.Add(new RiderMonthlySalaryLine
                {
                    Type = SalaryLineType.Bonus,
                    Description = $"Bonus for {acceptedOrders} accepted orders",
                    Amount = rule.BonusAmount,
                    SourceType = "RiderBonusRule",
                    SourceId = rule.Id,
                    IsEditable = true,
                    Notes = rule.Notes
                });
            }

            foreach (var item in financialItems.Where(i => i.RiderId == rider.Id))
            {
                salary.Lines.Add(new RiderMonthlySalaryLine
                {
                    Type = ToSalaryLineType(item.Type.Category),
                    Description = item.Type.Name,
                    Amount = Math.Abs(item.Amount),
                    SourceType = "RiderFinancialItem",
                    SourceId = item.Id,
                    IsEditable = true,
                    Notes = item.Notes
                });
            }

            foreach (var installment in loanInstallments.Where(i => i.RiderLoan.RiderId == rider.Id))
            {
                salary.Lines.Add(new RiderMonthlySalaryLine
                {
                    Type = SalaryLineType.Deduction,
                    Description = "Loan installment",
                    Amount = Math.Abs(installment.Amount - installment.PaidAmount),
                    SourceType = "RiderLoanInstallment",
                    SourceId = installment.Id,
                    IsEditable = true,
                    Notes = installment.RiderLoan.Notes
                });
            }

            RecalculateSalary(salary);
            Db.RiderMonthlySalaries.Add(salary);
        }

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var generated = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .ThenInclude(r => r.Employee)
            .Include(s => s.Lines)
            .Where(s => s.Year == request.Year && s.Month == request.Month)
            .Where(s => riderIds.Contains(s.RiderId))
            .ToListAsync(cancellationToken);

        results.AddRange(generated.Select(MapSalary));
        return Result.Success(results);
    }

    public async Task<Result<SalaryResponse>> GetSalaryAsync(int salaryId, CancellationToken cancellationToken = default)
    {
        var salary = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .ThenInclude(r => r.Employee)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == salaryId, cancellationToken);

        return salary is null
            ? Result.Failure<SalaryResponse>(AccountingErrors.NotFound("Salary"))
            : Result.Success(MapSalary(salary));
    }

    public async Task<Result<SalaryResponse>> ApproveSalaryAsync(int salaryId, string approvedBy, CancellationToken cancellationToken = default)
    {
        var salary = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .ThenInclude(r => r.Employee)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == salaryId, cancellationToken);

        if (salary is null)
            return Result.Failure<SalaryResponse>(AccountingErrors.NotFound("Salary"));

        var periodResult = await EnsureOpenPeriodAsync(salary.Year, salary.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<SalaryResponse>(periodResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        salary.Status = SalaryStatus.Approved;
        salary.ApprovedBy = approvedBy;
        salary.ApprovedAt = AccountingClock.Now;

        var journalResult = await AddJournalEntryAsync(
            PeriodEnd(salary.Year, salary.Month),
            $"Rider salary {salary.Rider.WorkingId} {salary.Year}-{salary.Month:00}",
            "RiderMonthlySalary",
            salary.Id,
            approvedBy,
            cancellationToken,
            new JournalEntryLine
            {
                AccountId = AccountingAccountIds.RiderSalaryExpense,
                Debit = salary.NetSalary,
                RiderId = salary.RiderId,
                EmployeeIqamaNo = salary.Rider.EmployeeIqamaNo,
                CompanyId = salary.Rider.CompanyId
            },
            new JournalEntryLine
            {
                AccountId = AccountingAccountIds.RiderPayables,
                Credit = salary.NetSalary,
                RiderId = salary.RiderId,
                EmployeeIqamaNo = salary.Rider.EmployeeIqamaNo,
                CompanyId = salary.Rider.CompanyId
            });

        if (journalResult.IsFailure)
            return Result.Failure<SalaryResponse>(journalResult.Error);

        AddAuditLog("RiderMonthlySalary", salary.Id, "Approve", approvedBy);
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(MapSalary(salary));
    }

    public async Task<Result<SalaryResponse>> ReverseSalaryAsync(int salaryId, string reversedBy, CancellationToken cancellationToken = default)
    {
        var salary = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .ThenInclude(r => r.Employee)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == salaryId, cancellationToken);

        if (salary is null)
            return Result.Failure<SalaryResponse>(AccountingErrors.NotFound("Salary"));

        var periodResult = await EnsureOpenPeriodAsync(salary.Year, salary.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<SalaryResponse>(periodResult.Error);

        if (salary.PaidAmount > 0)
            return Result.Failure<SalaryResponse>(AccountingErrors.Invalid("Paid salaries cannot be reversed before payment reversal."));

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var reverseResult = await ReverseJournalEntriesForSourceAsync(
            "RiderMonthlySalary",
            salary.Id,
            PeriodEnd(salary.Year, salary.Month),
            reversedBy,
            "Rider salary reversed.",
            cancellationToken);

        if (reverseResult.IsFailure)
            return Result.Failure<SalaryResponse>(reverseResult.Error);

        salary.Status = SalaryStatus.Cancelled;
        salary.RemainingAmount = 0;
        AddAuditLog("RiderMonthlySalary", salary.Id, "Reverse", reversedBy);

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(MapSalary(salary));
    }

    public async Task<Result<BonusRuleResponse>> CreateBonusRuleAsync(BonusRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (request.MinimumAcceptedOrders <= 0 || request.BonusAmount <= 0)
            return Result.Failure<BonusRuleResponse>(AccountingErrors.Invalid("Bonus rule threshold and amount must be greater than zero."));

        var rule = new RiderBonusRule
        {
            CompanyId = request.CompanyId,
            MinimumAcceptedOrders = request.MinimumAcceptedOrders,
            BonusAmount = request.BonusAmount,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Priority = request.Priority,
            Notes = request.Notes,
            IsActive = true
        };

        Db.RiderBonusRules.Add(rule);
        await Db.SaveChangesAsync(cancellationToken);

        var company = request.CompanyId is null
            ? null
            : await Db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken);

        return Result.Success(MapBonusRule(rule, company?.Name));
    }

    public async Task<Result<List<BonusRuleResponse>>> GetBonusRulesAsync(int? companyId = null, CancellationToken cancellationToken = default)
    {
        var rules = await Db.RiderBonusRules
            .Include(r => r.Company)
            .Where(r => companyId == null || r.CompanyId == companyId)
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.Priority)
            .ThenByDescending(r => r.MinimumAcceptedOrders)
            .Select(r => MapBonusRule(r, r.Company == null ? null : r.Company.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(rules);
    }

    public async Task<Result<FinancialItemTypeResponse>> CreateFinancialItemTypeAsync(
        FinancialItemTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<FinancialItemTypeResponse>(AccountingErrors.Invalid("Financial item type code and name are required."));

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await Db.RiderFinancialItemTypes.AnyAsync(t => t.Code == code, cancellationToken);
        if (exists)
            return Result.Failure<FinancialItemTypeResponse>(AccountingErrors.Invalid("Financial item type code already exists."));

        var type = new RiderFinancialItemType
        {
            Code = code,
            Name = request.Name.Trim(),
            Category = request.Category,
            IsSystem = false,
            IsActive = true
        };

        Db.RiderFinancialItemTypes.Add(type);
        await Db.SaveChangesAsync(cancellationToken);

        return Result.Success(MapFinancialItemType(type));
    }

    public async Task<Result<RiderFinancialItemResponse>> CreateFinancialItemAsync(
        RiderFinancialItemRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var periodResult = await EnsureOpenPeriodAsync(request.Year, request.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<RiderFinancialItemResponse>(periodResult.Error);

        var rider = await Db.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == request.RiderId, cancellationToken);
        var type = await Db.RiderFinancialItemTypes.FirstOrDefaultAsync(t => t.Id == request.RiderFinancialItemTypeId && t.IsActive, cancellationToken);

        if (rider is null)
            return Result.Failure<RiderFinancialItemResponse>(AccountingErrors.NotFound("Rider"));
        if (type is null)
            return Result.Failure<RiderFinancialItemResponse>(AccountingErrors.NotFound("Financial item type"));

        var item = new RiderFinancialItem
        {
            RiderFinancialItemTypeId = type.Id,
            RiderId = rider.Id,
            EmployeeIqamaNo = rider.EmployeeIqamaNo,
            CompanyId = request.CompanyId,
            HousingId = request.HousingId,
            VehicleNumber = request.VehicleNumber,
            Year = request.Year,
            Month = request.Month,
            OccurredOn = request.OccurredOn,
            Amount = request.Amount,
            RemainingAmount = request.Amount,
            Status = AccountingRecordStatus.Approved,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
            CreatedBy = createdBy
        };

        Db.RiderFinancialItems.Add(item);
        await Db.SaveChangesAsync(cancellationToken);

        item.Type = type;
        item.Rider = rider;
        return Result.Success(MapFinancialItem(item));
    }

    public async Task<Result<List<RiderFinancialItemResponse>>> CreateBulkInternetReplacementAsync(
        BulkInternetReplacementRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            return Result.Failure<List<RiderFinancialItemResponse>>(AccountingErrors.Invalid("Internet replacement amount must be greater than zero."));

        var periodResult = await EnsureOpenPeriodAsync(request.Year, request.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<List<RiderFinancialItemResponse>>(periodResult.Error);

        var companyExists = await Db.Companies.AnyAsync(c => c.Id == request.CompanyId, cancellationToken);
        if (!companyExists)
            return Result.Failure<List<RiderFinancialItemResponse>>(AccountingErrors.NotFound("Company"));

        var type = await EnsureFinancialItemTypeAsync(
            "INTERNET_REPLACEMENT",
            "Internet Replacement",
            FinancialItemCategory.Reimbursement,
            cancellationToken);

        var riders = await Db.RiderDetails
            .Include(r => r.Employee)
            .Where(r => r.CompanyId == request.CompanyId)
            .OrderBy(r => r.WorkingId)
            .ToListAsync(cancellationToken);

        if (riders.Count == 0)
            return Result.Failure<List<RiderFinancialItemResponse>>(AccountingErrors.Invalid("No riders found for this company."));

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        if (request.ReplaceExisting)
        {
            var riderIds = riders.Select(r => r.Id).ToList();
            var existing = await Db.RiderFinancialItems
                .Where(i => i.Year == request.Year
                    && i.Month == request.Month
                    && i.CompanyId == request.CompanyId
                    && i.RiderFinancialItemTypeId == type.Id
                    && riderIds.Contains(i.RiderId)
                    && i.Status != AccountingRecordStatus.Posted)
                .ToListAsync(cancellationToken);
            Db.RiderFinancialItems.RemoveRange(existing);
            await Db.SaveChangesAsync(cancellationToken);
        }

        var items = riders.Select(r => new RiderFinancialItem
        {
            RiderFinancialItemTypeId = type.Id,
            RiderId = r.Id,
            EmployeeIqamaNo = r.EmployeeIqamaNo,
            CompanyId = request.CompanyId,
            HousingId = r.Employee?.HousingId,
            VehicleNumber = r.VehicleNumber,
            Year = request.Year,
            Month = request.Month,
            OccurredOn = request.OccurredOn,
            Amount = request.Amount,
            RemainingAmount = request.Amount,
            Status = AccountingRecordStatus.Approved,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
            CreatedBy = createdBy,
            Type = type,
            Rider = r
        }).ToList();

        Db.RiderFinancialItems.AddRange(items);
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(items.Select(MapFinancialItem).ToList());
    }

    public async Task<Result<List<RiderEarningResponse>>> CreateFixedMonthlyEarningsAsync(
        FixedMonthlyEarningRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (request.SalaryAmount <= 0)
            return Result.Failure<List<RiderEarningResponse>>(AccountingErrors.Invalid("Fixed salary amount must be greater than zero."));

        var periodResult = await EnsureOpenPeriodAsync(request.Year, request.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<List<RiderEarningResponse>>(periodResult.Error);

        var companyExists = await Db.Companies.AnyAsync(c => c.Id == request.CompanyId, cancellationToken);
        if (!companyExists)
            return Result.Failure<List<RiderEarningResponse>>(AccountingErrors.NotFound("Company"));

        var riders = await Db.RiderDetails
            .Where(r => r.CompanyId == request.CompanyId)
            .OrderBy(r => r.WorkingId)
            .ToListAsync(cancellationToken);

        if (riders.Count == 0)
            return Result.Failure<List<RiderEarningResponse>>(AccountingErrors.Invalid("No riders found for this company."));

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        if (request.ReplaceExisting)
        {
            var riderIds = riders.Select(r => r.Id).ToList();
            var existing = await Db.RiderEarnings
                .Where(e => e.Year == request.Year
                    && e.Month == request.Month
                    && e.CompanyId == request.CompanyId
                    && e.SourceType == "FixedMonthlySalary"
                    && riderIds.Contains(e.PaidRiderId)
                    && e.Status != AccountingRecordStatus.Posted)
                .ToListAsync(cancellationToken);
            Db.RiderEarnings.RemoveRange(existing);
            await Db.SaveChangesAsync(cancellationToken);
        }

        var earnings = riders.Select(r => new RiderEarning
        {
            CompanyId = request.CompanyId,
            OriginalRiderId = r.Id,
            PaidRiderId = r.Id,
            Year = request.Year,
            Month = request.Month,
            GrossAmount = request.SalaryAmount,
            SalaryAmount = request.SalaryAmount,
            SourceType = "FixedMonthlySalary",
            Status = AccountingRecordStatus.Approved,
            Notes = string.IsNullOrWhiteSpace(request.Notes)
                ? $"Fixed monthly salary created by {createdBy}."
                : request.Notes,
            PaidRider = r,
            OriginalRider = r
        }).ToList();

        Db.RiderEarnings.AddRange(earnings);
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(earnings.Select(MapEarning).ToList());
    }

    public async Task<Result<RiderLoanResponse>> CreateLoanAsync(
        RiderLoanRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (request.PrincipalAmount <= 0 || request.InstallmentCount <= 0)
            return Result.Failure<RiderLoanResponse>(AccountingErrors.Invalid("Loan amount and installment count must be greater than zero."));

        var riderExists = await Db.RiderDetails.AnyAsync(r => r.Id == request.RiderId, cancellationToken);
        if (!riderExists)
            return Result.Failure<RiderLoanResponse>(AccountingErrors.NotFound("Rider"));

        var loan = new RiderLoan
        {
            RiderId = request.RiderId,
            PrincipalAmount = request.PrincipalAmount,
            RemainingAmount = request.PrincipalAmount,
            FirstDeductionYear = request.FirstDeductionYear,
            FirstDeductionMonth = request.FirstDeductionMonth,
            InstallmentCount = request.InstallmentCount,
            Status = AccountingRecordStatus.Approved,
            Notes = request.Notes,
            CreatedBy = createdBy
        };

        var baseAmount = Math.Round(request.PrincipalAmount / request.InstallmentCount, 2);
        var runningMonth = new DateOnly(request.FirstDeductionYear, request.FirstDeductionMonth, 1);
        var allocated = 0m;

        for (var i = 0; i < request.InstallmentCount; i++)
        {
            var amount = i == request.InstallmentCount - 1
                ? request.PrincipalAmount - allocated
                : baseAmount;
            allocated += amount;

            loan.Installments.Add(new RiderLoanInstallment
            {
                Year = runningMonth.Year,
                Month = runningMonth.Month,
                Amount = amount,
                Status = AccountingRecordStatus.Approved
            });

            runningMonth = runningMonth.AddMonths(1);
        }

        Db.RiderLoans.Add(loan);
        await Db.SaveChangesAsync(cancellationToken);

        return Result.Success(MapLoan(loan));
    }

    private async Task EnsureEarningsFromSummariesAsync(GenerateSalaryRequest request, CancellationToken cancellationToken)
    {
        var existingSummaryIds = await Db.RiderEarnings
            .Where(e => e.CompanyBillRiderSummaryId != null)
            .Select(e => e.CompanyBillRiderSummaryId!.Value)
            .ToListAsync(cancellationToken);

        var summaries = await Db.CompanyBillRiderSummaries
            .Include(s => s.CompanyBillImport)
            .Where(s => s.CompanyBillImport.Year == request.Year && s.CompanyBillImport.Month == request.Month)
            .Where(s => request.CompanyId == null || s.CompanyBillImport.CompanyId == request.CompanyId)
            .Where(s => s.PaidRiderId != null && !existingSummaryIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        foreach (var summary in summaries)
        {
            var amount = summary.CompanyBillImport.TemplateType == CompanyBillTemplateType.FtrHunger
                ? summary.AcceptedOrders >= 500 ? 2000m + (summary.AcceptedOrders - 500) * 6m : summary.AcceptedOrders * 3m
                : summary.NetAmount != 0 ? summary.NetAmount : summary.BasicPayment + summary.BonusAmount + summary.RiderBalance - summary.PenaltyAmount;

            Db.RiderEarnings.Add(new RiderEarning
            {
                CompanyBillImportId = summary.CompanyBillImportId,
                CompanyBillRiderSummaryId = summary.Id,
                CompanyId = summary.CompanyBillImport.CompanyId,
                OriginalRiderId = summary.OriginalRiderId,
                PaidRiderId = summary.PaidRiderId!.Value,
                Year = request.Year,
                Month = request.Month,
                AcceptedOrders = summary.AcceptedOrders,
                RejectedOrders = summary.RejectedOrders,
                GrossAmount = summary.NetAmount,
                DistanceAmount = summary.DistanceAmount,
                SalaryAmount = amount,
                SourceType = "CompanyBillRiderSummary",
                Status = summary.ResolutionStatus == ImportResolutionStatus.Resolved
                    ? AccountingRecordStatus.Approved
                    : AccountingRecordStatus.PendingReview,
                Notes = summary.ResolutionNotes
            });
        }
    }

    private async Task<List<RiderBonusRule>> GetActiveBonusRulesAsync(int year, int month, CancellationToken cancellationToken)
    {
        var periodStart = PeriodStart(year, month);
        var periodEnd = PeriodEnd(year, month);

        return await Db.RiderBonusRules
            .Where(r => r.IsActive && r.EffectiveFrom <= periodEnd && (r.EffectiveTo == null || r.EffectiveTo >= periodStart))
            .ToListAsync(cancellationToken);
    }

    private async Task<RiderFinancialItemType> EnsureFinancialItemTypeAsync(
        string code,
        string name,
        FinancialItemCategory category,
        CancellationToken cancellationToken)
    {
        var type = await Db.RiderFinancialItemTypes
            .FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

        if (type is not null)
            return type;

        type = new RiderFinancialItemType
        {
            Code = code,
            Name = name,
            Category = category,
            IsSystem = true,
            IsActive = true
        };

        Db.RiderFinancialItemTypes.Add(type);
        await Db.SaveChangesAsync(cancellationToken);
        return type;
    }

    private static void RecalculateSalary(RiderMonthlySalary salary)
    {
        salary.GrossEarnings = salary.Lines.Where(l => l.Type == SalaryLineType.Earning).Sum(l => l.Amount);
        salary.TotalBonuses = salary.Lines.Where(l => l.Type == SalaryLineType.Bonus).Sum(l => l.Amount);
        salary.TotalAllowances = salary.Lines.Where(l => l.Type is SalaryLineType.Allowance or SalaryLineType.Reimbursement).Sum(l => l.Amount);
        salary.TotalDeductions = salary.Lines.Where(l => l.Type == SalaryLineType.Deduction).Sum(l => l.Amount);
        salary.NetSalary = salary.GrossEarnings + salary.TotalBonuses + salary.TotalAllowances - salary.TotalDeductions;
        salary.RemainingAmount = salary.NetSalary - salary.PaidAmount;
    }

    private static SalaryLineType ToSalaryLineType(FinancialItemCategory category) => category switch
    {
        FinancialItemCategory.Earning => SalaryLineType.Earning,
        FinancialItemCategory.Deduction => SalaryLineType.Deduction,
        FinancialItemCategory.Allowance => SalaryLineType.Allowance,
        FinancialItemCategory.Reimbursement => SalaryLineType.Reimbursement,
        FinancialItemCategory.CompanyCost => SalaryLineType.InformationOnly,
        _ => SalaryLineType.InformationOnly
    };

    internal static SalaryResponse MapSalary(RiderMonthlySalary salary)
        => new(
            salary.Id,
            salary.RiderId,
            salary.Rider.WorkingId,
            salary.Rider.Employee?.NameEN ?? salary.Rider.Employee?.NameAR ?? string.Empty,
            salary.Year,
            salary.Month,
            salary.PaymentMethod,
            salary.Status,
            salary.GrossEarnings,
            salary.TotalBonuses,
            salary.TotalAllowances,
            salary.TotalDeductions,
            salary.NetSalary,
            salary.PaidAmount,
            salary.RemainingAmount,
            salary.IbanSnapshot,
            salary.Lines.OrderBy(l => l.Id).Select(l => new SalaryLineResponse(
                l.Id,
                l.Type,
                l.Description,
                l.Amount,
                l.SourceType,
                l.SourceId,
                l.IsEditable,
                l.Notes)).ToList());

    private static BonusRuleResponse MapBonusRule(RiderBonusRule rule, string? companyName)
        => new(rule.Id, rule.CompanyId, companyName, rule.MinimumAcceptedOrders, rule.BonusAmount, rule.EffectiveFrom, rule.EffectiveTo, rule.Priority, rule.IsActive, rule.Notes);

    private static FinancialItemTypeResponse MapFinancialItemType(RiderFinancialItemType type)
        => new(type.Id, type.Code, type.Name, type.Category, type.IsSystem, type.IsActive);

    private static RiderFinancialItemResponse MapFinancialItem(RiderFinancialItem item)
        => new(
            item.Id,
            item.RiderId,
            item.Rider.WorkingId,
            item.Type.Code,
            item.Type.Name,
            item.Type.Category,
            item.Year,
            item.Month,
            item.OccurredOn,
            item.Amount,
            item.RemainingAmount,
            item.Status,
            item.ReferenceNumber,
            item.Notes);

    private static RiderEarningResponse MapEarning(RiderEarning earning)
        => new(
            earning.Id,
            earning.PaidRiderId,
            earning.PaidRider.WorkingId,
            earning.CompanyId,
            earning.Year,
            earning.Month,
            earning.AcceptedOrders,
            earning.GrossAmount,
            earning.SalaryAmount,
            earning.SourceType,
            earning.Status,
            earning.Notes);

    private static RiderLoanResponse MapLoan(RiderLoan loan)
        => new(
            loan.Id,
            loan.RiderId,
            loan.PrincipalAmount,
            loan.RemainingAmount,
            loan.FirstDeductionYear,
            loan.FirstDeductionMonth,
            loan.InstallmentCount,
            loan.Status,
            loan.Installments
                .OrderBy(i => i.Year)
                .ThenBy(i => i.Month)
                .Select(i => new RiderLoanInstallmentResponse(i.Id, i.Year, i.Month, i.Amount, i.PaidAmount, i.Status))
                .ToList());
}

public class AccountingPaymentService(ApplicationDbcontext db) : AccountingServiceBase(db), IAccountingPaymentService
{
    public async Task<Result<PaymentBatchResponse>> CreateBankPaymentBatchAsync(
        CreatePaymentBatchRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var periodResult = await EnsureOpenPeriodAsync(request.Year, request.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<PaymentBatchResponse>(periodResult.Error);

        var salaries = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .ThenInclude(r => r.Employee)
            .Where(s => s.Year == request.Year && s.Month == request.Month)
            .Where(s => s.PaymentMethod == RiderPaymentMethod.BankTransfer || s.PaymentMethod == RiderPaymentMethod.Mixed)
            .Where(s => s.Status == SalaryStatus.Approved || s.Status == SalaryStatus.PartiallyPaid)
            .Where(s => s.RemainingAmount > 0)
            .Where(s => request.CompanyId == null || s.Rider.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var blockedSalaryIds = await Db.RiderSalaryPayments
            .Include(p => p.Batch)
            .Where(p => p.Batch.Year == request.Year && p.Batch.Month == request.Month)
            .Where(p => p.Batch.PaymentMethod == RiderPaymentMethod.BankTransfer)
            .Where(p => p.Batch.Status == PaymentBatchStatus.Prepared
                || p.Batch.Status == PaymentBatchStatus.Sent
                || p.Batch.Status == PaymentBatchStatus.PartiallyConfirmed)
            .Where(p => p.Status == PaymentBatchStatus.Prepared || p.Status == PaymentBatchStatus.Sent)
            .Select(p => p.RiderMonthlySalaryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        salaries = salaries.Where(s => !blockedSalaryIds.Contains(s.Id)).ToList();

        var batch = new RiderSalaryPaymentBatch
        {
            Year = request.Year,
            Month = request.Month,
            PaymentMethod = RiderPaymentMethod.BankTransfer,
            Status = PaymentBatchStatus.Prepared,
            CreatedBy = createdBy,
            Notes = request.Notes
        };

        foreach (var salary in salaries)
        {
            var iban = salary.IbanSnapshot ?? salary.Rider.Employee?.IBAN;
            if (string.IsNullOrWhiteSpace(iban))
            {
                salary.Notes = AppendNote(salary.Notes, "Skipped from bank batch because IBAN is missing.");
                continue;
            }

            batch.Payments.Add(new RiderSalaryPayment
            {
                RiderMonthlySalaryId = salary.Id,
                RiderId = salary.RiderId,
                Amount = salary.RemainingAmount,
                IbanSnapshot = iban,
                Status = PaymentBatchStatus.Prepared,
                Notes = request.Notes
            });
        }

        batch.TotalAmount = batch.Payments.Sum(p => p.Amount);
        batch.PaymentCount = batch.Payments.Count;

        Db.RiderSalaryPaymentBatches.Add(batch);
        await Db.SaveChangesAsync(cancellationToken);

        return await GetPaymentBatchAsync(batch.Id, cancellationToken);
    }

    public async Task<Result<AccountingFileResponse>> ExportBankPaymentBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var batchResult = await GetPaymentBatchEntityAsync(batchId, cancellationToken);
        if (batchResult.IsFailure)
            return Result.Failure<AccountingFileResponse>(batchResult.Error);

        var batch = batchResult.Value;
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Bank Transfers");
        ws.Cell(1, 1).Value = "Rider Id";
        ws.Cell(1, 2).Value = "Working Id";
        ws.Cell(1, 3).Value = "Rider Name";
        ws.Cell(1, 4).Value = "IBAN";
        ws.Cell(1, 5).Value = "Amount";
        ws.Cell(1, 6).Value = "Reference";
        ws.Cell(1, 7).Value = "Notes";

        var row = 2;
        foreach (var payment in batch.Payments.OrderBy(p => p.Rider.Employee.NameEN))
        {
            ws.Cell(row, 1).Value = payment.RiderId;
            ws.Cell(row, 2).Value = payment.Rider.WorkingId;
            ws.Cell(row, 3).Value = payment.Rider.Employee?.NameEN ?? payment.Rider.Employee?.NameAR;
            ws.Cell(row, 4).Value = payment.IbanSnapshot;
            ws.Cell(row, 5).Value = payment.Amount;
            ws.Cell(row, 6).Value = payment.ReferenceNumber;
            ws.Cell(row, 7).Value = payment.Notes;
            row++;
        }

        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return Result.Success(new AccountingFileResponse(
            $"bank-transfers-{batch.Year}-{batch.Month:00}-{batch.Id}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            stream.ToArray()));
    }

    public async Task<Result<PaymentBatchResponse>> MarkBankPaymentBatchSentAsync(
        int batchId,
        string sentBy,
        CancellationToken cancellationToken = default)
    {
        var batchResult = await GetPaymentBatchEntityAsync(batchId, cancellationToken);
        if (batchResult.IsFailure)
            return Result.Failure<PaymentBatchResponse>(batchResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);
        var batch = batchResult.Value;
        if (batch.Status != PaymentBatchStatus.Prepared)
            return Result.Failure<PaymentBatchResponse>(AccountingErrors.Invalid("Only prepared bank batches can be sent."));

        batch.Status = PaymentBatchStatus.Sent;
        batch.SentAt = AccountingClock.Now;
        batch.SentBy = sentBy;

        foreach (var payment in batch.Payments)
        {
            if (payment.Status == PaymentBatchStatus.Prepared)
                payment.Status = PaymentBatchStatus.Sent;
        }

        AddAuditLog("RiderSalaryPaymentBatch", batch.Id, "Send", sentBy, batch.Notes);
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetPaymentBatchAsync(batchId, cancellationToken);
    }

    public async Task<Result<PaymentBatchResponse>> ConfirmBankPaymentBatchAsync(
        int batchId,
        BankPaymentConfirmationRequest request,
        string confirmedBy,
        CancellationToken cancellationToken = default)
    {
        var batchResult = await GetPaymentBatchEntityAsync(batchId, cancellationToken);
        if (batchResult.IsFailure)
            return Result.Failure<PaymentBatchResponse>(batchResult.Error);

        var batch = batchResult.Value;
        var periodResult = await EnsureOpenPeriodAsync(batch.Year, batch.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<PaymentBatchResponse>(periodResult.Error);

        if (batch.Status is not (PaymentBatchStatus.Sent or PaymentBatchStatus.PartiallyConfirmed))
            return Result.Failure<PaymentBatchResponse>(AccountingErrors.Invalid("Only sent bank batches can be confirmed."));

        var confirmed = request.ConfirmedPayments ?? [];
        var rejected = request.RejectedPayments ?? [];
        var confirmedIds = confirmed.Select(p => p.PaymentId).ToHashSet();
        var rejectedIds = rejected.Select(p => p.PaymentId).ToHashSet();

        if (confirmedIds.Overlaps(rejectedIds))
            return Result.Failure<PaymentBatchResponse>(AccountingErrors.Invalid("A payment cannot be both confirmed and rejected."));

        var requestedIds = confirmedIds.Concat(rejectedIds).ToHashSet();
        if (requestedIds.Count == 0)
            return Result.Failure<PaymentBatchResponse>(AccountingErrors.Invalid("At least one payment must be confirmed or rejected."));

        if (batch.Payments.Any(p => requestedIds.Contains(p.Id) && p.RiderSalaryPaymentBatchId != batch.Id)
            || requestedIds.Any(id => batch.Payments.All(p => p.Id != id)))
            return Result.Failure<PaymentBatchResponse>(AccountingErrors.Invalid("One or more payments do not belong to this batch."));

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var confirmation in confirmed)
        {
            var payment = batch.Payments.First(p => p.Id == confirmation.PaymentId);
            if (payment.Status == PaymentBatchStatus.Confirmed)
                continue;
            if (payment.Status == PaymentBatchStatus.Failed)
                return Result.Failure<PaymentBatchResponse>(AccountingErrors.Invalid("Rejected payments cannot be confirmed."));

            payment.Status = PaymentBatchStatus.Confirmed;
            payment.ReferenceNumber = confirmation.ReferenceNumber ?? payment.ReferenceNumber;
            payment.Notes = AppendNote(payment.Notes, confirmation.Notes ?? request.Notes);
            payment.ConfirmedAt = AccountingClock.Now;
            payment.ConfirmedBy = confirmedBy;
            payment.Salary.PaidAmount += payment.Amount;
            payment.Salary.RemainingAmount = payment.Salary.NetSalary - payment.Salary.PaidAmount;
            payment.Salary.Status = payment.Salary.RemainingAmount <= 0 ? SalaryStatus.Paid : SalaryStatus.PartiallyPaid;

            var journalResult = await AddJournalEntryAsync(
                PeriodEnd(batch.Year, batch.Month),
                $"Bank salary payment {payment.Rider.WorkingId}",
                "RiderSalaryPayment",
                payment.Id,
                confirmedBy,
                cancellationToken,
                new JournalEntryLine { AccountId = AccountingAccountIds.RiderPayables, Debit = payment.Amount, RiderId = payment.RiderId, EmployeeIqamaNo = payment.Rider.EmployeeIqamaNo },
                new JournalEntryLine { AccountId = AccountingAccountIds.CashAndBank, Credit = payment.Amount, RiderId = payment.RiderId, EmployeeIqamaNo = payment.Rider.EmployeeIqamaNo });

            if (journalResult.IsFailure)
                return Result.Failure<PaymentBatchResponse>(journalResult.Error);

            AddAuditLog("RiderSalaryPayment", payment.Id, "Confirm", confirmedBy, confirmation.Notes);
        }

        foreach (var rejection in rejected)
        {
            var payment = batch.Payments.First(p => p.Id == rejection.PaymentId);
            if (payment.Status == PaymentBatchStatus.Confirmed)
                return Result.Failure<PaymentBatchResponse>(AccountingErrors.Invalid("Confirmed payments cannot be rejected."));

            payment.Status = PaymentBatchStatus.Failed;
            payment.Notes = AppendNote(payment.Notes, rejection.Notes ?? request.Notes);
            AddAuditLog("RiderSalaryPayment", payment.Id, "Reject", confirmedBy, rejection.Notes);
        }

        batch.Status = batch.Payments.All(p => p.Status == PaymentBatchStatus.Confirmed)
            ? PaymentBatchStatus.Confirmed
            : batch.Payments.All(p => p.Status == PaymentBatchStatus.Failed)
                ? PaymentBatchStatus.Failed
                : PaymentBatchStatus.PartiallyConfirmed;

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetPaymentBatchAsync(batchId, cancellationToken);
    }

    public async Task<Result<PaymentLineResponse>> ReverseSalaryPaymentAsync(
        int paymentId,
        string reversedBy,
        CancellationToken cancellationToken = default)
    {
        var payment = await Db.RiderSalaryPayments
            .Include(p => p.Batch)
            .Include(p => p.Salary)
            .Include(p => p.Rider)
            .ThenInclude(r => r.Employee)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

        if (payment is null)
            return Result.Failure<PaymentLineResponse>(AccountingErrors.NotFound("Salary payment"));

        var periodResult = await EnsureOpenPeriodAsync(payment.Batch.Year, payment.Batch.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<PaymentLineResponse>(periodResult.Error);

        if (payment.Status != PaymentBatchStatus.Confirmed)
            return Result.Failure<PaymentLineResponse>(AccountingErrors.Invalid("Only confirmed payments can be reversed."));

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var reverseResult = await ReverseJournalEntriesForSourceAsync(
            "RiderSalaryPayment",
            payment.Id,
            PeriodEnd(payment.Batch.Year, payment.Batch.Month),
            reversedBy,
            "Salary payment reversed.",
            cancellationToken);

        if (reverseResult.IsFailure)
            return Result.Failure<PaymentLineResponse>(reverseResult.Error);

        payment.Status = PaymentBatchStatus.Failed;
        payment.Notes = AppendNote(payment.Notes, "Payment reversed.");
        payment.Salary.PaidAmount = Math.Max(0, payment.Salary.PaidAmount - payment.Amount);
        payment.Salary.RemainingAmount = payment.Salary.NetSalary - payment.Salary.PaidAmount;
        payment.Salary.Status = payment.Salary.PaidAmount == 0 ? SalaryStatus.Approved : SalaryStatus.PartiallyPaid;
        AddAuditLog("RiderSalaryPayment", payment.Id, "Reverse", reversedBy);

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(MapPaymentLine(payment));
    }

    public async Task<Result<CashHandoverBatchResponse>> CreateCashHandoverBatchAsync(
        CreateCashHandoverBatchRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var periodResult = await EnsureOpenPeriodAsync(request.Year, request.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CashHandoverBatchResponse>(periodResult.Error);

        var salaries = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .ThenInclude(r => r.Employee)
            .Where(s => s.Year == request.Year && s.Month == request.Month)
            .Where(s => s.PaymentMethod == RiderPaymentMethod.Cash || s.PaymentMethod == RiderPaymentMethod.Mixed)
            .Where(s => s.Status == SalaryStatus.Approved || s.Status == SalaryStatus.PartiallyPaid)
            .Where(s => s.RemainingAmount > 0)
            .Where(s => request.HousingId == null || s.Rider.Employee.HousingId == request.HousingId)
            .Where(s => request.CompanyId == null || s.Rider.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var blockedCashSalaryIds = await Db.CashSalaryHandoverLines
            .Include(l => l.Batch)
            .Where(l => l.Batch.Year == request.Year && l.Batch.Month == request.Month)
            .Where(l => l.Batch.Status == PaymentBatchStatus.Prepared || l.Batch.Status == PaymentBatchStatus.PartiallyConfirmed)
            .Where(l => l.Status == CashHandoverLineStatus.Pending || l.Status == CashHandoverLineStatus.Delivered)
            .Select(l => l.RiderMonthlySalaryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        salaries = salaries.Where(s => !blockedCashSalaryIds.Contains(s.Id)).ToList();

        var batch = new CashSalaryHandoverBatch
        {
            Year = request.Year,
            Month = request.Month,
            HousingId = request.HousingId,
            Status = PaymentBatchStatus.Prepared,
            CreatedBy = createdBy,
            Notes = request.Notes
        };

        foreach (var salary in salaries)
        {
            batch.Lines.Add(new CashSalaryHandoverLine
            {
                RiderMonthlySalaryId = salary.Id,
                RiderId = salary.RiderId,
                Amount = salary.RemainingAmount,
                Status = CashHandoverLineStatus.Pending
            });
        }

        batch.TotalAmount = batch.Lines.Sum(l => l.Amount);
        Db.CashSalaryHandoverBatches.Add(batch);
        await Db.SaveChangesAsync(cancellationToken);

        return await GetCashBatchAsync(batch.Id, cancellationToken);
    }

    public async Task<Result<AccountingFileResponse>> ExportCashHandoverBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var batchResult = await GetCashBatchEntityAsync(batchId, cancellationToken);
        if (batchResult.IsFailure)
            return Result.Failure<AccountingFileResponse>(batchResult.Error);

        var batch = batchResult.Value;
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Cash Handover");
        ws.Cell(1, 1).Value = "Housing";
        ws.Cell(1, 2).Value = "Rider Id";
        ws.Cell(1, 3).Value = "Working Id";
        ws.Cell(1, 4).Value = "Rider Name";
        ws.Cell(1, 5).Value = "Iqama";
        ws.Cell(1, 6).Value = "Amount";
        ws.Cell(1, 7).Value = "Status";
        ws.Cell(1, 8).Value = "Signature";
        ws.Cell(1, 9).Value = "Notes";

        var row = 2;
        foreach (var line in batch.Lines.OrderBy(l => l.Rider.Employee.HousingId).ThenBy(l => l.Rider.Employee.NameEN))
        {
            ws.Cell(row, 1).Value = line.Rider.Employee?.Housing?.Name;
            ws.Cell(row, 2).Value = line.RiderId;
            ws.Cell(row, 3).Value = line.Rider.WorkingId;
            ws.Cell(row, 4).Value = line.Rider.Employee?.NameEN ?? line.Rider.Employee?.NameAR;
            ws.Cell(row, 5).Value = line.Rider.EmployeeIqamaNo.ToString(CultureInfo.InvariantCulture);
            ws.Cell(row, 6).Value = line.Amount;
            ws.Cell(row, 7).Value = line.Status.ToString();
            ws.Cell(row, 9).Value = line.MemberNotes;
            row++;
        }

        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return Result.Success(new AccountingFileResponse(
            $"cash-handover-{batch.Year}-{batch.Month:00}-{batch.Id}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            stream.ToArray()));
    }

    public async Task<Result<List<CashHandoverBatchResponse>>> GetCashHandoverForHousingManagerAsync(
        long managerIqamaNo,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var housingIds = await Db.Housings
            .Where(h => h.ManagerIqamaNo == managerIqamaNo)
            .Select(h => h.Id)
            .ToListAsync(cancellationToken);

        if (housingIds.Count == 0)
            return Result.Failure<List<CashHandoverBatchResponse>>(AccountingErrors.NotFound("Housing manager"));

        var batches = await Db.CashSalaryHandoverBatches
            .Include(b => b.Housing)
            .Include(b => b.Lines)
            .ThenInclude(l => l.Rider)
            .ThenInclude(r => r.Employee)
            .Where(b => b.Year == year && b.Month == month && b.HousingId != null && housingIds.Contains(b.HousingId.Value))
            .ToListAsync(cancellationToken);

        return Result.Success(batches.Select(MapCashBatch).ToList());
    }

    public async Task<Result<CashHandoverLineResponse>> SubmitCashHandoverLineAsync(
        int lineId,
        CashSalarySubmissionRequest request,
        long managerIqamaNo,
        string submittedBy,
        CancellationToken cancellationToken = default)
    {
        var line = await Db.CashSalaryHandoverLines
            .Include(l => l.Batch)
            .Include(l => l.Salary)
            .Include(l => l.Rider)
            .ThenInclude(r => r.Employee)
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);

        if (line is null)
            return Result.Failure<CashHandoverLineResponse>(AccountingErrors.NotFound("Cash handover line"));

        var periodResult = await EnsureOpenPeriodAsync(line.Batch.Year, line.Batch.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CashHandoverLineResponse>(periodResult.Error);

        var accessResult = await EnsureCashManagerAccessAsync(line.Batch, managerIqamaNo, cancellationToken);
        if (accessResult.IsFailure)
            return Result.Failure<CashHandoverLineResponse>(accessResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var applyResult = await ApplyCashSubmissionAsync(line, request, submittedBy, cancellationToken);
        if (applyResult.IsFailure)
            return Result.Failure<CashHandoverLineResponse>(applyResult.Error);

        UpdateCashBatchStatus(line.Batch, submittedBy);
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(MapCashLine(line));
    }

    public async Task<Result<CashHandoverBatchResponse>> SubmitCashHandoverBatchAsync(
        int batchId,
        CashSalarySubmissionRequest request,
        long managerIqamaNo,
        string submittedBy,
        CancellationToken cancellationToken = default)
    {
        var batchResult = await GetCashBatchEntityAsync(batchId, cancellationToken);
        if (batchResult.IsFailure)
            return Result.Failure<CashHandoverBatchResponse>(batchResult.Error);

        var batch = batchResult.Value;
        var periodResult = await EnsureOpenPeriodAsync(batch.Year, batch.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CashHandoverBatchResponse>(periodResult.Error);

        var accessResult = await EnsureCashManagerAccessAsync(batch, managerIqamaNo, cancellationToken);
        if (accessResult.IsFailure)
            return Result.Failure<CashHandoverBatchResponse>(accessResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var line in batch.Lines.Where(l => l.Status == CashHandoverLineStatus.Pending))
        {
            var applyResult = await ApplyCashSubmissionAsync(line, request, submittedBy, cancellationToken);
            if (applyResult.IsFailure)
                return Result.Failure<CashHandoverBatchResponse>(applyResult.Error);
        }

        UpdateCashBatchStatus(batch, submittedBy);

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(MapCashBatch(batch));
    }

    private async Task<Result> ApplyCashSubmissionAsync(
        CashSalaryHandoverLine line,
        CashSalarySubmissionRequest request,
        string submittedBy,
        CancellationToken cancellationToken)
    {
        if (line.Status == CashHandoverLineStatus.Delivered)
        {
            if (request.Status == CashHandoverLineStatus.Delivered)
                return Result.Success();

            return Result.Failure(AccountingErrors.Invalid("Delivered cash lines cannot be changed."));
        }

        line.Status = request.Status;
        line.SubmittedBy = submittedBy;
        line.SubmittedAt = AccountingClock.Now;
        line.MemberNotes = request.Notes;

        if (request.Status != CashHandoverLineStatus.Delivered)
            return Result.Success();

        line.Salary.PaidAmount += line.Amount;
        line.Salary.RemainingAmount = line.Salary.NetSalary - line.Salary.PaidAmount;
        line.Salary.Status = line.Salary.RemainingAmount <= 0 ? SalaryStatus.Paid : SalaryStatus.PartiallyPaid;

        var journalResult = await AddJournalEntryAsync(
            PeriodEnd(line.Batch.Year, line.Batch.Month),
            $"Cash salary handover {line.Rider.WorkingId}",
            "CashSalaryHandoverLine",
            line.Id,
            submittedBy,
            cancellationToken,
            new JournalEntryLine { AccountId = AccountingAccountIds.RiderPayables, Debit = line.Amount, RiderId = line.RiderId, EmployeeIqamaNo = line.Rider.EmployeeIqamaNo },
            new JournalEntryLine { AccountId = AccountingAccountIds.CashAndBank, Credit = line.Amount, RiderId = line.RiderId, EmployeeIqamaNo = line.Rider.EmployeeIqamaNo });

        if (journalResult.IsFailure)
            return Result.Failure(journalResult.Error);

        AddAuditLog("CashSalaryHandoverLine", line.Id, "Deliver", submittedBy, request.Notes);
        return Result.Success();
    }

    private static void UpdateCashBatchStatus(CashSalaryHandoverBatch batch, string reviewedBy)
    {
        if (batch.Lines.Count == 0)
            return;

        if (batch.Lines.All(l => l.Status == CashHandoverLineStatus.Delivered))
            batch.Status = PaymentBatchStatus.Confirmed;
        else if (batch.Lines.Any(l => l.Status != CashHandoverLineStatus.Pending))
            batch.Status = PaymentBatchStatus.PartiallyConfirmed;

        batch.ReviewedBy = reviewedBy;
        batch.ReviewedAt = AccountingClock.Now;
    }

    private async Task<Result> EnsureCashManagerAccessAsync(
        CashSalaryHandoverBatch batch,
        long managerIqamaNo,
        CancellationToken cancellationToken)
    {
        if (managerIqamaNo <= 0)
            return Result.Failure(AccountingErrors.Invalid("Authenticated member iqama is required."));

        if (batch.HousingId is null)
            return Result.Failure(AccountingErrors.Invalid("Cash batch is not assigned to a housing manager."));

        var allowed = await Db.Housings
            .AnyAsync(h => h.Id == batch.HousingId && h.ManagerIqamaNo == managerIqamaNo, cancellationToken);

        return allowed
            ? Result.Success()
            : Result.Failure(AccountingErrors.Invalid("Cash batch is outside this member's housing."));
    }

    private async Task<Result<PaymentBatchResponse>> GetPaymentBatchAsync(int batchId, CancellationToken cancellationToken)
    {
        var batchResult = await GetPaymentBatchEntityAsync(batchId, cancellationToken);
        return batchResult.IsFailure
            ? Result.Failure<PaymentBatchResponse>(batchResult.Error)
            : Result.Success(MapPaymentBatch(batchResult.Value));
    }

    private async Task<Result<RiderSalaryPaymentBatch>> GetPaymentBatchEntityAsync(int batchId, CancellationToken cancellationToken)
    {
        var batch = await Db.RiderSalaryPaymentBatches
            .Include(b => b.Payments)
            .ThenInclude(p => p.Salary)
            .Include(b => b.Payments)
            .ThenInclude(p => p.Rider)
            .ThenInclude(r => r.Employee)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        return batch is null
            ? Result.Failure<RiderSalaryPaymentBatch>(AccountingErrors.NotFound("Payment batch"))
            : Result.Success(batch);
    }

    private async Task<Result<CashHandoverBatchResponse>> GetCashBatchAsync(int batchId, CancellationToken cancellationToken)
    {
        var batchResult = await GetCashBatchEntityAsync(batchId, cancellationToken);
        return batchResult.IsFailure
            ? Result.Failure<CashHandoverBatchResponse>(batchResult.Error)
            : Result.Success(MapCashBatch(batchResult.Value));
    }

    private async Task<Result<CashSalaryHandoverBatch>> GetCashBatchEntityAsync(int batchId, CancellationToken cancellationToken)
    {
        var batch = await Db.CashSalaryHandoverBatches
            .Include(b => b.Housing)
            .Include(b => b.Lines)
            .ThenInclude(l => l.Salary)
            .Include(b => b.Lines)
            .ThenInclude(l => l.Rider)
            .ThenInclude(r => r.Employee)
            .ThenInclude(e => e.Housing)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        return batch is null
            ? Result.Failure<CashSalaryHandoverBatch>(AccountingErrors.NotFound("Cash handover batch"))
            : Result.Success(batch);
    }

    private static PaymentBatchResponse MapPaymentBatch(RiderSalaryPaymentBatch batch)
        => new(
            batch.Id,
            batch.Year,
            batch.Month,
            batch.PaymentMethod,
            batch.Status,
            batch.TotalAmount,
            batch.PaymentCount,
            batch.Payments.OrderBy(p => p.Id).Select(MapPaymentLine).ToList(),
            batch.Notes);

    private static PaymentLineResponse MapPaymentLine(RiderSalaryPayment payment)
        => new(
            payment.Id,
            payment.RiderId,
            payment.Rider.WorkingId,
            payment.Rider.Employee?.NameEN ?? payment.Rider.Employee?.NameAR ?? string.Empty,
            payment.Amount,
            payment.IbanSnapshot,
            payment.BankNameSnapshot,
            payment.Status,
            payment.ReferenceNumber,
            payment.Notes);

    private static CashHandoverBatchResponse MapCashBatch(CashSalaryHandoverBatch batch)
        => new(
            batch.Id,
            batch.Year,
            batch.Month,
            batch.HousingId,
            batch.Housing?.Name,
            batch.Status,
            batch.TotalAmount,
            batch.Lines.OrderBy(l => l.Id).Select(MapCashLine).ToList(),
            batch.Notes);

    private static CashHandoverLineResponse MapCashLine(CashSalaryHandoverLine line)
        => new(
            line.Id,
            line.RiderId,
            line.Rider.WorkingId,
            line.Rider.Employee?.NameEN ?? line.Rider.Employee?.NameAR ?? string.Empty,
            line.Amount,
            line.Status,
            line.SubmittedBy,
            line.SubmittedAt,
            line.MemberNotes);

}

public class CompanyFinanceService(ApplicationDbcontext db) : AccountingServiceBase(db), ICompanyFinanceService
{
    public async Task<Result<CompanyFinanceSummaryResponse>> GetSummaryAsync(
        int year,
        int month,
        int? companyId,
        CancellationToken cancellationToken = default)
    {
        var receivables = await Db.CompanyReceivables
            .Where(r => r.Year == year && r.Month == month)
            .Where(r => companyId == null || r.CompanyId == companyId)
            .Where(r => r.Status != AccountingRecordStatus.Cancelled && r.Status != AccountingRecordStatus.Reversed)
            .ToListAsync(cancellationToken);

        var salaries = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .Where(s => s.Year == year && s.Month == month)
            .Where(s => companyId == null || s.Rider.CompanyId == companyId)
            .Where(s => s.Status != SalaryStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var expenses = await Db.CompanyExpenses
            .Where(e => e.ExpenseDate.Year == year && e.ExpenseDate.Month == month)
            .Where(e => companyId == null || e.CompanyId == companyId)
            .Where(e => e.Status != AccountingRecordStatus.Cancelled && e.Status != AccountingRecordStatus.Reversed)
            .ToListAsync(cancellationToken);

        var supplierPayables = await Db.SupplierPayables
            .Where(p => p.DueDate.Year == year && p.DueDate.Month == month)
            .SumAsync(p => p.Amount - p.PaidAmount, cancellationToken);

        var payments = await Db.RiderSalaryPayments
            .Include(p => p.Batch)
            .Include(p => p.Rider)
            .Where(p => p.Batch.Year == year && p.Batch.Month == month)
            .Where(p => companyId == null || p.Rider.CompanyId == companyId)
            .Where(p => p.Status == PaymentBatchStatus.Confirmed)
            .ToListAsync(cancellationToken);

        var cashPayments = await Db.CashSalaryHandoverLines
            .Include(l => l.Batch)
            .Include(l => l.Rider)
            .Where(l => l.Batch.Year == year && l.Batch.Month == month)
            .Where(l => companyId == null || l.Rider.CompanyId == companyId)
            .Where(l => l.Status == CashHandoverLineStatus.Delivered)
            .SumAsync(l => l.Amount, cancellationToken);

        var deductionsRecovered = salaries.Sum(s => s.TotalDeductions);
        var companyExpenses = expenses.Sum(e => e.Amount + e.VatAmount);
        var netIncome = receivables.Sum(r => r.NetAmount);
        var riderSalaries = salaries.Sum(s => s.NetSalary);

        return Result.Success(new CompanyFinanceSummaryResponse(
            year,
            month,
            companyId,
            receivables.Sum(r => r.GrossAmount),
            receivables.Sum(r => r.VatAmount),
            netIncome,
            receivables.Sum(r => r.CollectedAmount),
            receivables.Sum(r => r.PendingAmount),
            riderSalaries,
            salaries.Sum(s => s.TotalBonuses),
            cashPayments,
            payments.Sum(p => p.Amount),
            deductionsRecovered,
            companyExpenses,
            supplierPayables,
            netIncome - riderSalaries - companyExpenses - supplierPayables + deductionsRecovered));
    }

    public async Task<Result<List<CompanyIncomeResponse>>> GetIncomeAsync(
        DateOnly from,
        DateOnly to,
        int? companyId,
        CancellationToken cancellationToken = default)
    {
        var rows = await Db.CompanyReceivables
            .Include(r => r.Company)
            .Where(r => new DateOnly(r.Year, r.Month, 1) >= new DateOnly(from.Year, from.Month, 1)
                && new DateOnly(r.Year, r.Month, 1) <= new DateOnly(to.Year, to.Month, 1))
            .Where(r => companyId == null || r.CompanyId == companyId)
            .Where(r => r.Status != AccountingRecordStatus.Cancelled && r.Status != AccountingRecordStatus.Reversed)
            .OrderBy(r => r.Year)
            .ThenBy(r => r.Month)
            .Select(r => new CompanyIncomeResponse(
                r.Id,
                r.CompanyId,
                r.Company == null ? null : r.Company.Name,
                r.Year,
                r.Month,
                r.GrossAmount,
                r.VatAmount,
                r.NetAmount,
                r.CollectedAmount,
                r.PendingAmount,
                r.Status,
                r.Notes))
            .ToListAsync(cancellationToken);

        return Result.Success(rows);
    }

    public async Task<Result<List<CompanyExpenseResponse>>> GetExpensesAsync(
        DateOnly from,
        DateOnly to,
        int? companyId,
        string? category,
        CancellationToken cancellationToken = default)
    {
        var rows = await Db.CompanyExpenses
            .Include(e => e.Category)
            .Include(e => e.Company)
            .Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to)
            .Where(e => companyId == null || e.CompanyId == companyId)
            .Where(e => string.IsNullOrWhiteSpace(category) || e.Category.Code == category || e.Category.Name == category)
            .OrderByDescending(e => e.ExpenseDate)
            .Select(e => MapExpense(e))
            .ToListAsync(cancellationToken);

        return Result.Success(rows);
    }

    public async Task<Result<CompanyExpenseResponse>> CreateExpenseAsync(
        CompanyExpenseRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var periodResult = await EnsureOpenPeriodAsync(request.ExpenseDate.Year, request.ExpenseDate.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CompanyExpenseResponse>(periodResult.Error);

        var category = await Db.CompanyExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.CompanyExpenseCategoryId, cancellationToken);
        if (category is null)
            return Result.Failure<CompanyExpenseResponse>(AccountingErrors.NotFound("Expense category"));

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var expense = new CompanyExpense
        {
            CompanyExpenseCategoryId = category.Id,
            CompanyId = request.CompanyId,
            CostCenterId = request.CostCenterId,
            RiderId = request.RiderId,
            HousingId = request.HousingId,
            VehicleNumber = request.VehicleNumber,
            ExpenseDate = request.ExpenseDate,
            Amount = request.Amount,
            VatAmount = request.VatAmount,
            ReferenceNumber = request.ReferenceNumber,
            Description = request.Description,
            Status = request.AutoApprove ? AccountingRecordStatus.Approved : AccountingRecordStatus.PendingReview,
            CreatedBy = createdBy
        };

        Db.CompanyExpenses.Add(expense);
        await Db.SaveChangesAsync(cancellationToken);

        if (expense.Status == AccountingRecordStatus.Approved)
        {
            var journalResult = await AddJournalEntryAsync(
                expense.ExpenseDate,
                expense.Description ?? category.Name,
                "CompanyExpense",
                expense.Id,
                createdBy,
                cancellationToken,
                new JournalEntryLine
                {
                    AccountId = ExpenseAccountId(category.Id),
                    Debit = expense.Amount + expense.VatAmount,
                    CostCenterId = expense.CostCenterId,
                    CompanyId = expense.CompanyId,
                    RiderId = expense.RiderId,
                    HousingId = expense.HousingId,
                    VehicleNumber = expense.VehicleNumber
                },
                new JournalEntryLine
                {
                    AccountId = AccountingAccountIds.CashAndBank,
                    Credit = expense.Amount + expense.VatAmount,
                    CostCenterId = expense.CostCenterId,
                    CompanyId = expense.CompanyId,
                    RiderId = expense.RiderId,
                    HousingId = expense.HousingId,
                    VehicleNumber = expense.VehicleNumber
                });

            if (journalResult.IsFailure)
                return Result.Failure<CompanyExpenseResponse>(journalResult.Error);
        }

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        expense.Category = category;
        return Result.Success(MapExpense(expense));
    }

    public async Task<Result<CompanyPaymentReceiptResponse>> CreateReceiptAsync(
        CompanyPaymentReceiptRequest request,
        string receivedBy,
        CancellationToken cancellationToken = default)
    {
        var periodResult = await EnsureOpenPeriodAsync(request.ReceiptDate.Year, request.ReceiptDate.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CompanyPaymentReceiptResponse>(periodResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var receivable = request.CompanyReceivableId is null
            ? null
            : await Db.CompanyReceivables.FirstOrDefaultAsync(r => r.Id == request.CompanyReceivableId, cancellationToken);

        if (request.CompanyReceivableId is not null && receivable is null)
            return Result.Failure<CompanyPaymentReceiptResponse>(AccountingErrors.NotFound("Company receivable"));

        if (request.Amount <= 0)
            return Result.Failure<CompanyPaymentReceiptResponse>(AccountingErrors.Invalid("Receipt amount must be greater than zero."));

        if (receivable is not null && request.Amount > receivable.PendingAmount)
            return Result.Failure<CompanyPaymentReceiptResponse>(AccountingErrors.Invalid("Receipt amount exceeds receivable pending amount."));

        var companyId = request.CompanyId ?? receivable?.CompanyId;
        if (!string.IsNullOrWhiteSpace(request.ReferenceNumber))
        {
            var duplicateReceipt = await Db.CompanyPaymentReceipts.AnyAsync(r =>
                r.CompanyId == companyId
                && r.ReceiptDate == request.ReceiptDate
                && r.ReferenceNumber == request.ReferenceNumber
                && r.BankAccount == request.BankAccount,
                cancellationToken);

            if (duplicateReceipt)
                return Result.Failure<CompanyPaymentReceiptResponse>(AccountingErrors.Invalid("A receipt with the same reference already exists."));
        }

        var receipt = new CompanyPaymentReceipt
        {
            CompanyReceivableId = receivable?.Id,
            CompanyId = companyId,
            ReceiptDate = request.ReceiptDate,
            Amount = request.Amount,
            ReferenceNumber = request.ReferenceNumber,
            BankAccount = request.BankAccount,
            Notes = request.Notes,
            ReceivedBy = receivedBy
        };

        Db.CompanyPaymentReceipts.Add(receipt);

        if (receivable is not null)
        {
            receivable.CollectedAmount += request.Amount;
            receivable.PendingAmount = Math.Max(0, receivable.NetAmount - receivable.CollectedAmount);
            receivable.Status = receivable.PendingAmount == 0 ? AccountingRecordStatus.Posted : AccountingRecordStatus.Approved;
        }

        await Db.SaveChangesAsync(cancellationToken);

        var receiptJournalResult = await AddJournalEntryAsync(
            receipt.ReceiptDate,
            $"Company receipt {receipt.ReferenceNumber}",
            "CompanyPaymentReceipt",
            receipt.Id,
            receivedBy,
            cancellationToken,
            new JournalEntryLine { AccountId = AccountingAccountIds.CashAndBank, Debit = receipt.Amount, CompanyId = receipt.CompanyId },
            new JournalEntryLine { AccountId = AccountingAccountIds.CompanyReceivables, Credit = receipt.Amount, CompanyId = receipt.CompanyId });

        if (receiptJournalResult.IsFailure)
            return Result.Failure<CompanyPaymentReceiptResponse>(receiptJournalResult.Error);

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(MapReceipt(receipt));
    }

    public async Task<Result<CompanyPaymentReceiptResponse>> ReverseReceiptAsync(
        int receiptId,
        string reversedBy,
        CancellationToken cancellationToken = default)
    {
        var receipt = await Db.CompanyPaymentReceipts
            .Include(r => r.CompanyReceivable)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);

        if (receipt is null)
            return Result.Failure<CompanyPaymentReceiptResponse>(AccountingErrors.NotFound("Company receipt"));

        var periodResult = await EnsureOpenPeriodAsync(receipt.ReceiptDate.Year, receipt.ReceiptDate.Month, cancellationToken);
        if (periodResult.IsFailure)
            return Result.Failure<CompanyPaymentReceiptResponse>(periodResult.Error);

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        var reverseResult = await ReverseJournalEntriesForSourceAsync(
            "CompanyPaymentReceipt",
            receipt.Id,
            receipt.ReceiptDate,
            reversedBy,
            "Company receipt reversed.",
            cancellationToken);

        if (reverseResult.IsFailure)
            return Result.Failure<CompanyPaymentReceiptResponse>(reverseResult.Error);

        if (receipt.CompanyReceivable is not null)
        {
            receipt.CompanyReceivable.CollectedAmount = Math.Max(0, receipt.CompanyReceivable.CollectedAmount - receipt.Amount);
            receipt.CompanyReceivable.PendingAmount = receipt.CompanyReceivable.NetAmount - receipt.CompanyReceivable.CollectedAmount;
            receipt.CompanyReceivable.Status = receipt.CompanyReceivable.CollectedAmount == 0
                ? AccountingRecordStatus.Posted
                : AccountingRecordStatus.Approved;
        }

        receipt.Notes = AppendNote(receipt.Notes, "Receipt reversed.");
        AddAuditLog("CompanyPaymentReceipt", receipt.Id, "Reverse", reversedBy);

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(MapReceipt(receipt));
    }

    public async Task<Result<ProfitLossResponse>> GetProfitLossAsync(
        DateOnly from,
        DateOnly to,
        int? companyId,
        CancellationToken cancellationToken = default)
    {
        var monthStart = new DateOnly(from.Year, from.Month, 1);
        var monthEnd = new DateOnly(to.Year, to.Month, 1);
        var receivables = await Db.CompanyReceivables
            .Include(r => r.Company)
            .Where(r => new DateOnly(r.Year, r.Month, 1) >= monthStart && new DateOnly(r.Year, r.Month, 1) <= monthEnd)
            .Where(r => companyId == null || r.CompanyId == companyId)
            .Where(r => r.Status != AccountingRecordStatus.Cancelled && r.Status != AccountingRecordStatus.Reversed)
            .ToListAsync(cancellationToken);
        var salaries = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .ThenInclude(r => r.Company)
            .Where(s => new DateOnly(s.Year, s.Month, 1) >= monthStart && new DateOnly(s.Year, s.Month, 1) <= monthEnd)
            .Where(s => companyId == null || s.Rider.CompanyId == companyId)
            .Where(s => s.Status != SalaryStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var expenses = await Db.CompanyExpenses
            .Include(e => e.Company)
            .Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to)
            .Where(e => companyId == null || e.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var supplierExpenses = await Db.SupplierPayables
            .Where(p => p.DueDate >= from && p.DueDate <= to)
            .SumAsync(p => p.Amount, cancellationToken);

        var breakdown = receivables
            .GroupBy(r => new { r.CompanyId, Name = r.Company?.Name ?? "No company" })
            .Select(g =>
            {
                var expensesForCompany = expenses.Where(e => e.CompanyId == g.Key.CompanyId).Sum(e => e.Amount + e.VatAmount);
                var salariesForCompany = salaries.Where(s => s.Rider.CompanyId == g.Key.CompanyId).Sum(s => s.NetSalary);
                var income = g.Sum(r => r.NetAmount);
                return new ProfitLossBreakdownLine("Company", g.Key.Name, income, expensesForCompany + salariesForCompany, income - expensesForCompany - salariesForCompany);
            })
            .ToList();

        var grossIncome = receivables.Sum(r => r.GrossAmount);
        var vat = receivables.Sum(r => r.VatAmount);
        var netIncome = receivables.Sum(r => r.NetAmount);
        var riderSalaryExpense = salaries.Sum(s => s.NetSalary);
        var companyExpenses = expenses.Sum(e => e.Amount + e.VatAmount);
        var deductionsRecovered = salaries.Sum(s => s.TotalDeductions);

        return Result.Success(new ProfitLossResponse(
            from,
            to,
            companyId,
            grossIncome,
            vat,
            netIncome,
            riderSalaryExpense,
            companyExpenses,
            supplierExpenses,
            deductionsRecovered,
            netIncome - riderSalaryExpense - companyExpenses - supplierExpenses + deductionsRecovered,
            breakdown));
    }

    public async Task<Result<List<CostCenterFinanceResponse>>> GetCostCentersAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var centers = await Db.CostCenters.ToListAsync(cancellationToken);
        var expenses = await Db.CompanyExpenses
            .Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to)
            .ToListAsync(cancellationToken);
        var incomes = await Db.CompanyReceivables
            .Where(r => new DateOnly(r.Year, r.Month, 1) >= new DateOnly(from.Year, from.Month, 1)
                && new DateOnly(r.Year, r.Month, 1) <= new DateOnly(to.Year, to.Month, 1))
            .Where(r => r.Status != AccountingRecordStatus.Cancelled && r.Status != AccountingRecordStatus.Reversed)
            .ToListAsync(cancellationToken);
        var salaries = await Db.RiderMonthlySalaries
            .Include(s => s.Rider)
            .Where(s => new DateOnly(s.Year, s.Month, 1) >= new DateOnly(from.Year, from.Month, 1)
                && new DateOnly(s.Year, s.Month, 1) <= new DateOnly(to.Year, to.Month, 1))
            .Where(s => s.Status != SalaryStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var result = centers.Select(c =>
        {
            var income = c.Type == CostCenterType.Company
                ? incomes.Where(i => i.CompanyId == c.CompanyId).Sum(i => i.NetAmount)
                : 0m;
            var expense = expenses.Where(e => e.CostCenterId == c.Id
                || (c.Type == CostCenterType.Rider && e.RiderId == c.RiderId)
                || (c.Type == CostCenterType.Housing && e.HousingId == c.HousingId)
                || (c.Type == CostCenterType.Vehicle && e.VehicleNumber == c.VehicleNumber)
                || (c.Type == CostCenterType.Company && e.CompanyId == c.CompanyId))
                .Sum(e => e.Amount + e.VatAmount);
            var riderSalary = c.Type == CostCenterType.Rider
                ? salaries.Where(s => s.RiderId == c.RiderId).Sum(s => s.NetSalary)
                : c.Type == CostCenterType.Company
                    ? salaries.Where(s => s.Rider.CompanyId == c.CompanyId).Sum(s => s.NetSalary)
                    : 0m;

            return new CostCenterFinanceResponse(c.Id, c.Code, c.Name, c.Type, income, expense, riderSalary, income - expense - riderSalary);
        }).ToList();

        return Result.Success(result);
    }

    private static CompanyExpenseResponse MapExpense(CompanyExpense expense)
        => new(
            expense.Id,
            expense.CompanyExpenseCategoryId,
            expense.Category.Name,
            expense.CompanyId,
            expense.Company?.Name,
            expense.CostCenterId,
            expense.RiderId,
            expense.HousingId,
            expense.VehicleNumber,
            expense.ExpenseDate,
            expense.Amount,
            expense.VatAmount,
            expense.Status,
            expense.ReferenceNumber,
            expense.Description);

    private static CompanyPaymentReceiptResponse MapReceipt(CompanyPaymentReceipt receipt)
        => new(
            receipt.Id,
            receipt.CompanyReceivableId,
            receipt.CompanyId,
            receipt.ReceiptDate,
            receipt.Amount,
            receipt.ReferenceNumber,
            receipt.BankAccount,
            receipt.Notes);
}

public class RiderAccountingProfileService(ApplicationDbcontext db) : IRiderAccountingProfileService
{
    private readonly ApplicationDbcontext _db = db;

    public async Task<Result<RiderAccountingProfileResponse>> GetRiderProfileAsync(
        int riderId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var rider = await _db.RiderDetails
            .Include(r => r.Employee)
            .ThenInclude(e => e.Housing)
            .Include(r => r.Company)
            .FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken);

        if (rider is null)
            return Result.Failure<RiderAccountingProfileResponse>(AccountingErrors.NotFound("Rider"));

        var monthStart = new DateOnly(from.Year, from.Month, 1);
        var monthEnd = new DateOnly(to.Year, to.Month, 1);

        var earnings = await _db.RiderEarnings
            .Where(e => e.PaidRiderId == riderId)
            .Where(e => new DateOnly(e.Year, e.Month, 1) >= monthStart && new DateOnly(e.Year, e.Month, 1) <= monthEnd)
            .ToListAsync(cancellationToken);

        var salaries = await _db.RiderMonthlySalaries
            .Include(s => s.Lines)
            .Where(s => s.RiderId == riderId)
            .Where(s => new DateOnly(s.Year, s.Month, 1) >= monthStart && new DateOnly(s.Year, s.Month, 1) <= monthEnd)
            .ToListAsync(cancellationToken);

        var items = await _db.RiderFinancialItems
            .Include(i => i.Type)
            .Where(i => i.RiderId == riderId && i.OccurredOn >= from && i.OccurredOn <= to)
            .ToListAsync(cancellationToken);

        var payments = await _db.RiderSalaryPayments
            .Include(p => p.Batch)
            .Where(p => p.RiderId == riderId)
            .Where(p => new DateOnly(p.Batch.Year, p.Batch.Month, 1) >= monthStart && new DateOnly(p.Batch.Year, p.Batch.Month, 1) <= monthEnd)
            .Where(p => p.Status == PaymentBatchStatus.Confirmed)
            .ToListAsync(cancellationToken);

        var cashPayments = await _db.CashSalaryHandoverLines
            .Include(l => l.Batch)
            .Where(l => l.RiderId == riderId)
            .Where(l => new DateOnly(l.Batch.Year, l.Batch.Month, 1) >= monthStart && new DateOnly(l.Batch.Year, l.Batch.Month, 1) <= monthEnd)
            .Where(l => l.Status == CashHandoverLineStatus.Delivered)
            .ToListAsync(cancellationToken);

        var periodKeys = earnings.Select(e => (e.Year, e.Month))
            .Concat(salaries.Select(s => (s.Year, s.Month)))
            .Distinct()
            .OrderBy(k => k.Year)
            .ThenBy(k => k.Month)
            .ToList();

        var periods = periodKeys.Select(k =>
        {
            var periodEarnings = earnings.Where(e => e.Year == k.Year && e.Month == k.Month).ToList();
            var salary = salaries.FirstOrDefault(s => s.Year == k.Year && s.Month == k.Month);
            return new RiderAccountingPeriodSummary(
                k.Year,
                k.Month,
                periodEarnings.Sum(e => e.AcceptedOrders),
                periodEarnings.Sum(e => e.RejectedOrders),
                periodEarnings.Sum(e => e.GrossAmount),
                salary?.GrossEarnings ?? 0,
                salary?.TotalBonuses ?? 0,
                salary?.TotalAllowances ?? 0,
                salary?.TotalDeductions ?? 0,
                salary?.NetSalary ?? 0,
                salary?.PaidAmount ?? 0,
                salary?.RemainingAmount ?? 0);
        }).ToList();

        var statement = new List<RiderStatementLineResponse>();
        foreach (var earning in earnings)
        {
            statement.Add(new RiderStatementLineResponse(
                new DateOnly(earning.Year, earning.Month, 1),
                earning.Year,
                earning.Month,
                "CompanyEarning",
                earning.SourceType,
                0,
                earning.GrossAmount,
                0,
                earning.SourceType,
                earning.Id,
                earning.Notes));
        }

        foreach (var item in items)
        {
            var isDeduction = item.Type.Category == FinancialItemCategory.Deduction;
            statement.Add(new RiderStatementLineResponse(
                item.OccurredOn,
                item.Year,
                item.Month,
                item.Type.Code,
                item.Type.Name,
                isDeduction ? Math.Abs(item.Amount) : 0,
                isDeduction ? 0 : Math.Abs(item.Amount),
                0,
                "RiderFinancialItem",
                item.Id,
                item.Notes));
        }

        foreach (var salary in salaries)
        {
            statement.Add(new RiderStatementLineResponse(
                new DateOnly(salary.Year, salary.Month, DateTime.DaysInMonth(salary.Year, salary.Month)),
                salary.Year,
                salary.Month,
                "Salary",
                "Monthly salary",
                0,
                salary.NetSalary,
                0,
                "RiderMonthlySalary",
                salary.Id,
                salary.Notes));
        }

        foreach (var payment in payments)
        {
            statement.Add(new RiderStatementLineResponse(
                new DateOnly(payment.Batch.Year, payment.Batch.Month, DateTime.DaysInMonth(payment.Batch.Year, payment.Batch.Month)),
                payment.Batch.Year,
                payment.Batch.Month,
                "BankPayment",
                "Bank transfer payment",
                payment.Amount,
                0,
                0,
                "RiderSalaryPayment",
                payment.Id,
                payment.Notes));
        }

        foreach (var payment in cashPayments)
        {
            statement.Add(new RiderStatementLineResponse(
                new DateOnly(payment.Batch.Year, payment.Batch.Month, DateTime.DaysInMonth(payment.Batch.Year, payment.Batch.Month)),
                payment.Batch.Year,
                payment.Batch.Month,
                "CashPayment",
                "Cash payment",
                payment.Amount,
                0,
                0,
                "CashSalaryHandoverLine",
                payment.Id,
                payment.MemberNotes));
        }

        var running = 0m;
        statement = statement
            .OrderBy(s => s.Date)
            .ThenBy(s => s.Type)
            .Select(s =>
            {
                running += s.Credit - s.Debit;
                return s with { Balance = running };
            })
            .ToList();

        return Result.Success(new RiderAccountingProfileResponse(
            rider.Id,
            rider.WorkingId,
            rider.Employee?.NameEN ?? rider.Employee?.NameAR ?? string.Empty,
            rider.EmployeeIqamaNo,
            rider.Company?.Name,
            rider.Employee?.Housing?.Name,
            rider.VehicleNumber,
            from,
            to,
            earnings.Sum(e => e.GrossAmount),
            salaries.Sum(s => s.NetSalary),
            salaries.Sum(s => s.PaidAmount),
            salaries.Sum(s => s.RemainingAmount),
            periods,
            statement));
    }
}

public class AccountingReportService(ApplicationDbcontext db) : IAccountingReportService
{
    private readonly ApplicationDbcontext _db = db;

    public async Task<Result<TrialBalanceResponse>> GetTrialBalanceAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var lines = await _db.JournalEntryLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to)
            .Where(l => l.JournalEntry.Status == AccountingRecordStatus.Posted)
            .GroupBy(l => new { l.AccountId, l.Account.Code, l.Account.Name, l.Account.Type })
            .Select(g => new TrialBalanceLineResponse(
                g.Key.AccountId,
                g.Key.Code,
                g.Key.Name,
                g.Key.Type,
                g.Sum(l => l.Debit),
                g.Sum(l => l.Credit),
                g.Sum(l => l.Debit - l.Credit)))
            .OrderBy(l => l.AccountCode)
            .ToListAsync(cancellationToken);

        return Result.Success(new TrialBalanceResponse(
            from,
            to,
            lines,
            lines.Sum(l => l.Debit),
            lines.Sum(l => l.Credit)));
    }

    public async Task<Result<GeneralLedgerResponse>> GetGeneralLedgerAsync(
        DateOnly from,
        DateOnly to,
        int? accountId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.JournalEntryLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to)
            .Where(l => l.JournalEntry.Status == AccountingRecordStatus.Posted)
            .Where(l => accountId == null || l.AccountId == accountId)
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ThenBy(l => l.JournalEntry.EntryNumber)
            .ToListAsync(cancellationToken);

        var runningByAccount = new Dictionary<int, decimal>();
        var lines = rows.Select(row =>
        {
            var current = runningByAccount.GetValueOrDefault(row.AccountId);
            current += row.Debit - row.Credit;
            runningByAccount[row.AccountId] = current;

            return new GeneralLedgerLineResponse(
                row.JournalEntry.EntryDate,
                row.JournalEntry.EntryNumber,
                row.AccountId,
                row.Account.Code,
                row.Account.Name,
                row.Debit,
                row.Credit,
                current,
                row.JournalEntry.Description,
                row.JournalEntry.SourceType,
                row.JournalEntry.SourceId);
        }).ToList();

        return Result.Success(new GeneralLedgerResponse(from, to, lines));
    }
}
