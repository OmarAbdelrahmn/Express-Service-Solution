using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Ledger;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Service.AccountingPosting;

public class AccountingPostingService(ApplicationDbcontext dbcontext, IFinancialAccessService financialAccessService) : IAccountingPostingService
{
    public Task<Result<FinancialDocumentResponse>> PostAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default) =>
        PostCoreAsync(request, actorId, false, cancellationToken);

    public Task<Result<FinancialDocumentResponse>> PostAfterScopeValidationAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default) =>
        PostCoreAsync(request, actorId, true, cancellationToken);

    private async Task<Result<FinancialDocumentResponse>> PostCoreAsync(PostSourceDocumentRequest request, string actorId, bool scopeAlreadyValidated, CancellationToken cancellationToken)
    {
        if (!scopeAlreadyValidated)
        {
            var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Post, cancellationToken);
            if (access.IsFailure) return Result.Failure<FinancialDocumentResponse>(access.Error);
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);

        var requestHash = CanonicalHash(request);
        var documentType = request.DocumentType.Trim();
        var idempotencyKey = request.IdempotencyKey.Trim();
        var existing = await dbcontext.FinancialDocuments.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.LegalEntityId == request.LegalEntityId && x.DocumentType == documentType && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
            return string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
                ? Result.Success(ToResponse(existing))
                : Result.Failure<FinancialDocumentResponse>(LedgerErrors.IdempotencyConflict);
        if (request.Events.Count == 0 || request.Events.Any(x => x.Amount <= 0) || string.IsNullOrWhiteSpace(request.CorrelationId))
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);

        var entity = await dbcontext.LegalEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.LegalEntityId && x.IsActive, cancellationToken);
        if (entity is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.LegalEntityNotFound);
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (!await dbcontext.Currencies.AnyAsync(x => x.Code == currency && x.IsActive, cancellationToken)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.ExchangeRateMissing);

        ExchangeRate? exchangeRateRecord = null;
        var exchangeRate = 1m;
        if (!string.Equals(currency, entity.BaseCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            exchangeRateRecord = await dbcontext.ExchangeRates.AsNoTracking().Where(x => x.LegalEntityId == request.LegalEntityId && x.FromCurrencyCode == currency && x.ToCurrencyCode == entity.BaseCurrencyCode && x.EffectiveDate <= request.TransactionDate).OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(cancellationToken);
            if (exchangeRateRecord is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.ExchangeRateMissing);
            exchangeRate = exchangeRateRecord.Rate;
        }

        var period = await dbcontext.FiscalPeriods.Include(x => x.FiscalYear).SingleOrDefaultAsync(x => x.FiscalYear.LegalEntityId == request.LegalEntityId && x.StartDate <= request.TransactionDate && x.EndDate >= request.TransactionDate, cancellationToken);
        if (period is null || period.Status != FiscalPeriodStatus.Open || (request.Module == AccountingModule.Payroll && period.PayrollLocked) || (request.Module == AccountingModule.Tax && period.TaxLocked))
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.PeriodLocked);

        var profileCode = request.PostingProfileCode.Trim().ToUpperInvariant();
        var profile = await dbcontext.PostingProfiles.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.LegalEntityId == request.LegalEntityId && x.Code == profileCode && x.IsActive && x.EffectiveFrom <= request.TransactionDate && (x.EffectiveTo == null || x.EffectiveTo >= request.TransactionDate), cancellationToken);
        if (profile is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.MissingPostingRoute);
        var routes = profile.Lines.ToDictionary(x => x.EventCode, StringComparer.OrdinalIgnoreCase);
        if (request.Events.Any(x => !routes.ContainsKey(x.EventCode.Trim()))) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.MissingPostingRoute);

        var accountIds = request.Events.SelectMany(x => new[] { routes[x.EventCode.Trim()].DebitAccountId, routes[x.EventCode.Trim()].CreditAccountId }).Distinct().ToArray();
        if (await dbcontext.AccountingAccounts.CountAsync(x => x.LegalEntityId == request.LegalEntityId && x.IsActive && accountIds.Contains(x.Id), cancellationToken) != accountIds.Length)
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.AccountNotFound);
        var dimensionValidation = await ValidateDimensionsAsync(request, cancellationToken);
        if (dimensionValidation.IsFailure) return Result.Failure<FinancialDocumentResponse>(dimensionValidation.Error);

        IDbContextTransaction? ownedTransaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            ownedTransaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            await AcquireLockAsync($"Accounting:DocumentSequence:{request.LegalEntityId}:{documentType}", cancellationToken);
            await AcquireLockAsync($"Accounting:AuditChain:{request.LegalEntityId}", cancellationToken);
            var number = await NextDocumentNumberAsync(request.LegalEntityId, documentType, cancellationToken);
            var rounding = new List<object>();
            var lineNumber = 0;
            var documentLines = new List<FinancialDocumentLine>();
            foreach (var item in request.Events.OrderBy(x => x.EventCode, StringComparer.OrdinalIgnoreCase))
            {
                var route = routes[item.EventCode.Trim()];
                var transactionAmount = decimal.Round(item.Amount, 4, MidpointRounding.AwayFromZero);
                var unroundedBase = transactionAmount * exchangeRate;
                var baseAmount = decimal.Round(unroundedBase, 4, MidpointRounding.AwayFromZero);
                rounding.Add(new { item.EventCode, TransactionAmount = transactionAmount, ExchangeRate = exchangeRate, UnroundedBase = unroundedBase, BaseAmount = baseAmount });
                var dimensions = (item.DimensionValueIds ?? []).Distinct().Select(x => new FinancialDocumentLineDimension { FinancialDimensionValueId = x }).ToList();
                documentLines.Add(new FinancialDocumentLine { LineNumber = ++lineNumber, AccountId = route.DebitAccountId, Description = item.Description, Debit = transactionAmount, BaseDebit = baseAmount, Dimensions = dimensions.Select(x => new FinancialDocumentLineDimension { FinancialDimensionValueId = x.FinancialDimensionValueId }).ToList() });
                documentLines.Add(new FinancialDocumentLine { LineNumber = ++lineNumber, AccountId = route.CreditAccountId, Description = item.Description, Credit = transactionAmount, BaseCredit = baseAmount, Dimensions = dimensions });
            }

            var document = new FinancialDocument
            {
                LegalEntityId = request.LegalEntityId, BranchId = request.BranchId, DocumentType = documentType, DocumentNumber = number,
                IdempotencyKey = idempotencyKey, RequestHash = requestHash, CorrelationId = request.CorrelationId.Trim(), SourceReference = request.SourceReference.Trim(),
                PostingProfileCode = profileCode, Description = request.Description.Trim(), TransactionDate = request.TransactionDate, CurrencyCode = currency,
                BaseCurrencyCode = entity.BaseCurrencyCode, ExchangeRate = exchangeRate, ExchangeRateId = exchangeRateRecord?.Id,
                RoundingTraceJson = JsonSerializer.Serialize(rounding), Status = FinancialDocumentStatus.Approved, CreatedBy = actorId, SubmittedBy = actorId,
                SubmittedAt = DateTime.UtcNow, ApprovedBy = actorId, ApprovedAt = DateTime.UtcNow, Lines = documentLines,
                Approvals = [new DocumentApproval { ApprovedBy = actorId, Comment = "Source operation auto-approved by authorized Accountant." }]
            };
            var batch = new PostingBatch { LegalEntityId = request.LegalEntityId, FinancialDocument = document, PostingKey = $"{documentType}:{document.Id:N}", PostedBy = actorId };
            var entry = new JournalEntry
            {
                PostingBatch = batch, LegalEntityId = request.LegalEntityId, FiscalPeriodId = period.Id, EntryNumber = $"JE-{number}", PostingDate = request.TransactionDate,
                Description = request.Description.Trim(), IsFinalized = false,
                Lines = documentLines.Select(x => new JournalLine
                {
                    LineNumber = x.LineNumber, AccountId = x.AccountId, Description = x.Description, Debit = x.Debit, Credit = x.Credit, BaseDebit = x.BaseDebit, BaseCredit = x.BaseCredit,
                    Dimensions = x.Dimensions.Select(d => new JournalLineDimension { FinancialDimensionValueId = d.FinancialDimensionValueId }).ToList()
                }).ToList()
            };
            dbcontext.JournalEntries.Add(entry);
            var audit = await AppendAuditAsync(document, actorId, request.CorrelationId, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            entry.IsFinalized = true;
            document.Status = FinancialDocumentStatus.Posted;
            document.PostedBy = actorId;
            document.PostedAt = DateTime.UtcNow;
            var head = await dbcontext.AccountingAuditChainHeads.SingleAsync(x => x.LegalEntityId == request.LegalEntityId, cancellationToken);
            head.LastEventId = audit.Id;
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(document));
        }
        catch (DbUpdateException)
        {
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
            // A caller may own a wider source-operation transaction. Clearing its
            // tracker here would silently detach the subledger changes it still has
            // to roll back or inspect. We only own (and may clear) our own unit of work.
            if (ownedTransaction is not null) dbcontext.ChangeTracker.Clear();
            var raced = await dbcontext.FinancialDocuments.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.LegalEntityId == request.LegalEntityId && x.DocumentType == documentType && x.IdempotencyKey == idempotencyKey, cancellationToken);
            return raced is not null && raced.RequestHash == requestHash ? Result.Success(ToResponse(raced)) : Result.Failure<FinancialDocumentResponse>(LedgerErrors.IdempotencyConflict);
        }
        finally
        {
            if (ownedTransaction is not null) await ownedTransaction.DisposeAsync();
        }
    }

    public async Task<Result<FinancialDocumentResponse>> ReverseAsync(ReverseSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);

        var original = await dbcontext.FinancialDocuments
            .Include(x => x.Lines).ThenInclude(x => x.Dimensions)
            .SingleOrDefaultAsync(x => x.Id == request.FinancialDocumentId, cancellationToken);
        if (original is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.DocumentNotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, original.LegalEntityId, FinancialPermission.Post, cancellationToken);
        if (access.IsFailure) return Result.Failure<FinancialDocumentResponse>(access.Error);

        var documentType = ReversalDocumentType(original.DocumentType);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var requestHash = CanonicalHash(request, original);
        var existing = await dbcontext.FinancialDocuments.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.LegalEntityId == original.LegalEntityId && x.DocumentType == documentType && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
            return existing.RequestHash == requestHash ? Result.Success(ToResponse(existing)) : Result.Failure<FinancialDocumentResponse>(LedgerErrors.IdempotencyConflict);
        if (string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.CorrelationId))
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        if (original.Status == FinancialDocumentStatus.Reversed || original.ReversedByDocumentId.HasValue)
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.ReversalExists);
        if (original.Status != FinancialDocumentStatus.Posted) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidTransition);

        var period = await dbcontext.FiscalPeriods.Include(x => x.FiscalYear)
            .SingleOrDefaultAsync(x => x.FiscalYear.LegalEntityId == original.LegalEntityId && x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate, cancellationToken);
        if (period is null || period.Status != FiscalPeriodStatus.Open || (request.Module == AccountingModule.Payroll && period.PayrollLocked) || (request.Module == AccountingModule.Tax && period.TaxLocked))
            return Result.Failure<FinancialDocumentResponse>(LedgerErrors.PeriodLocked);

        IDbContextTransaction? ownedTransaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            ownedTransaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquireLockAsync($"Accounting:DocumentSequence:{original.LegalEntityId}:{documentType}", cancellationToken);
            await AcquireLockAsync($"Accounting:AuditChain:{original.LegalEntityId}", cancellationToken);
            var originalBatchId = await dbcontext.PostingBatches.Where(x => x.FinancialDocumentId == original.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
            if (!originalBatchId.HasValue) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidTransition);
            var number = await NextDocumentNumberAsync(original.LegalEntityId, documentType, cancellationToken);
            var now = DateTime.UtcNow;
            var reversalLines = original.Lines.OrderBy(x => x.LineNumber).Select(x => new FinancialDocumentLine
            {
                LineNumber = x.LineNumber,
                AccountId = x.AccountId,
                Description = $"Reversal: {x.Description}",
                Debit = x.Credit,
                Credit = x.Debit,
                BaseDebit = x.BaseCredit,
                BaseCredit = x.BaseDebit,
                Dimensions = x.Dimensions.Select(d => new FinancialDocumentLineDimension { FinancialDimensionValueId = d.FinancialDimensionValueId }).ToList()
            }).ToList();
            var reversal = new FinancialDocument
            {
                LegalEntityId = original.LegalEntityId, BranchId = original.BranchId, DocumentType = documentType, DocumentNumber = number,
                IdempotencyKey = idempotencyKey, RequestHash = requestHash, CorrelationId = request.CorrelationId.Trim(), SourceReference = original.DocumentNumber,
                PostingProfileCode = original.PostingProfileCode, Description = $"Reversal of {original.DocumentNumber}: {request.Reason.Trim()}", TransactionDate = request.ReversalDate,
                CurrencyCode = original.CurrencyCode, BaseCurrencyCode = original.BaseCurrencyCode, ExchangeRate = original.ExchangeRate, ExchangeRateId = original.ExchangeRateId,
                RoundingTraceJson = original.RoundingTraceJson, Status = FinancialDocumentStatus.Approved, CreatedBy = actorId, SubmittedBy = actorId, SubmittedAt = now,
                ApprovedBy = actorId, ApprovedAt = now, ReversalOfDocumentId = original.Id, ReversalReason = request.Reason.Trim(), Lines = reversalLines,
                Approvals = [new DocumentApproval { ApprovedBy = actorId, Comment = $"Module-owned reversal: {request.Reason.Trim()}" }]
            };
            var batch = new PostingBatch
            {
                LegalEntityId = original.LegalEntityId, FinancialDocument = reversal, PostingKey = $"{documentType}:{reversal.Id:N}",
                PostedBy = actorId, ReversalOfPostingBatchId = originalBatchId
            };
            var entry = new JournalEntry
            {
                PostingBatch = batch, LegalEntityId = original.LegalEntityId, FiscalPeriodId = period.Id, EntryNumber = $"JE-{number}", PostingDate = request.ReversalDate,
                Description = reversal.Description, IsFinalized = false,
                Lines = reversalLines.Select(x => new JournalLine
                {
                    LineNumber = x.LineNumber, AccountId = x.AccountId, Description = x.Description, Debit = x.Debit, Credit = x.Credit,
                    BaseDebit = x.BaseDebit, BaseCredit = x.BaseCredit,
                    Dimensions = x.Dimensions.Select(d => new JournalLineDimension { FinancialDimensionValueId = d.FinancialDimensionValueId }).ToList()
                }).ToList()
            };
            dbcontext.JournalEntries.Add(entry);
            var audit = await AppendAuditAsync(reversal, actorId, request.CorrelationId, cancellationToken, "Document.Reversed");
            await dbcontext.SaveChangesAsync(cancellationToken);
            entry.IsFinalized = true;
            reversal.Status = FinancialDocumentStatus.Posted;
            reversal.PostedBy = actorId;
            reversal.PostedAt = now;
            original.Status = FinancialDocumentStatus.Reversed;
            original.ReversedByDocumentId = reversal.Id;
            original.ReversalReason = request.Reason.Trim();
            var head = await dbcontext.AccountingAuditChainHeads.SingleAsync(x => x.LegalEntityId == original.LegalEntityId, cancellationToken);
            head.LastEventId = audit.Id;
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(reversal));
        }
        catch (DbUpdateException)
        {
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
            if (ownedTransaction is not null) dbcontext.ChangeTracker.Clear();
            var raced = await dbcontext.FinancialDocuments.AsNoTracking().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.LegalEntityId == original.LegalEntityId && x.DocumentType == documentType && x.IdempotencyKey == idempotencyKey, cancellationToken);
            return raced is not null && raced.RequestHash == requestHash ? Result.Success(ToResponse(raced)) : Result.Failure<FinancialDocumentResponse>(LedgerErrors.IdempotencyConflict);
        }
        finally
        {
            if (ownedTransaction is not null) await ownedTransaction.DisposeAsync();
        }
    }

    private async Task<Result> ValidateDimensionsAsync(PostSourceDocumentRequest request, CancellationToken ct)
    {
        var required = await dbcontext.FinancialDimensions.AsNoTracking().Where(x => x.LegalEntityId == request.LegalEntityId && x.IsActive && x.IsRequired).Select(x => x.Id).ToArrayAsync(ct);
        var allIds = request.Events.SelectMany(x => x.DimensionValueIds ?? []).Distinct().ToArray();
        var values = await dbcontext.FinancialDimensionValues.AsNoTracking().Where(x => allIds.Contains(x.Id) && x.IsActive && x.FinancialDimension.LegalEntityId == request.LegalEntityId).Select(x => new { x.Id, x.FinancialDimensionId }).ToListAsync(ct);
        if (values.Count != allIds.Length) return Result.Failure(LedgerErrors.RequiredDimensionMissing);
        foreach (var item in request.Events)
        {
            var selected = values.Where(x => (item.DimensionValueIds ?? []).Contains(x.Id)).GroupBy(x => x.FinancialDimensionId).ToDictionary(x => x.Key, x => x.Count());
            if (required.Any(x => !selected.TryGetValue(x, out var count) || count != 1) || selected.Values.Any(x => x > 1)) return Result.Failure(LedgerErrors.RequiredDimensionMissing);
        }
        return Result.Success();
    }

    private async Task AcquireLockAsync(string resource, CancellationToken ct)
    {
        if (!dbcontext.Database.IsSqlServer()) return;
        await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
    }

    private async Task<string> NextDocumentNumberAsync(int entityId, string documentType, CancellationToken ct)
    {
        var sequence = await dbcontext.LegalEntityDocumentSequences.SingleOrDefaultAsync(x => x.LegalEntityId == entityId && x.DocumentType == documentType, ct);
        if (sequence is null)
        {
            sequence = new LegalEntityDocumentSequence { LegalEntityId = entityId, DocumentType = documentType };
            dbcontext.LegalEntityDocumentSequences.Add(sequence);
        }
        var current = sequence.NextNumber++;
        var prefix = new string(documentType.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
        return $"{prefix}-{entityId:D5}-{current:D8}";
    }

    private async Task<AccountingAuditEvent> AppendAuditAsync(FinancialDocument document, string actorId, string correlationId, CancellationToken ct, string eventType = "Document.Posted")
    {
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == document.LegalEntityId, ct);
        if (head is null)
        {
            head = new AccountingAuditChainHead { LegalEntityId = document.LegalEntityId };
            dbcontext.AccountingAuditChainHeads.Add(head);
        }
        var payload = JsonSerializer.Serialize(new { document.Id, document.DocumentNumber, document.DocumentType, document.SourceReference, document.CurrencyCode, document.BaseCurrencyCode, document.ExchangeRate, document.ReversalOfDocumentId, document.ReversalReason });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{document.LegalEntityId}|{document.Id}|{eventType}|{actorId}|{payload}")));
        var audit = new AccountingAuditEvent { LegalEntityId = document.LegalEntityId, FinancialDocumentId = document.Id, EventType = eventType, ActorId = actorId, PayloadJson = payload, PreviousHash = head.LastHash, Hash = hash };
        dbcontext.AccountingAuditEvents.Add(audit);
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage { LegalEntityId = document.LegalEntityId, Type = eventType, PayloadJson = payload, CorrelationId = correlationId.Trim() });
        head.LastHash = hash;
        return audit;
    }

    private static string CanonicalHash(PostSourceDocumentRequest request)
    {
        var canonical = new
        {
            request.LegalEntityId, request.BranchId, request.TransactionDate, DocumentType = request.DocumentType.Trim(), SourceReference = request.SourceReference.Trim(),
            PostingProfileCode = request.PostingProfileCode.Trim().ToUpperInvariant(), Description = request.Description.Trim(), CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            CorrelationId = request.CorrelationId.Trim(), request.Module, request.IdempotencyPayload,
            Events = request.Events.OrderBy(x => x.EventCode, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Description).Select(x => new { EventCode = x.EventCode.Trim().ToUpperInvariant(), Amount = decimal.Round(x.Amount, 4), Description = x.Description?.Trim(), Dimensions = (x.DimensionValueIds ?? []).Distinct().OrderBy(id => id).ToArray() }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }

    private static string CanonicalHash(ReverseSourceDocumentRequest request, FinancialDocument original)
    {
        var canonical = new
        {
            request.FinancialDocumentId, request.ReversalDate, Reason = request.Reason.Trim(), CorrelationId = request.CorrelationId.Trim(), request.Module, request.IdempotencyPayload,
            OriginalDocumentNumber = original.DocumentNumber, OriginalRequestHash = original.RequestHash
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }

    private static string ReversalDocumentType(string documentType)
    {
        var value = $"{documentType}.Reversal";
        return value.Length <= 64 ? value : value[..64];
    }

    private static FinancialDocumentResponse ToResponse(FinancialDocument x) => new(x.Id, x.LegalEntityId, x.BranchId, x.DocumentType, x.DocumentNumber, x.SourceReference, x.Description, x.TransactionDate, x.Status, x.CreatedBy, x.SubmittedBy, x.ApprovedBy, x.PostedBy, x.ReversalOfDocumentId, x.ReversedByDocumentId, x.Lines.OrderBy(l => l.LineNumber).Select(l => new FinancialDocumentLineResponse(l.LineNumber, l.AccountId, l.Description, l.Debit, l.Credit)).ToArray(), x.CorrelationId, x.RequestHash);
}
