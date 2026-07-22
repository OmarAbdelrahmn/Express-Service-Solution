using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
using Application.Contracts.Ledger;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Ledger;

public class LedgerService(ApplicationDbcontext dbcontext, IFinancialAccessService financialAccessService) : ILedgerService
{
    public async Task<Result<IReadOnlyCollection<CurrencyResponse>>> GetCurrenciesAsync(bool? active, string? search, CancellationToken ct = default)
    {
        var query = dbcontext.Currencies.AsNoTracking();
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term));
        }
        var items = await query.OrderBy(x => x.Code).Select(x => new CurrencyResponse(x.Code, x.Name, x.DecimalPlaces, x.IsActive)).ToListAsync(ct);
        return Result.Success<IReadOnlyCollection<CurrencyResponse>>(items);
    }

    public async Task<Result<PagedResponse<ExchangeRateResponse>>> GetExchangeRatesAsync(int legalEntityId, PaginationRequest pagination, string? fromCurrencyCode, string? toCurrencyCode, DateOnly? fromDate, DateOnly? toDate, string? sortBy, string? sortDirection, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<PagedResponse<ExchangeRateResponse>>(access.Error);
        if (fromDate > toDate || !IsValidSortDirection(sortDirection)) return Result.Failure<PagedResponse<ExchangeRateResponse>>(LedgerErrors.InvalidQuery);
        var query = dbcontext.ExchangeRates.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId);
        if (!string.IsNullOrWhiteSpace(fromCurrencyCode)) { var code = Code(fromCurrencyCode); query = query.Where(x => x.FromCurrencyCode == code); }
        if (!string.IsNullOrWhiteSpace(toCurrencyCode)) { var code = Code(toCurrencyCode); query = query.Where(x => x.ToCurrencyCode == code); }
        if (fromDate.HasValue) query = query.Where(x => x.EffectiveDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.EffectiveDate <= toDate.Value);
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize;
        var total = await query.CountAsync(ct);
        var ordered = OrderExchangeRates(query, sortBy, sortDirection);
        var items = await ordered
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(x => new ExchangeRateResponse(x.Id, x.LegalEntityId, x.FromCurrencyCode, x.ToCurrencyCode, x.EffectiveDate, x.Rate)).ToListAsync(ct);
        return Result.Success(new PagedResponse<ExchangeRateResponse>(items, pageNumber, pageSize, total));
    }

    public async Task<Result<IReadOnlyCollection<FinancialDimensionResponse>>> GetDimensionsAsync(int legalEntityId, bool? active, string? search, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<IReadOnlyCollection<FinancialDimensionResponse>>(access.Error);
        var query = dbcontext.FinancialDimensions.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term)); }
        var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Select(x => new FinancialDimensionResponse(x.Id, x.LegalEntityId, x.Code, x.Name, x.IsRequired, x.IsActive)).ToListAsync(ct);
        return Result.Success<IReadOnlyCollection<FinancialDimensionResponse>>(items);
    }

    public async Task<Result<IReadOnlyCollection<FinancialDimensionValueResponse>>> GetDimensionValuesAsync(int financialDimensionId, bool? active, string? search, string actorId, CancellationToken ct = default)
    {
        var legalEntityId = await dbcontext.FinancialDimensions.AsNoTracking().Where(x => x.Id == financialDimensionId).Select(x => (int?)x.LegalEntityId).SingleOrDefaultAsync(ct);
        if (!legalEntityId.HasValue) return Result.Failure<IReadOnlyCollection<FinancialDimensionValueResponse>>(LedgerErrors.DimensionNotFound);
        var access = await RequireAsync(actorId, legalEntityId.Value, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<IReadOnlyCollection<FinancialDimensionValueResponse>>(access.Error);
        var query = dbcontext.FinancialDimensionValues.AsNoTracking().Where(x => x.FinancialDimensionId == financialDimensionId);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term)); }
        var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Select(x => new FinancialDimensionValueResponse(x.Id, x.FinancialDimensionId, x.Code, x.Name, x.IsActive)).ToListAsync(ct);
        return Result.Success<IReadOnlyCollection<FinancialDimensionValueResponse>>(items);
    }

    public async Task<Result<PagedResponse<PostingProfileResponse>>> GetPostingProfilesAsync(int legalEntityId, PaginationRequest pagination, bool? active, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<PagedResponse<PostingProfileResponse>>(access.Error);
        if (fromDate > toDate || !IsValidSortDirection(sortDirection)) return Result.Failure<PagedResponse<PostingProfileResponse>>(LedgerErrors.InvalidQuery);
        var query = dbcontext.PostingProfiles.AsNoTracking().Include(x => x.Lines).Where(x => x.LegalEntityId == legalEntityId);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (fromDate.HasValue) query = query.Where(x => x.EffectiveTo == null || x.EffectiveTo >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.EffectiveFrom <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term)); }
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize; var total = await query.CountAsync(ct);
        var records = await OrderPostingProfiles(query, sortBy, sortDirection).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result.Success(new PagedResponse<PostingProfileResponse>(records.Select(ToResponse).ToList(), pageNumber, pageSize, total));
    }

    public async Task<Result<PostingProfileResponse>> GetPostingProfileAsync(int id, string actorId, CancellationToken ct = default)
    {
        var profile = await dbcontext.PostingProfiles.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (profile is null) return Result.Failure<PostingProfileResponse>(LedgerErrors.PostingProfileNotFound);
        var access = await RequireAsync(actorId, profile.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<PostingProfileResponse>(access.Error) : Result.Success(ToResponse(profile));
    }

    public async Task<Result<PagedResponse<FiscalYearResponse>>> GetFiscalYearsAsync(int legalEntityId, PaginationRequest pagination, string? sortBy, string? sortDirection, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<PagedResponse<FiscalYearResponse>>(access.Error);
        if (!IsValidSortDirection(sortDirection)) return Result.Failure<PagedResponse<FiscalYearResponse>>(LedgerErrors.InvalidQuery);
        var query = dbcontext.FiscalYears.AsNoTracking().Include(x => x.Periods).Where(x => x.LegalEntityId == legalEntityId);
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize; var total = await query.CountAsync(ct);
        var records = await OrderFiscalYears(query, sortBy, sortDirection).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result.Success(new PagedResponse<FiscalYearResponse>(records.Select(ToResponse).ToList(), pageNumber, pageSize, total));
    }

    public async Task<Result<PagedResponse<RecurringJournalScheduleResponse>>> GetRecurringSchedulesAsync(int legalEntityId, PaginationRequest pagination, bool? active, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<PagedResponse<RecurringJournalScheduleResponse>>(access.Error);
        if (fromDate > toDate || !IsValidSortDirection(sortDirection)) return Result.Failure<PagedResponse<RecurringJournalScheduleResponse>>(LedgerErrors.InvalidQuery);
        var query = dbcontext.RecurringJournalSchedules.AsNoTracking().Include(x => x.Lines).Where(x => x.LegalEntityId == legalEntityId);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (fromDate.HasValue) query = query.Where(x => x.NextRunDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.NextRunDate <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => x.DocumentType.ToLower().Contains(term) || x.Description.ToLower().Contains(term)); }
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize; var total = await query.CountAsync(ct);
        var records = await OrderRecurringSchedules(query, sortBy, sortDirection).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result.Success(new PagedResponse<RecurringJournalScheduleResponse>(records.Select(ToResponse).ToList(), pageNumber, pageSize, total));
    }

    public async Task<Result<RecurringJournalScheduleResponse>> GetRecurringScheduleAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var schedule = await dbcontext.RecurringJournalSchedules.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (schedule is null) return Result.Failure<RecurringJournalScheduleResponse>(LedgerErrors.RecurringScheduleNotFound);
        var access = await RequireAsync(actorId, schedule.LegalEntityId, FinancialPermission.View, ct);
        return access.IsFailure ? Result.Failure<RecurringJournalScheduleResponse>(access.Error) : Result.Success(ToResponse(schedule));
    }

    public async Task<Result<PagedResponse<FinancialDocumentResponse>>> GetDocumentsAsync(int legalEntityId, PaginationRequest pagination, FinancialDocumentStatus? status, string? documentType, DateOnly? fromDate, DateOnly? toDate, string? search, string? reference, string? sortBy, string? sortDirection, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<PagedResponse<FinancialDocumentResponse>>(access.Error);
        if (fromDate > toDate || !IsValidSortDirection(sortDirection)) return Result.Failure<PagedResponse<FinancialDocumentResponse>>(LedgerErrors.InvalidQuery);
        var query = dbcontext.FinancialDocuments.AsNoTracking().Include(x => x.Lines).Where(x => x.LegalEntityId == legalEntityId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(documentType)) { var value = documentType.Trim().ToLower(); query = query.Where(x => x.DocumentType.ToLower() == value); }
        if (fromDate.HasValue) query = query.Where(x => x.TransactionDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.TransactionDate <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(reference)) { var value = reference.Trim().ToLower(); query = query.Where(x => (x.SourceReference != null && x.SourceReference.ToLower().Contains(value)) || x.DocumentNumber.ToLower().Contains(value)); }
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim().ToLower(); query = query.Where(x => x.DocumentNumber.ToLower().Contains(value) || x.DocumentType.ToLower().Contains(value) || x.Description.ToLower().Contains(value) || (x.SourceReference != null && x.SourceReference.ToLower().Contains(value))); }
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize; var total = await query.CountAsync(ct);
        var records = await OrderDocuments(query, sortBy, sortDirection).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result.Success(new PagedResponse<FinancialDocumentResponse>(records.Select(ToResponse).ToList(), pageNumber, pageSize, total));
    }

    public async Task<Result<PagedResponse<JournalEntryResponse>>> GetJournalEntriesAsync(int legalEntityId, PaginationRequest pagination, DateOnly? fromDate, DateOnly? toDate, int? accountId, Guid? documentId, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<PagedResponse<JournalEntryResponse>>(access.Error);
        if (fromDate > toDate || !IsValidSortDirection(sortDirection)) return Result.Failure<PagedResponse<JournalEntryResponse>>(LedgerErrors.InvalidQuery);
        var query = dbcontext.JournalEntries.AsNoTracking().Include(x => x.Lines).Include(x => x.PostingBatch).ThenInclude(x => x.FinancialDocument).Where(x => x.LegalEntityId == legalEntityId && x.IsFinalized);
        if (fromDate.HasValue) query = query.Where(x => x.PostingDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.PostingDate <= toDate.Value);
        if (accountId.HasValue) query = query.Where(x => x.Lines.Any(l => l.AccountId == accountId.Value));
        if (documentId.HasValue) query = query.Where(x => x.PostingBatch.FinancialDocumentId == documentId.Value);
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim().ToLower(); query = query.Where(x => x.EntryNumber.ToLower().Contains(value) || x.Description.ToLower().Contains(value) || x.PostingBatch.FinancialDocument.DocumentNumber.ToLower().Contains(value) || (x.PostingBatch.FinancialDocument.SourceReference != null && x.PostingBatch.FinancialDocument.SourceReference.ToLower().Contains(value))); }
        var pageNumber = pagination.NormalizedPageNumber; var pageSize = pagination.NormalizedPageSize; var total = await query.CountAsync(ct);
        var records = await OrderJournalEntries(query, sortBy, sortDirection).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result.Success(new PagedResponse<JournalEntryResponse>(records.Select(ToResponse).ToList(), pageNumber, pageSize, total));
    }

    public async Task<Result<CurrencyResponse>> CreateCurrencyAsync(CreateCurrencyRequest r, string actorId, CancellationToken ct = default)
    {
        var code = Code(r.Code); if (r.DecimalPlaces is < 0 or > 8 || await dbcontext.Currencies.AnyAsync(x => x.Code == code, ct)) return Result.Failure<CurrencyResponse>(LedgerErrors.DuplicateCode);
        var currency = new Currency { Code = code, Name = r.Name.Trim(), DecimalPlaces = r.DecimalPlaces }; dbcontext.Currencies.Add(currency); await dbcontext.SaveChangesAsync(ct); return Result.Success(new CurrencyResponse(currency.Code, currency.Name, currency.DecimalPlaces, currency.IsActive));
    }
    public async Task<Result<ExchangeRateResponse>> CreateExchangeRateAsync(CreateExchangeRateRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<ExchangeRateResponse>(access.Error);
        var from = Code(r.FromCurrencyCode); var to = Code(r.ToCurrencyCode); if (r.Rate <= 0 || from == to || !await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct) || await dbcontext.Currencies.CountAsync(x => x.IsActive && (x.Code == from || x.Code == to), ct) != 2) return Result.Failure<ExchangeRateResponse>(LedgerErrors.InvalidPeriod);
        if (await dbcontext.ExchangeRates.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.FromCurrencyCode == from && x.ToCurrencyCode == to && x.EffectiveDate == r.EffectiveDate, ct)) return Result.Failure<ExchangeRateResponse>(LedgerErrors.DuplicateCode);
        var rate = new ExchangeRate { LegalEntityId = r.LegalEntityId, FromCurrencyCode = from, ToCurrencyCode = to, EffectiveDate = r.EffectiveDate, Rate = r.Rate, CreatedBy = actorId }; dbcontext.ExchangeRates.Add(rate); await AuditAsync(r.LegalEntityId, null, "Currency.ExchangeRateCreated", actorId, new { from, to, r.EffectiveDate, r.Rate }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(new ExchangeRateResponse(rate.Id, rate.LegalEntityId, from, to, rate.EffectiveDate, rate.Rate));
    }
    public async Task<Result<FinancialDimensionResponse>> CreateDimensionAsync(CreateFinancialDimensionRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<FinancialDimensionResponse>(access.Error);
        var code = Code(r.Code); if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<FinancialDimensionResponse>(LedgerErrors.LegalEntityNotFound); if (await dbcontext.FinancialDimensions.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Code == code, ct)) return Result.Failure<FinancialDimensionResponse>(LedgerErrors.DuplicateCode);
        var dimension = new FinancialDimension { LegalEntityId = r.LegalEntityId, Code = code, Name = r.Name.Trim(), IsRequired = r.IsRequired }; dbcontext.FinancialDimensions.Add(dimension); await AuditAsync(r.LegalEntityId, null, "Dimension.Created", actorId, new { code }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(new FinancialDimensionResponse(dimension.Id, dimension.LegalEntityId, code, dimension.Name, dimension.IsRequired, dimension.IsActive));
    }
    public async Task<Result<FinancialDimensionValueResponse>> CreateDimensionValueAsync(CreateFinancialDimensionValueRequest r, string actorId, CancellationToken ct = default)
    {
        var dimension = await dbcontext.FinancialDimensions.SingleOrDefaultAsync(x => x.Id == r.FinancialDimensionId, ct); if (dimension is null) return Result.Failure<FinancialDimensionValueResponse>(LedgerErrors.InvalidPeriod); var access = await RequireAsync(actorId, dimension.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<FinancialDimensionValueResponse>(access.Error); var code = Code(r.Code); if (await dbcontext.FinancialDimensionValues.AnyAsync(x => x.FinancialDimensionId == r.FinancialDimensionId && x.Code == code, ct)) return Result.Failure<FinancialDimensionValueResponse>(LedgerErrors.DuplicateCode);
        var value = new FinancialDimensionValue { FinancialDimensionId = r.FinancialDimensionId, Code = code, Name = r.Name.Trim() }; dbcontext.FinancialDimensionValues.Add(value); await AuditAsync(dimension.LegalEntityId, null, "Dimension.ValueCreated", actorId, new { dimension.Code, code }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(new FinancialDimensionValueResponse(value.Id, value.FinancialDimensionId, code, value.Name, value.IsActive));
    }
    public async Task<Result<RecurringJournalScheduleResponse>> CreateRecurringScheduleAsync(CreateRecurringJournalScheduleRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<RecurringJournalScheduleResponse>(access.Error);
        if (!IsBalanced(r.Lines) || r.FrequencyMonths is < 1 or > 12 || (r.EndDate is not null && r.EndDate < r.NextRunDate)) return Result.Failure<RecurringJournalScheduleResponse>(LedgerErrors.InvalidJournal); if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<RecurringJournalScheduleResponse>(LedgerErrors.LegalEntityNotFound);
        var accounts = r.Lines.Select(x => x.AccountId).Distinct().ToArray(); if (await dbcontext.AccountingAccounts.CountAsync(x => x.LegalEntityId == r.LegalEntityId && x.IsActive && x.AllowManualPosting && accounts.Contains(x.Id), ct) != accounts.Length) return Result.Failure<RecurringJournalScheduleResponse>(LedgerErrors.AccountNotPostable);
        var schedule = new RecurringJournalSchedule { LegalEntityId = r.LegalEntityId, BranchId = r.BranchId, DocumentType = r.DocumentType.Trim(), Description = r.Description.Trim(), CurrencyCode = Code(r.CurrencyCode), FrequencyMonths = r.FrequencyMonths, NextRunDate = r.NextRunDate, EndDate = r.EndDate, CreatedBy = actorId, Lines = r.Lines.Select((x, i) => new RecurringJournalScheduleLine { LineNumber = i + 1, AccountId = x.AccountId, Description = Trim(x.Description), Debit = x.Debit, Credit = x.Credit }).ToList() }; dbcontext.RecurringJournalSchedules.Add(schedule); await AuditAsync(r.LegalEntityId, null, "RecurringJournal.Created", actorId, new { schedule.Id }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(schedule));
    }
    public async Task<Result<IReadOnlyCollection<FinancialDocumentResponse>>> GenerateDueSchedulesAsync(DateOnly through, string actorId, CancellationToken ct = default)
    {
        var schedules = await dbcontext.RecurringJournalSchedules.Include(x => x.Lines).Where(x => x.IsActive && x.NextRunDate <= through && (x.EndDate == null || x.NextRunDate <= x.EndDate)).ToListAsync(ct); var documents = new List<FinancialDocumentResponse>();
        foreach (var schedule in schedules)
        {
            var access = await RequireAsync(actorId, schedule.LegalEntityId, FinancialPermission.Prepare, ct);
            if (access.IsFailure) return Result.Failure<IReadOnlyCollection<FinancialDocumentResponse>>(access.Error);

            var date = schedule.NextRunDate;
            var baseCurrency = await dbcontext.LegalEntities.Where(x => x.Id == schedule.LegalEntityId).Select(x => x.BaseCurrencyCode).SingleAsync(ct);
            var exchangeRate = schedule.CurrencyCode == baseCurrency
                ? 1m
                : await dbcontext.ExchangeRates
                    .Where(x => x.LegalEntityId == schedule.LegalEntityId && x.FromCurrencyCode == schedule.CurrencyCode && x.ToCurrencyCode == baseCurrency && x.EffectiveDate <= date)
                    .OrderByDescending(x => x.EffectiveDate)
                    .Select(x => (decimal?)x.Rate)
                    .FirstOrDefaultAsync(ct);

            if (exchangeRate is null or <= 0)
                return Result.Failure<IReadOnlyCollection<FinancialDocumentResponse>>(LedgerErrors.InvalidPeriod);

            var result = await CreateManualJournalAsync(new CreateManualJournalRequest(schedule.LegalEntityId, schedule.BranchId, date, schedule.Description, schedule.CurrencyCode, exchangeRate.Value, $"recurring:{schedule.Id:N}:{date:yyyyMMdd}", schedule.Lines.Select(x => new JournalLineRequest(x.AccountId, x.Description, x.Debit, x.Credit)).ToArray()), actorId, ct);
            if (result.IsFailure) return Result.Failure<IReadOnlyCollection<FinancialDocumentResponse>>(result.Error);
            documents.Add(result.Value);
            schedule.NextRunDate = schedule.NextRunDate.AddMonths(schedule.FrequencyMonths);
            if (schedule.EndDate is not null && schedule.NextRunDate > schedule.EndDate) schedule.IsActive = false;
        }
        await dbcontext.SaveChangesAsync(ct); return Result.Success<IReadOnlyCollection<FinancialDocumentResponse>>(documents);
    }
    public async Task<Result<AccountingAccountResponse>> CreateAccountAsync(CreateAccountingAccountRequest request, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, request.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<AccountingAccountResponse>(access.Error);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == request.LegalEntityId, ct)) return Result.Failure<AccountingAccountResponse>(LedgerErrors.LegalEntityNotFound);
        var code = Code(request.Code);
        if (await dbcontext.AccountingAccounts.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.Code == code, ct)) return Result.Failure<AccountingAccountResponse>(LedgerErrors.DuplicateCode);
        if (request.ParentAccountId is not null && !await dbcontext.AccountingAccounts.AnyAsync(x => x.Id == request.ParentAccountId && x.LegalEntityId == request.LegalEntityId, ct)) return Result.Failure<AccountingAccountResponse>(LedgerErrors.AccountNotFound);
        var account = new AccountingAccount { LegalEntityId = request.LegalEntityId, ParentAccountId = request.ParentAccountId, Code = code, Name = request.Name.Trim(), Type = request.Type, IsControlAccount = request.IsControlAccount, AllowManualPosting = request.AllowManualPosting && !request.IsControlAccount, IsCashEquivalent = request.IsCashEquivalent };
        dbcontext.AccountingAccounts.Add(account); await AuditAsync(request.LegalEntityId, null, "Chart.AccountCreated", actorId, new { account.Code, account.Name }, ct); await dbcontext.SaveChangesAsync(ct);
        return Result.Success(ToResponse(account));
    }

    public async Task<Result<IReadOnlyCollection<AccountingAccountResponse>>> GetAccountsAsync(int legalEntityId, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, legalEntityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<IReadOnlyCollection<AccountingAccountResponse>>(access.Error);
        var accounts = await dbcontext.AccountingAccounts.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId).OrderBy(x => x.Code).Select(x => ToResponse(x)).ToListAsync(ct);
        return Result.Success<IReadOnlyCollection<AccountingAccountResponse>>(accounts);
    }

    public async Task<Result<PostingProfileResponse>> CreatePostingProfileAsync(CreatePostingProfileRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<PostingProfileResponse>(access.Error);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<PostingProfileResponse>(LedgerErrors.LegalEntityNotFound);
        var code = Code(r.Code); if (await dbcontext.PostingProfiles.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Code == code && x.Version == 1, ct)) return Result.Failure<PostingProfileResponse>(LedgerErrors.DuplicateCode);
        var ids = r.Lines.SelectMany(x => new[] { x.DebitAccountId, x.CreditAccountId }).Distinct().ToArray();
        if (await dbcontext.AccountingAccounts.CountAsync(x => x.LegalEntityId == r.LegalEntityId && x.IsActive && ids.Contains(x.Id), ct) != ids.Length) return Result.Failure<PostingProfileResponse>(LedgerErrors.AccountNotFound);
        var profile = new PostingProfile { LegalEntityId = r.LegalEntityId, Code = code, Name = r.Name.Trim(), EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, Lines = r.Lines.Select(x => new PostingProfileLine { EventCode = Code(x.EventCode), DebitAccountId = x.DebitAccountId, CreditAccountId = x.CreditAccountId }).ToList() };
        dbcontext.PostingProfiles.Add(profile); await AuditAsync(r.LegalEntityId, null, "Chart.PostingProfileCreated", actorId, new { profile.Code }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(profile));
    }

    public async Task<Result<FiscalYearResponse>> CreateFiscalYearAsync(CreateFiscalYearRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Configure, ct); if (access.IsFailure) return Result.Failure<FiscalYearResponse>(access.Error);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<FiscalYearResponse>(LedgerErrors.LegalEntityNotFound);
        if (await dbcontext.FiscalYears.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && (x.Name == r.Name.Trim() || (x.StartDate <= r.EndDate && x.EndDate >= r.StartDate)), ct)) return Result.Failure<FiscalYearResponse>(LedgerErrors.DuplicateCode);
        var periods = r.Periods.OrderBy(x => x.PeriodNumber).ToArray();
        if (periods.Select(x => x.PeriodNumber).Distinct().Count() != periods.Length || periods.Any(x => x.StartDate < r.StartDate || x.EndDate > r.EndDate || x.EndDate < x.StartDate) || periods[0].StartDate != r.StartDate || periods[^1].EndDate != r.EndDate || Enumerable.Range(1, Math.Max(0, periods.Length - 1)).Any(i => periods[i].StartDate != periods[i - 1].EndDate.AddDays(1))) return Result.Failure<FiscalYearResponse>(LedgerErrors.InvalidPeriod);
        var year = new FiscalYear { LegalEntityId = r.LegalEntityId, Name = r.Name.Trim(), StartDate = r.StartDate, EndDate = r.EndDate, Periods = periods.Select(x => new FiscalPeriod { PeriodNumber = x.PeriodNumber, Name = x.Name.Trim(), StartDate = x.StartDate, EndDate = x.EndDate }).ToList() };
        dbcontext.FiscalYears.Add(year); await AuditAsync(r.LegalEntityId, null, "Period.FiscalYearCreated", actorId, new { year.Name }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(year));
    }

    public async Task<Result<FiscalYearResponse>> GetFiscalYearAsync(int id, string actorId, CancellationToken ct = default)
    { var year = await dbcontext.FiscalYears.AsNoTracking().Include(x => x.Periods).SingleOrDefaultAsync(x => x.Id == id, ct); if (year is null) return Result.Failure<FiscalYearResponse>(LedgerErrors.FiscalYearNotFound); var access = await RequireAsync(actorId, year.LegalEntityId, FinancialPermission.View, ct); return access.IsFailure ? Result.Failure<FiscalYearResponse>(access.Error) : Result.Success(ToResponse(year)); }

    public async Task<Result<FiscalPeriodResponse>> SoftClosePeriodAsync(int id, ChangeFiscalPeriodStatusRequest request, string actorId, CancellationToken ct = default)
    {
        var period = await dbcontext.FiscalPeriods.Include(x => x.FiscalYear).SingleOrDefaultAsync(x => x.Id == id, ct); if (period is null || period.Status != FiscalPeriodStatus.Open || string.IsNullOrWhiteSpace(request.Reason)) return Result.Failure<FiscalPeriodResponse>(LedgerErrors.InvalidPeriod);
        var access = await RequireAsync(actorId, period.FiscalYear.LegalEntityId, FinancialPermission.ManagePeriods, ct); if (access.IsFailure) return Result.Failure<FiscalPeriodResponse>(access.Error);
        period.Status = FiscalPeriodStatus.SoftClosed; period.TaxLocked = request.TaxLocked; period.PayrollLocked = request.PayrollLocked; period.CloseReason = request.Reason.Trim(); period.ClosedBy = actorId; period.ClosedAt = DateTime.UtcNow; await AuditAsync(period.FiscalYear.LegalEntityId, null, "Period.SoftClosed", actorId, new { period.Id, period.CloseReason, period.TaxLocked, period.PayrollLocked }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(period));
    }

    public async Task<Result<FiscalPeriodResponse>> ClosePeriodAsync(int id, ChangeFiscalPeriodStatusRequest request, string actorId, CancellationToken ct = default)
    {
        var period = await dbcontext.FiscalPeriods.Include(x => x.FiscalYear).SingleOrDefaultAsync(x => x.Id == id, ct); if (period is null || period.Status is not (FiscalPeriodStatus.Open or FiscalPeriodStatus.SoftClosed) || string.IsNullOrWhiteSpace(request.Reason)) return Result.Failure<FiscalPeriodResponse>(LedgerErrors.InvalidPeriod);
        var access = await RequireAsync(actorId, period.FiscalYear.LegalEntityId, FinancialPermission.ManagePeriods, ct); if (access.IsFailure) return Result.Failure<FiscalPeriodResponse>(access.Error);
        var pending = await dbcontext.FinancialDocuments.AnyAsync(x => x.LegalEntityId == period.FiscalYear.LegalEntityId && x.TransactionDate >= period.StartDate && x.TransactionDate <= period.EndDate && (x.Status == FinancialDocumentStatus.Draft || x.Status == FinancialDocumentStatus.Submitted || x.Status == FinancialDocumentStatus.Approved), ct); if (pending) return Result.Failure<FiscalPeriodResponse>(LedgerErrors.InvalidTransition);
        period.Status = FiscalPeriodStatus.Closed; period.TaxLocked = request.TaxLocked; period.PayrollLocked = request.PayrollLocked; period.CloseReason = request.Reason.Trim(); period.ClosedBy = actorId; period.ClosedAt = DateTime.UtcNow; await AuditAsync(period.FiscalYear.LegalEntityId, null, "Period.Closed", actorId, new { period.Id, period.CloseReason, period.TaxLocked, period.PayrollLocked }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(period));
    }

    public async Task<Result<FiscalPeriodResponse>> ReopenPeriodAsync(int id, ChangeFiscalPeriodStatusRequest request, string actorId, CancellationToken ct = default)
    { var period = await dbcontext.FiscalPeriods.Include(x => x.FiscalYear).SingleOrDefaultAsync(x => x.Id == id, ct); if (period is null || period.Status is not (FiscalPeriodStatus.Closed or FiscalPeriodStatus.SoftClosed) || string.IsNullOrWhiteSpace(request.Reason)) return Result.Failure<FiscalPeriodResponse>(LedgerErrors.InvalidPeriod); var access = await RequireAsync(actorId, period.FiscalYear.LegalEntityId, FinancialPermission.ManagePeriods, ct); if (access.IsFailure) return Result.Failure<FiscalPeriodResponse>(access.Error); period.Status = FiscalPeriodStatus.Open; period.TaxLocked = false; period.PayrollLocked = false; period.ReopenReason = request.Reason.Trim(); period.ReopenedBy = actorId; period.ReopenedAt = DateTime.UtcNow; await AuditAsync(period.FiscalYear.LegalEntityId, null, "Period.Reopened", actorId, new { period.Id, period.ReopenReason, PreviousCloseReason = period.CloseReason }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(period)); }

    public async Task<Result<FinancialDocumentResponse>> CreateManualJournalAsync(CreateManualJournalRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialDocumentResponse>(access.Error);
        if (!IsBalanced(r.Lines)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.LegalEntityNotFound);
        var currencyCode = Code(r.CurrencyCode);
        if (!await dbcontext.Currencies.AnyAsync(x => x.Code == currencyCode && x.IsActive, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidPeriod);
        var baseCurrency = await dbcontext.LegalEntities.Where(x => x.Id == r.LegalEntityId).Select(x => x.BaseCurrencyCode).SingleAsync(ct);
        if (currencyCode == baseCurrency && r.ExchangeRate != 1m) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        if (currencyCode != baseCurrency && !await dbcontext.ExchangeRates.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.FromCurrencyCode == currencyCode && x.ToCurrencyCode == baseCurrency && x.EffectiveDate <= r.TransactionDate && x.Rate == r.ExchangeRate, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        var key = r.IdempotencyKey.Trim(); var requestHash = HashRequest(r); var duplicate = await dbcontext.FinancialDocuments.Include(x => x.Lines).SingleOrDefaultAsync(x => x.LegalEntityId == r.LegalEntityId && x.DocumentType == "ManualJournal" && x.IdempotencyKey == key, ct);
        if (duplicate is not null) return duplicate.RequestHash == requestHash ? Result.Success(ToResponse(duplicate)) : Result.Failure<FinancialDocumentResponse>(LedgerErrors.IdempotencyConflict);
        if (r.BranchId is not null && !await dbcontext.Branches.AnyAsync(x => x.Id == r.BranchId && x.LegalEntityId == r.LegalEntityId, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.LegalEntityNotFound);
        var accountIds = r.Lines.Select(x => x.AccountId).Distinct().ToArray(); var accounts = await dbcontext.AccountingAccounts.Where(x => x.LegalEntityId == r.LegalEntityId && accountIds.Contains(x.Id)).ToListAsync(ct);
        if (accounts.Count != accountIds.Length) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.AccountNotFound); if (accounts.Any(x => !x.IsActive || !x.AllowManualPosting || x.IsControlAccount)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.AccountNotPostable);
        var dimensionIds = r.Lines.SelectMany(x => x.DimensionValueIds ?? []).Distinct().ToArray();
        if (dimensionIds.Length > 0 && await dbcontext.FinancialDimensionValues.CountAsync(x => x.IsActive && dimensionIds.Contains(x.Id) && x.FinancialDimension.LegalEntityId == r.LegalEntityId, ct) != dimensionIds.Length) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        var requiredDimensionIds = await dbcontext.FinancialDimensions
            .Where(x => x.LegalEntityId == r.LegalEntityId && x.IsActive && x.IsRequired)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        if (requiredDimensionIds.Length > 0)
        {
            var suppliedDimensionPairs = await dbcontext.FinancialDimensionValues
                .Where(x => dimensionIds.Contains(x.Id))
                .Select(x => new { x.Id, x.FinancialDimensionId })
                .ToDictionaryAsync(x => x.Id, x => x.FinancialDimensionId, ct);
            if (r.Lines.Any(line => requiredDimensionIds.Any(requiredDimensionId => !(line.DimensionValueIds ?? []).Any(valueId => suppliedDimensionPairs.TryGetValue(valueId, out var dimensionId) && dimensionId == requiredDimensionId))))
                return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        }
        var number = await NextDocumentNumberAsync(r.LegalEntityId, "ManualJournal", ct);
        var doc = new FinancialDocument { LegalEntityId = r.LegalEntityId, BranchId = r.BranchId, DocumentNumber = number, Description = r.Description.Trim(), TransactionDate = r.TransactionDate, CurrencyCode = currencyCode, BaseCurrencyCode = baseCurrency, ExchangeRate = r.ExchangeRate, IdempotencyKey = key, RequestHash = requestHash, CorrelationId = Guid.NewGuid().ToString("N"), CreatedBy = actorId, Lines = r.Lines.Select((x, i) => new FinancialDocumentLine { LineNumber = i + 1, AccountId = x.AccountId, Description = Trim(x.Description), Debit = x.Debit, Credit = x.Credit, BaseDebit = decimal.Round(x.Debit * r.ExchangeRate, 4, MidpointRounding.AwayFromZero), BaseCredit = decimal.Round(x.Credit * r.ExchangeRate, 4, MidpointRounding.AwayFromZero), Dimensions = (x.DimensionValueIds ?? []).Distinct().Select(id => new FinancialDocumentLineDimension { FinancialDimensionValueId = id }).ToList() }).ToList() };
        dbcontext.FinancialDocuments.Add(doc); await AuditAsync(r.LegalEntityId, doc.Id, "Document.Created", actorId, new { doc.DocumentNumber, Total = doc.Lines.Sum(x => x.Debit) }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(doc));
    }

    public async Task<Result<FinancialDocumentResponse>> CreateSourceJournalAsync(CreateSourceJournalRequest r, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, r.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialDocumentResponse>(access.Error);
        var documentType = r.DocumentType.Trim(); var sourceReference = r.SourceReference.Trim(); var profileCode = Code(r.PostingProfileCode);
        if (string.IsNullOrWhiteSpace(documentType) || documentType.Length > 64 || string.IsNullOrWhiteSpace(sourceReference) || sourceReference.Length > 128 || !IsBalanced(r.Lines)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == r.LegalEntityId, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.LegalEntityNotFound);
        if (!await dbcontext.PostingProfiles.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.Code == profileCode && x.IsActive && x.EffectiveFrom <= r.TransactionDate && (x.EffectiveTo == null || x.EffectiveTo >= r.TransactionDate), ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        var currencyCode = Code(r.CurrencyCode);
        if (!await dbcontext.Currencies.AnyAsync(x => x.Code == currencyCode && x.IsActive, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidPeriod);
        var baseCurrency = await dbcontext.LegalEntities.Where(x => x.Id == r.LegalEntityId).Select(x => x.BaseCurrencyCode).SingleAsync(ct);
        if (currencyCode == baseCurrency && r.ExchangeRate != 1m) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        if (currencyCode != baseCurrency && !await dbcontext.ExchangeRates.AnyAsync(x => x.LegalEntityId == r.LegalEntityId && x.FromCurrencyCode == currencyCode && x.ToCurrencyCode == baseCurrency && x.EffectiveDate <= r.TransactionDate && x.Rate == r.ExchangeRate, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        var key = r.IdempotencyKey.Trim(); var requestHash = HashRequest(r); var duplicate = await dbcontext.FinancialDocuments.Include(x => x.Lines).SingleOrDefaultAsync(x => x.LegalEntityId == r.LegalEntityId && x.DocumentType == documentType && x.IdempotencyKey == key, ct);
        if (duplicate is not null) return duplicate.RequestHash == requestHash ? Result.Success(ToResponse(duplicate)) : Result.Failure<FinancialDocumentResponse>(LedgerErrors.IdempotencyConflict);
        if (r.BranchId is not null && !await dbcontext.Branches.AnyAsync(x => x.Id == r.BranchId && x.LegalEntityId == r.LegalEntityId, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.LegalEntityNotFound);
        var accountIds = r.Lines.Select(x => x.AccountId).Distinct().ToArray(); var accounts = await dbcontext.AccountingAccounts.Where(x => x.LegalEntityId == r.LegalEntityId && accountIds.Contains(x.Id)).ToListAsync(ct);
        if (accounts.Count != accountIds.Length) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.AccountNotFound); if (accounts.Any(x => !x.IsActive)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.AccountNotPostable);
        var dimensionIds = r.Lines.SelectMany(x => x.DimensionValueIds ?? []).Distinct().ToArray();
        if (dimensionIds.Length > 0 && await dbcontext.FinancialDimensionValues.CountAsync(x => x.IsActive && dimensionIds.Contains(x.Id) && x.FinancialDimension.LegalEntityId == r.LegalEntityId, ct) != dimensionIds.Length) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        var requiredDimensionIds = await dbcontext.FinancialDimensions.Where(x => x.LegalEntityId == r.LegalEntityId && x.IsActive && x.IsRequired).Select(x => x.Id).ToArrayAsync(ct);
        if (requiredDimensionIds.Length > 0)
        {
            var suppliedDimensionPairs = await dbcontext.FinancialDimensionValues.Where(x => dimensionIds.Contains(x.Id)).Select(x => new { x.Id, x.FinancialDimensionId }).ToDictionaryAsync(x => x.Id, x => x.FinancialDimensionId, ct);
            if (r.Lines.Any(line => requiredDimensionIds.Any(requiredDimensionId => !(line.DimensionValueIds ?? []).Any(valueId => suppliedDimensionPairs.TryGetValue(valueId, out var dimensionId) && dimensionId == requiredDimensionId)))) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidJournal);
        }
        var number = await NextDocumentNumberAsync(r.LegalEntityId, documentType, ct);
        var doc = new FinancialDocument { LegalEntityId = r.LegalEntityId, BranchId = r.BranchId, DocumentType = documentType, DocumentNumber = number, SourceReference = sourceReference, PostingProfileCode = profileCode, Description = r.Description.Trim(), TransactionDate = r.TransactionDate, CurrencyCode = currencyCode, BaseCurrencyCode = baseCurrency, ExchangeRate = r.ExchangeRate, IdempotencyKey = key, RequestHash = requestHash, CorrelationId = Guid.NewGuid().ToString("N"), CreatedBy = actorId, Lines = r.Lines.Select((x, i) => new FinancialDocumentLine { LineNumber = i + 1, AccountId = x.AccountId, Description = Trim(x.Description), Debit = x.Debit, Credit = x.Credit, BaseDebit = decimal.Round(x.Debit * r.ExchangeRate, 4, MidpointRounding.AwayFromZero), BaseCredit = decimal.Round(x.Credit * r.ExchangeRate, 4, MidpointRounding.AwayFromZero), Dimensions = (x.DimensionValueIds ?? []).Distinct().Select(id => new FinancialDocumentLineDimension { FinancialDimensionValueId = id }).ToList() }).ToList() };
        dbcontext.FinancialDocuments.Add(doc); await AuditAsync(r.LegalEntityId, doc.Id, "Document.SourceCreated", actorId, new { doc.DocumentNumber, doc.DocumentType, doc.SourceReference, Total = doc.Lines.Sum(x => x.Debit) }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(doc));
    }

    public async Task<Result<FinancialDocumentResponse>> GetDocumentAsync(Guid id, string actorId, CancellationToken ct = default)
    { var d = await dbcontext.FinancialDocuments.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct); if (d is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.DocumentNotFound); var access = await RequireAsync(actorId, d.LegalEntityId, FinancialPermission.View, ct); return access.IsFailure ? Result.Failure<FinancialDocumentResponse>(access.Error) : Result.Success(ToResponse(d)); }

    public async Task<Result<FinancialDocumentResponse>> SubmitDocumentAsync(Guid id, string actorId, CancellationToken ct = default)
    { var d = await LoadDocumentAsync(id, ct); if (d is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.DocumentNotFound); var access = await RequireAsync(actorId, d.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialDocumentResponse>(access.Error); if (d.Status != FinancialDocumentStatus.Draft || !IsBalanced(d.Lines)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidTransition); d.Status = FinancialDocumentStatus.Submitted; d.SubmittedBy = actorId; d.SubmittedAt = DateTime.UtcNow; await AuditAsync(d.LegalEntityId, d.Id, "Document.Submitted", actorId, new { d.DocumentNumber }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(d)); }

    public async Task<Result<FinancialDocumentResponse>> ApproveDocumentAsync(Guid id, ApproveDocumentRequest r, string actorId, CancellationToken ct = default)
    { var d = await LoadDocumentAsync(id, ct); if (d is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.DocumentNotFound); var access = await RequireAsync(actorId, d.LegalEntityId, FinancialPermission.Approve, ct); if (access.IsFailure) return Result.Failure<FinancialDocumentResponse>(access.Error); if (d.Status != FinancialDocumentStatus.Submitted) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidTransition); if (d.DocumentType == "ManualJournal" && string.Equals(d.CreatedBy, actorId, StringComparison.Ordinal)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.MakerCheckerViolation); d.Status = FinancialDocumentStatus.Approved; d.ApprovedBy = actorId; d.ApprovedAt = DateTime.UtcNow; d.Approvals.Add(new DocumentApproval { ApprovedBy = actorId, Comment = Trim(r.Comment) }); await AuditAsync(d.LegalEntityId, d.Id, "Document.Approved", actorId, new { d.DocumentNumber }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(d)); }

    public async Task<Result<JournalEntryResponse>> PostDocumentAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var d = await LoadDocumentAsync(id, ct); if (d is null) return Result.Failure<JournalEntryResponse>(LedgerErrors.DocumentNotFound); var access = await RequireAsync(actorId, d.LegalEntityId, FinancialPermission.Post, ct); if (access.IsFailure) return Result.Failure<JournalEntryResponse>(access.Error); if (d.Status != FinancialDocumentStatus.Approved) return Result.Failure<JournalEntryResponse>(LedgerErrors.InvalidTransition); if (d.DocumentType == "ManualJournal" && (string.Equals(d.CreatedBy, actorId, StringComparison.Ordinal) || !d.Approvals.Any(x => !string.Equals(x.ApprovedBy, d.CreatedBy, StringComparison.Ordinal)))) return Result.Failure<JournalEntryResponse>(LedgerErrors.MakerCheckerViolation); if (!IsBalanced(d.Lines)) return Result.Failure<JournalEntryResponse>(LedgerErrors.InvalidJournal);
        var period = await dbcontext.FiscalPeriods.Include(x => x.FiscalYear).SingleOrDefaultAsync(x => x.FiscalYear.LegalEntityId == d.LegalEntityId && x.StartDate <= d.TransactionDate && x.EndDate >= d.TransactionDate && x.Status == FiscalPeriodStatus.Open, ct); if (period is null) return Result.Failure<JournalEntryResponse>(LedgerErrors.InvalidPeriod);
        if (await dbcontext.PostingBatches.AnyAsync(x => x.FinancialDocumentId == id, ct)) return Result.Failure<JournalEntryResponse>(LedgerErrors.IdempotencyConflict);
        var batch = new PostingBatch { LegalEntityId = d.LegalEntityId, FinancialDocumentId = d.Id, PostingKey = $"{d.DocumentType}:{d.Id:N}", PostedBy = actorId };
        var entry = new JournalEntry { PostingBatch = batch, LegalEntityId = d.LegalEntityId, FiscalPeriodId = period.Id, EntryNumber = $"JE-{d.DocumentNumber}", PostingDate = d.TransactionDate, Description = d.Description, Lines = d.Lines.Select(x => new JournalLine { LineNumber = x.LineNumber, AccountId = x.AccountId, Description = x.Description, Debit = x.Debit, Credit = x.Credit, BaseDebit = x.BaseDebit, BaseCredit = x.BaseCredit, Dimensions = x.Dimensions.Select(dimension => new JournalLineDimension { FinancialDimensionValueId = dimension.FinancialDimensionValueId }).ToList() }).ToList() };
        if (d.ReversalOfDocumentId is not null) { var original = await dbcontext.PostingBatches.SingleOrDefaultAsync(x => x.FinancialDocumentId == d.ReversalOfDocumentId, ct); if (original is null) return Result.Failure<JournalEntryResponse>(LedgerErrors.InvalidTransition); batch.ReversalOfPostingBatchId = original.Id; var originalDoc = await dbcontext.FinancialDocuments.SingleAsync(x => x.Id == d.ReversalOfDocumentId, ct); originalDoc.Status = FinancialDocumentStatus.Reversed; originalDoc.ReversedByDocumentId = d.Id; }
        var ownsTransaction = dbcontext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await dbcontext.Database.BeginTransactionAsync(ct) : null;
        dbcontext.JournalEntries.Add(entry); d.Status = FinancialDocumentStatus.Posted; d.PostedBy = actorId; d.PostedAt = DateTime.UtcNow; await AuditAsync(d.LegalEntityId, d.Id, "Document.Posted", actorId, new { d.DocumentNumber, entry.EntryNumber }, ct); await dbcontext.SaveChangesAsync(ct);
        entry.IsFinalized = true; await dbcontext.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); return Result.Success(ToResponse(entry));
    }

    public async Task<Result<FinancialDocumentResponse>> CreateReversalAsync(Guid id, ReverseJournalRequest r, string actorId, CancellationToken ct = default)
    {
        var d = await LoadDocumentAsync(id, ct); if (d is null) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.DocumentNotFound); var access = await RequireAsync(actorId, d.LegalEntityId, FinancialPermission.Prepare, ct); if (access.IsFailure) return Result.Failure<FinancialDocumentResponse>(access.Error); if (d.Status != FinancialDocumentStatus.Posted) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.InvalidTransition); if (await dbcontext.FinancialDocuments.AnyAsync(x => x.ReversalOfDocumentId == id, ct)) return Result.Failure<FinancialDocumentResponse>(LedgerErrors.ReversalExists);
        Result<FinancialDocumentResponse> result;
        var reversalLines = d.Lines.Select(x => new JournalLineRequest(x.AccountId, x.Description, x.Credit, x.Debit)).ToArray();
        if (d.DocumentType == "ManualJournal")
            result = await CreateManualJournalAsync(new CreateManualJournalRequest(d.LegalEntityId, d.BranchId, r.ReversalDate, $"Reversal of {d.DocumentNumber}: {r.Reason.Trim()}", d.CurrencyCode, d.ExchangeRate, r.IdempotencyKey, reversalLines), actorId, ct);
        else
            result = await CreateSourceJournalAsync(new CreateSourceJournalRequest(d.LegalEntityId, d.BranchId, r.ReversalDate, $"{d.DocumentType}Reversal", $"REV:{d.Id:N}", d.PostingProfileCode ?? string.Empty, $"Reversal of {d.DocumentNumber}: {r.Reason.Trim()}", d.CurrencyCode, d.ExchangeRate, r.IdempotencyKey, reversalLines), actorId, ct);
        if (result.IsFailure) return result; var reversal = await dbcontext.FinancialDocuments.SingleAsync(x => x.Id == result.Value.Id, ct); reversal.ReversalOfDocumentId = d.Id; reversal.ReversalReason = r.Reason.Trim(); await AuditAsync(d.LegalEntityId, reversal.Id, "Document.ReversalCreated", actorId, new { Original = d.DocumentNumber }, ct); await dbcontext.SaveChangesAsync(ct); return Result.Success(ToResponse(reversal));
    }

    public async Task<Result<IReadOnlyCollection<ApprovalInboxItemResponse>>> GetApprovalInboxAsync(int entityId, string actorId, CancellationToken ct = default)
    { var access = await RequireAsync(actorId, entityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<IReadOnlyCollection<ApprovalInboxItemResponse>>(access.Error); var docs = await dbcontext.FinancialDocuments.AsNoTracking().Where(x => x.LegalEntityId == entityId && x.Status == FinancialDocumentStatus.Submitted).OrderBy(x => x.CreatedAt).Select(x => new ApprovalInboxItemResponse(x.Id, x.DocumentNumber, x.DocumentType, x.Description, x.TransactionDate, x.Lines.Sum(l => l.Debit), x.CreatedBy)).ToListAsync(ct); return Result.Success<IReadOnlyCollection<ApprovalInboxItemResponse>>(docs); }

    public async Task<Result<TrialBalanceResponse>> GetTrialBalanceAsync(int entityId, DateOnly from, DateOnly to, string actorId, CancellationToken ct = default)
    {
        if (to < from) return Result.Failure<TrialBalanceResponse>(LedgerErrors.InvalidPeriod);
        var access = await RequireAsync(actorId, entityId, FinancialPermission.View, ct);
        if (access.IsFailure) return Result.Failure<TrialBalanceResponse>(access.Error);
        var totals = await dbcontext.JournalLines.AsNoTracking()
            .Where(x => x.JournalEntry.LegalEntityId == entityId && x.JournalEntry.IsFinalized && x.JournalEntry.PostingDate <= to)
            .GroupBy(x => new { x.AccountId, x.Account.Code, x.Account.Name, x.Account.Type })
            .Select(g => new
            {
                g.Key.AccountId, g.Key.Code, g.Key.Name, g.Key.Type,
                OpeningDebit = g.Where(x => x.JournalEntry.PostingDate < from).Sum(x => x.BaseDebit),
                OpeningCredit = g.Where(x => x.JournalEntry.PostingDate < from).Sum(x => x.BaseCredit),
                MovementDebit = g.Where(x => x.JournalEntry.PostingDate >= from).Sum(x => x.BaseDebit),
                MovementCredit = g.Where(x => x.JournalEntry.PostingDate >= from).Sum(x => x.BaseCredit)
            }).ToListAsync(ct);
        var lines = totals.Select(x =>
        {
            var closingDebit = x.OpeningDebit + x.MovementDebit;
            var closingCredit = x.OpeningCredit + x.MovementCredit;
            var balance = NormalBalance(x.Type, closingDebit, closingCredit);
            return new TrialBalanceLineResponse(x.AccountId, x.Code, x.Name, x.Type, x.OpeningDebit, x.OpeningCredit, x.MovementDebit, x.MovementCredit, closingDebit, closingCredit, balance);
        }).OrderBy(x => x.AccountCode).ToArray();
        return Result.Success(new TrialBalanceResponse(entityId, from, to, lines, lines.Sum(x => x.OpeningDebit), lines.Sum(x => x.OpeningCredit), lines.Sum(x => x.MovementDebit), lines.Sum(x => x.MovementCredit), lines.Sum(x => x.ClosingDebit), lines.Sum(x => x.ClosingCredit)));
    }

    public async Task<Result<ProfitAndLossResponse>> GetProfitAndLossAsync(int entityId, DateOnly from, DateOnly to, string actorId, CancellationToken ct = default)
    {
        if (to < from) return Result.Failure<ProfitAndLossResponse>(LedgerErrors.InvalidPeriod);
        var access = await RequireAsync(actorId, entityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<ProfitAndLossResponse>(access.Error);
        var accountTotals = await dbcontext.JournalLines.AsNoTracking()
            .Where(x => x.JournalEntry.LegalEntityId == entityId && x.JournalEntry.IsFinalized && x.JournalEntry.PostingDate >= from && x.JournalEntry.PostingDate <= to && (x.Account.Type == AccountingAccountType.Revenue || x.Account.Type == AccountingAccountType.Expense))
            .GroupBy(x => new { x.AccountId, x.Account.Code, x.Account.Name, x.Account.Type })
            .Select(g => new { g.Key.AccountId, g.Key.Code, g.Key.Name, g.Key.Type, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct);

        var lines = accountTotals
            .Select(x => new ProfitAndLossLineResponse(x.AccountId, x.Code, x.Name, x.Type, x.Debit, x.Credit, x.Type == AccountingAccountType.Revenue ? x.Credit - x.Debit : x.Debit - x.Credit))
            .OrderBy(x => x.AccountCode)
            .ToArray();

        var revenue = lines.Where(x => x.AccountType == AccountingAccountType.Revenue).Sum(x => x.SignedAmount);
        var expense = lines.Where(x => x.AccountType == AccountingAccountType.Expense).Sum(x => x.SignedAmount);
        return Result.Success(new ProfitAndLossResponse(entityId, from, to, lines, revenue, expense, revenue - expense));
    }

    public async Task<Result<BalanceSheetResponse>> GetBalanceSheetAsync(int entityId, DateOnly asOf, string actorId, CancellationToken ct = default)
    {
        var access = await RequireAsync(actorId, entityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<BalanceSheetResponse>(access.Error);
        var totals = await dbcontext.JournalLines.AsNoTracking()
            .Where(x => x.JournalEntry.LegalEntityId == entityId && x.JournalEntry.IsFinalized && x.JournalEntry.PostingDate <= asOf && (x.Account.Type == AccountingAccountType.Asset || x.Account.Type == AccountingAccountType.Liability || x.Account.Type == AccountingAccountType.Equity))
            .GroupBy(x => new { x.AccountId, x.Account.Code, x.Account.Name, x.Account.Type })
            .Select(g => new { g.Key.AccountId, g.Key.Code, g.Key.Name, g.Key.Type, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct);
        var fiscalYearStart = await dbcontext.FiscalYears.AsNoTracking().Where(x => x.LegalEntityId == entityId && x.StartDate <= asOf && x.EndDate >= asOf).Select(x => (DateOnly?)x.StartDate).SingleOrDefaultAsync(ct) ?? DateOnly.MinValue;
        var currentEarningsTotals = await dbcontext.JournalLines.AsNoTracking().Where(x => x.JournalEntry.LegalEntityId == entityId && x.JournalEntry.IsFinalized && x.JournalEntry.PostingDate >= fiscalYearStart && x.JournalEntry.PostingDate <= asOf && (x.Account.Type == AccountingAccountType.Revenue || x.Account.Type == AccountingAccountType.Expense)).GroupBy(_ => 1).Select(g => new { Revenue = g.Where(x => x.Account.Type == AccountingAccountType.Revenue).Sum(x => x.BaseCredit - x.BaseDebit), Expense = g.Where(x => x.Account.Type == AccountingAccountType.Expense).Sum(x => x.BaseDebit - x.BaseCredit) }).SingleOrDefaultAsync(ct);
        var currentEarnings = (currentEarningsTotals?.Revenue ?? 0m) - (currentEarningsTotals?.Expense ?? 0m);
        var lines = totals.Select(x => new BalanceSheetLineResponse(x.AccountId, x.Code, x.Name, x.Type, x.Debit, x.Credit, NormalBalance(x.Type, x.Debit, x.Credit))).Append(new BalanceSheetLineResponse(0, "CURRENT-EARNINGS", "Current earnings", AccountingAccountType.Equity, currentEarnings < 0 ? -currentEarnings : 0, currentEarnings > 0 ? currentEarnings : 0, currentEarnings)).OrderBy(x => x.AccountCode).ToArray();
        var assets = lines.Where(x => x.AccountType == AccountingAccountType.Asset).Sum(x => x.Balance); var liabilities = lines.Where(x => x.AccountType == AccountingAccountType.Liability).Sum(x => x.Balance); var equity = lines.Where(x => x.AccountType == AccountingAccountType.Equity).Sum(x => x.Balance);
        return Result.Success(new BalanceSheetResponse(entityId, asOf, lines, assets, liabilities, equity, assets - liabilities - equity));
    }

    public async Task<Result<CashMovementResponse>> GetCashMovementAsync(int entityId, DateOnly from, DateOnly to, string actorId, CancellationToken ct = default)
    {
        if (to < from) return Result.Failure<CashMovementResponse>(LedgerErrors.InvalidPeriod);
        var access = await RequireAsync(actorId, entityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<CashMovementResponse>(access.Error);
        var ids = await dbcontext.AccountingAccounts.AsNoTracking().Where(x => x.LegalEntityId == entityId && x.IsActive && x.IsCashEquivalent).Select(x => x.Id).ToArrayAsync(ct); if (ids.Length == 0) return Result.Failure<CashMovementResponse>(LedgerErrors.AccountNotFound);
        var totals = await dbcontext.JournalLines.AsNoTracking().Where(x => x.JournalEntry.LegalEntityId == entityId && x.JournalEntry.IsFinalized && x.JournalEntry.PostingDate >= from && x.JournalEntry.PostingDate <= to && ids.Contains(x.AccountId)).GroupBy(_ => 1).Select(g => new { Inflows = g.Sum(x => x.BaseDebit), Outflows = g.Sum(x => x.BaseCredit) }).SingleOrDefaultAsync(ct);
        var inflows = totals?.Inflows ?? 0m; var outflows = totals?.Outflows ?? 0m; return Result.Success(new CashMovementResponse(entityId, from, to, ids, inflows, outflows, inflows - outflows));
    }

    public async Task<Result<DimensionBalanceResponse>> GetDimensionBalanceAsync(int entityId, int financialDimensionId, DateOnly from, DateOnly to, string actorId, CancellationToken ct = default)
    {
        if (to < from) return Result.Failure<DimensionBalanceResponse>(LedgerErrors.InvalidPeriod);
        var access = await RequireAsync(actorId, entityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<DimensionBalanceResponse>(access.Error);
        var dimension = await dbcontext.FinancialDimensions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == financialDimensionId && x.LegalEntityId == entityId, ct);
        if (dimension is null) return Result.Failure<DimensionBalanceResponse>(LedgerErrors.LegalEntityNotFound);

        var lines = await dbcontext.JournalLineDimensions.AsNoTracking()
            .Where(x => x.FinancialDimensionValue.FinancialDimensionId == financialDimensionId && x.JournalLine.JournalEntry.LegalEntityId == entityId && x.JournalLine.JournalEntry.IsFinalized && x.JournalLine.JournalEntry.PostingDate >= from && x.JournalLine.JournalEntry.PostingDate <= to)
            .GroupBy(x => new { x.FinancialDimensionValueId, DimensionValueCode = x.FinancialDimensionValue.Code, DimensionValueName = x.FinancialDimensionValue.Name, x.JournalLine.AccountId, AccountCode = x.JournalLine.Account.Code, AccountName = x.JournalLine.Account.Name, x.JournalLine.Account.Type })
            .Select(g => new { g.Key.FinancialDimensionValueId, g.Key.DimensionValueCode, g.Key.DimensionValueName, g.Key.AccountId, g.Key.AccountCode, g.Key.AccountName, g.Key.Type, Debit = g.Sum(x => x.JournalLine.BaseDebit), Credit = g.Sum(x => x.JournalLine.BaseCredit) })
            .OrderBy(x => x.DimensionValueCode).ThenBy(x => x.AccountCode)
            .ToListAsync(ct);
        var responseLines = lines.Select(x => new DimensionBalanceLineResponse(x.FinancialDimensionValueId, x.DimensionValueCode, x.DimensionValueName, x.AccountId, x.AccountCode, x.AccountName, x.Type, x.Debit, x.Credit, NormalBalance(x.Type, x.Debit, x.Credit))).ToArray();
        return Result.Success(new DimensionBalanceResponse(entityId, dimension.Id, dimension.Code, dimension.Name, from, to, responseLines));
    }

    public async Task<Result<IReadOnlyCollection<AccountingAuditEventResponse>>> GetAuditEventsAsync(int entityId, int take, string actorId, CancellationToken ct = default)
    { var access = await RequireAsync(actorId, entityId, FinancialPermission.View, ct); if (access.IsFailure) return Result.Failure<IReadOnlyCollection<AccountingAuditEventResponse>>(access.Error); var records = await dbcontext.AccountingAuditEvents.AsNoTracking().Where(x => x.LegalEntityId == entityId).OrderByDescending(x => x.Id).Take(Math.Clamp(take, 1, 500)).Select(x => new AccountingAuditEventResponse(x.Id, x.EventType, x.ActorId, x.OccurredAt, x.PayloadJson, x.Hash)).ToListAsync(ct); return Result.Success<IReadOnlyCollection<AccountingAuditEventResponse>>(records); }

    private static IOrderedQueryable<ExchangeRate> OrderExchangeRates(IQueryable<ExchangeRate> query, string? sortBy, string? sortDirection)
    {
        var desc = IsDescending(sortDirection);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "fromcurrencycode" or "fromcurrency" => desc ? query.OrderByDescending(x => x.FromCurrencyCode).ThenByDescending(x => x.Id) : query.OrderBy(x => x.FromCurrencyCode).ThenBy(x => x.Id),
            "tocurrencycode" or "tocurrency" => desc ? query.OrderByDescending(x => x.ToCurrencyCode).ThenByDescending(x => x.Id) : query.OrderBy(x => x.ToCurrencyCode).ThenBy(x => x.Id),
            "rate" => desc ? query.OrderByDescending(x => x.Rate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Rate).ThenBy(x => x.Id),
            _ => desc
                ? query.OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
                : query.OrderBy(x => x.EffectiveDate).ThenBy(x => x.Id)
        };
    }

    private static IOrderedQueryable<PostingProfile> OrderPostingProfiles(IQueryable<PostingProfile> query, string? sortBy, string? sortDirection)
    {
        var desc = IsDescending(sortDirection);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "code" => desc ? query.OrderByDescending(x => x.Code).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Code).ThenBy(x => x.Id),
            "name" => desc ? query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "version" => desc ? query.OrderByDescending(x => x.Version).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Version).ThenBy(x => x.Id),
            "effectiveto" => desc ? query.OrderByDescending(x => x.EffectiveTo).ThenByDescending(x => x.Id) : query.OrderBy(x => x.EffectiveTo).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id) : query.OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id)
        };
    }

    private static IOrderedQueryable<FiscalYear> OrderFiscalYears(IQueryable<FiscalYear> query, string? sortBy, string? sortDirection)
    {
        var desc = IsDescending(sortDirection);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "name" => desc ? query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "enddate" => desc ? query.OrderByDescending(x => x.EndDate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.EndDate).ThenBy(x => x.Id),
            "isclosed" or "status" => desc ? query.OrderByDescending(x => x.IsClosed).ThenByDescending(x => x.Id) : query.OrderBy(x => x.IsClosed).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.StartDate).ThenBy(x => x.Id)
        };
    }

    private static IOrderedQueryable<RecurringJournalSchedule> OrderRecurringSchedules(IQueryable<RecurringJournalSchedule> query, string? sortBy, string? sortDirection)
    {
        var desc = IsDescending(sortDirection);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "documenttype" or "type" => desc ? query.OrderByDescending(x => x.DocumentType).ThenByDescending(x => x.Id) : query.OrderBy(x => x.DocumentType).ThenBy(x => x.Id),
            "description" => desc ? query.OrderByDescending(x => x.Description).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Description).ThenBy(x => x.Id),
            "isactive" or "status" => desc ? query.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id) : query.OrderBy(x => x.IsActive).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.NextRunDate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.NextRunDate).ThenBy(x => x.Id)
        };
    }

    private static IOrderedQueryable<FinancialDocument> OrderDocuments(IQueryable<FinancialDocument> query, string? sortBy, string? sortDirection)
    {
        var desc = IsDescending(sortDirection);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "documentnumber" or "number" => desc ? query.OrderByDescending(x => x.DocumentNumber).ThenByDescending(x => x.Id) : query.OrderBy(x => x.DocumentNumber).ThenBy(x => x.Id),
            "documenttype" or "type" => desc ? query.OrderByDescending(x => x.DocumentType).ThenByDescending(x => x.Id) : query.OrderBy(x => x.DocumentType).ThenBy(x => x.Id),
            "status" => desc ? query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "amount" => desc ? query.OrderByDescending(x => x.Lines.Sum(l => l.Debit)).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Lines.Sum(l => l.Debit)).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.TransactionDate).ThenBy(x => x.Id)
        };
    }

    private static IOrderedQueryable<JournalEntry> OrderJournalEntries(IQueryable<JournalEntry> query, string? sortBy, string? sortDirection)
    {
        var desc = IsDescending(sortDirection);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "entrynumber" or "number" => desc ? query.OrderByDescending(x => x.EntryNumber).ThenByDescending(x => x.Id) : query.OrderBy(x => x.EntryNumber).ThenBy(x => x.Id),
            "description" => desc ? query.OrderByDescending(x => x.Description).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Description).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.PostingDate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.PostingDate).ThenBy(x => x.Id)
        };
    }

    private static bool IsDescending(string? sortDirection) => !string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
    private static bool IsValidSortDirection(string? sortDirection) => string.IsNullOrWhiteSpace(sortDirection) || string.Equals(sortDirection.Trim(), "asc", StringComparison.OrdinalIgnoreCase) || string.Equals(sortDirection.Trim(), "desc", StringComparison.OrdinalIgnoreCase);

    private async Task<FinancialDocument?> LoadDocumentAsync(Guid id, CancellationToken ct) => await dbcontext.FinancialDocuments.Include(x => x.Lines).ThenInclude(x => x.Dimensions).Include(x => x.Approvals).SingleOrDefaultAsync(x => x.Id == id, ct);
    private Task<Result> RequireAsync(string actorId, int legalEntityId, FinancialPermission permission, CancellationToken ct) => financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, permission, ct);
    private async Task<string> NextDocumentNumberAsync(int entityId, string type, CancellationToken ct) { var sequence = await dbcontext.LegalEntityDocumentSequences.SingleOrDefaultAsync(x => x.LegalEntityId == entityId && x.DocumentType == type, ct); if (sequence is null) { sequence = new LegalEntityDocumentSequence { LegalEntityId = entityId, DocumentType = type }; dbcontext.LegalEntityDocumentSequences.Add(sequence); } var number = sequence.NextNumber++; return $"{type[..2].ToUpperInvariant()}-{entityId:D5}-{number:D8}"; }
    private async Task AuditAsync(int entityId, Guid? documentId, string type, string actorId, object payload, CancellationToken ct)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:AuditChain:" + entityId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == entityId, ct);
        if (head is null) { head = new AccountingAuditChainHead { LegalEntityId = entityId }; dbcontext.AccountingAuditChainHeads.Add(head); }
        var json = JsonSerializer.Serialize(payload); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{entityId}|{documentId}|{type}|{actorId}|{json}")));
        dbcontext.AccountingAuditEvents.Add(new AccountingAuditEvent { LegalEntityId = entityId, FinancialDocumentId = documentId, EventType = type, ActorId = actorId, PayloadJson = json, PreviousHash = head.LastHash, Hash = hash });
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage { LegalEntityId = entityId, Type = type, PayloadJson = json, CorrelationId = hash[..32] });
        head.LastHash = hash;
    }
    private static bool IsBalanced(IEnumerable<JournalLineRequest> lines) => lines.All(x => (x.Debit > 0 && x.Credit == 0) || (x.Credit > 0 && x.Debit == 0)) && lines.Sum(x => x.Debit) == lines.Sum(x => x.Credit);
    private static bool IsBalanced(IEnumerable<FinancialDocumentLine> lines) => lines.All(x => (x.Debit > 0 && x.Credit == 0) || (x.Credit > 0 && x.Debit == 0)) && lines.Sum(x => x.Debit) == lines.Sum(x => x.Credit);
    private static string Code(string value) => value.Trim().ToUpperInvariant(); private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string HashRequest<T>(T request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    private static decimal NormalBalance(AccountingAccountType type, decimal debit, decimal credit) => type is AccountingAccountType.Asset or AccountingAccountType.Expense ? debit - credit : credit - debit;
    private static AccountingAccountResponse ToResponse(AccountingAccount x) => new(x.Id, x.LegalEntityId, x.ParentAccountId, x.Code, x.Name, x.Type, x.IsControlAccount, x.AllowManualPosting, x.IsCashEquivalent, x.IsActive);
    private static PostingProfileResponse ToResponse(PostingProfile x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.Version, x.EffectiveFrom, x.EffectiveTo, x.Lines.OrderBy(l => l.EventCode).Select(l => new PostingProfileLineResponse(l.EventCode, l.DebitAccountId, l.CreditAccountId)).ToArray(), x.IsActive);
    private static FiscalPeriodResponse ToResponse(FiscalPeriod p) => new(p.Id, p.PeriodNumber, p.Name, p.StartDate, p.EndDate, p.Status, p.TaxLocked, p.PayrollLocked, p.CloseReason, p.ReopenReason, p.ClosedBy, p.ClosedAt, p.ReopenedBy, p.ReopenedAt);
    private static FiscalYearResponse ToResponse(FiscalYear x) => new(x.Id, x.LegalEntityId, x.Name, x.StartDate, x.EndDate, x.IsClosed, x.Periods.OrderBy(p => p.PeriodNumber).Select(p => new FiscalPeriodResponse(p.Id, p.PeriodNumber, p.Name, p.StartDate, p.EndDate, p.Status, p.TaxLocked, p.PayrollLocked, p.CloseReason, p.ReopenReason, p.ClosedBy, p.ClosedAt, p.ReopenedBy, p.ReopenedAt)).ToArray());
    private static FinancialDocumentResponse ToResponse(FinancialDocument x) => new(x.Id, x.LegalEntityId, x.BranchId, x.DocumentType, x.DocumentNumber, x.SourceReference, x.Description, x.TransactionDate, x.Status, x.CreatedBy, x.SubmittedBy, x.ApprovedBy, x.PostedBy, x.ReversalOfDocumentId, x.ReversedByDocumentId, x.Lines.OrderBy(l => l.LineNumber).Select(l => new FinancialDocumentLineResponse(l.LineNumber, l.AccountId, l.Description, l.Debit, l.Credit)).ToArray(), x.CorrelationId, x.RequestHash);
    private static JournalEntryResponse ToResponse(JournalEntry x) => new(x.Id, x.PostingBatchId, x.LegalEntityId, x.FiscalPeriodId, x.EntryNumber, x.PostingDate, x.Description, x.Lines.OrderBy(l => l.LineNumber).Select(l => new JournalLineResponse(l.LineNumber, l.AccountId, l.Description, l.Debit, l.Credit)).ToArray());
    private static RecurringJournalScheduleResponse ToResponse(RecurringJournalSchedule x) => new(x.Id, x.LegalEntityId, x.DocumentType, x.Description, x.NextRunDate, x.EndDate, x.IsActive, x.BranchId, x.CurrencyCode, x.FrequencyMonths, x.Lines.OrderBy(l => l.LineNumber).Select(l => new RecurringJournalScheduleLineResponse(l.LineNumber, l.AccountId, l.Description, l.Debit, l.Credit)).ToArray());
}
