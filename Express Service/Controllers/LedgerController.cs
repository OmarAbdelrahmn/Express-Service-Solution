using Application.Contracts.Common;
using Application.Contracts.Ledger;
using Application.Extensions;
using Application.Service.Ledger;
using Domain.Entities.AccountingCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/ledger")]
[ApiController]
[Authorize(Roles = "Master,Accountant")]
public class LedgerController(ILedgerService service) : ControllerBase
{
    [HttpGet("currencies")]
    public async Task<IActionResult> GetCurrencies([FromQuery] bool? active, [FromQuery] string? search, CancellationToken ct)
        => ToAction(await service.GetCurrenciesAsync(active, search, ct));

    [HttpPost("currencies")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateCurrency([FromBody] CreateCurrencyRequest request, CancellationToken ct) => await WithActor(actor => service.CreateCurrencyAsync(request, actor, ct));

    [HttpPost("exchange-rates")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateExchangeRate([FromBody] CreateExchangeRateRequest request, CancellationToken ct) => await WithActor(actor => service.CreateExchangeRateAsync(request, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/exchange-rates")]
    public async Task<IActionResult> GetExchangeRates(int legalEntityId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] string? fromCurrencyCode = null, [FromQuery] string? toCurrencyCode = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        [FromQuery] string? sortBy = null, [FromQuery] string sortDirection = "desc")
        => await WithActor(actor => service.GetExchangeRatesAsync(legalEntityId, pagination, fromCurrencyCode, toCurrencyCode, fromDate, toDate, sortBy, sortDirection, actor, ct));

    [HttpPost("dimensions")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateDimension([FromBody] CreateFinancialDimensionRequest request, CancellationToken ct) => await WithActor(actor => service.CreateDimensionAsync(request, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/dimensions")]
    public async Task<IActionResult> GetDimensions(int legalEntityId, [FromQuery] bool? active, [FromQuery] string? search, CancellationToken ct)
        => await WithActor(actor => service.GetDimensionsAsync(legalEntityId, active, search, actor, ct));

    [HttpPost("dimension-values")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateDimensionValue([FromBody] CreateFinancialDimensionValueRequest request, CancellationToken ct) => await WithActor(actor => service.CreateDimensionValueAsync(request, actor, ct));

    [HttpGet("dimensions/{financialDimensionId:int}/values")]
    public async Task<IActionResult> GetDimensionValues(int financialDimensionId, [FromQuery] bool? active, [FromQuery] string? search, CancellationToken ct)
        => await WithActor(actor => service.GetDimensionValuesAsync(financialDimensionId, active, search, actor, ct));

    [HttpPost("recurring-journal-schedules")]
    public async Task<IActionResult> CreateRecurringSchedule([FromBody] CreateRecurringJournalScheduleRequest request, CancellationToken ct) => await WithActor(actor => service.CreateRecurringScheduleAsync(request, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/recurring-journal-schedules")]
    public async Task<IActionResult> GetRecurringSchedules(int legalEntityId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] bool? active = null, [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string sortDirection = "desc")
        => await WithActor(actor => service.GetRecurringSchedulesAsync(legalEntityId, pagination, active, fromDate, toDate, search, sortBy, sortDirection, actor, ct));

    [HttpGet("recurring-journal-schedules/{id:guid}")]
    public async Task<IActionResult> GetRecurringSchedule(Guid id, CancellationToken ct)
        => await WithActor(actor => service.GetRecurringScheduleAsync(id, actor, ct));

    [HttpPost("recurring-journal-schedules/generate")]
    public async Task<IActionResult> GenerateDueSchedules([FromQuery] DateOnly throughDate, CancellationToken ct) => await WithActor(actor => service.GenerateDueSchedulesAsync(throughDate, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/accounts")]
    public async Task<IActionResult> GetAccounts(int legalEntityId, CancellationToken ct) => await WithActor(actor => service.GetAccountsAsync(legalEntityId, actor, ct));

    [HttpPost("accounts")]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAccountingAccountRequest request, CancellationToken ct)
        => await WithActor(actor => service.CreateAccountAsync(request, actor, ct));

    [HttpPost("posting-profiles")]
    public async Task<IActionResult> CreatePostingProfile([FromBody] CreatePostingProfileRequest request, CancellationToken ct)
        => await WithActor(actor => service.CreatePostingProfileAsync(request, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/posting-profiles")]
    public async Task<IActionResult> GetPostingProfiles(int legalEntityId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] bool? active = null, [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string sortDirection = "desc")
        => await WithActor(actor => service.GetPostingProfilesAsync(legalEntityId, pagination, active, fromDate, toDate, search, sortBy, sortDirection, actor, ct));

    [HttpGet("posting-profiles/{id:int}")]
    public async Task<IActionResult> GetPostingProfile(int id, CancellationToken ct)
        => await WithActor(actor => service.GetPostingProfileAsync(id, actor, ct));

    [HttpPost("fiscal-years")]
    public async Task<IActionResult> CreateFiscalYear([FromBody] CreateFiscalYearRequest request, CancellationToken ct)
        => await WithActor(actor => service.CreateFiscalYearAsync(request, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/fiscal-years")]
    public async Task<IActionResult> GetFiscalYears(int legalEntityId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] string? sortBy = null, [FromQuery] string sortDirection = "desc")
        => await WithActor(actor => service.GetFiscalYearsAsync(legalEntityId, pagination, sortBy, sortDirection, actor, ct));

    [HttpGet("fiscal-years/{fiscalYearId:int}")]
    public async Task<IActionResult> GetFiscalYear(int fiscalYearId, CancellationToken ct) => await WithActor(actor => service.GetFiscalYearAsync(fiscalYearId, actor, ct));

    [HttpPost("fiscal-periods/{periodId:int}/close")]
    public async Task<IActionResult> ClosePeriod(int periodId, [FromBody] ChangeFiscalPeriodStatusRequest request, CancellationToken ct) => await WithActor(actor => service.ClosePeriodAsync(periodId, request, actor, ct));

    [HttpPost("fiscal-periods/{periodId:int}/soft-close")]
    public async Task<IActionResult> SoftClosePeriod(int periodId, [FromBody] ChangeFiscalPeriodStatusRequest request, CancellationToken ct) => await WithActor(actor => service.SoftClosePeriodAsync(periodId, request, actor, ct));

    [HttpPost("fiscal-periods/{periodId:int}/reopen")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> ReopenPeriod(int periodId, [FromBody] ChangeFiscalPeriodStatusRequest request, CancellationToken ct) => await WithActor(actor => service.ReopenPeriodAsync(periodId, request, actor, ct));

    [HttpPost("manual-journals")]
    public async Task<IActionResult> CreateManualJournal([FromBody] CreateManualJournalRequest request, CancellationToken ct)
        => await WithActor(actor => service.CreateManualJournalAsync(request, actor, ct));

    [HttpGet("documents/{documentId:guid}")]
    public async Task<IActionResult> GetDocument(Guid documentId, CancellationToken ct) => await WithActor(actor => service.GetDocumentAsync(documentId, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/documents")]
    public async Task<IActionResult> GetDocuments(int legalEntityId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] FinancialDocumentStatus? status = null, [FromQuery] string? documentType = null,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null, [FromQuery] string? search = null,
        [FromQuery] string? reference = null, [FromQuery] string? sortBy = null, [FromQuery] string sortDirection = "desc")
        => await WithActor(actor => service.GetDocumentsAsync(legalEntityId, pagination, status, documentType, fromDate, toDate, search, reference, sortBy, sortDirection, actor, ct));

    [HttpPost("documents/{documentId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid documentId, CancellationToken ct) => await WithActor(actor => service.SubmitDocumentAsync(documentId, actor, ct));

    [HttpPost("documents/{documentId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid documentId, [FromBody] ApproveDocumentRequest request, CancellationToken ct)
        => await WithActor(actor => service.ApproveDocumentAsync(documentId, request, actor, ct));

    [HttpPost("documents/{documentId:guid}/post")]
    public async Task<IActionResult> Post(Guid documentId, CancellationToken ct) => await WithActor(actor => service.PostDocumentAsync(documentId, actor, ct));

    [HttpPost("documents/{documentId:guid}/reversals")]
    public async Task<IActionResult> CreateReversal(Guid documentId, [FromBody] ReverseJournalRequest request, CancellationToken ct)
        => await WithActor(actor => service.CreateReversalAsync(documentId, request, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/journal-entries")]
    public async Task<IActionResult> GetJournalEntries(int legalEntityId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null, [FromQuery] int? accountId = null,
        [FromQuery] Guid? documentId = null, [FromQuery] string? search = null, [FromQuery] string? sortBy = null,
        [FromQuery] string sortDirection = "desc")
        => await WithActor(actor => service.GetJournalEntriesAsync(legalEntityId, pagination, fromDate, toDate, accountId, documentId, search, sortBy, sortDirection, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/approval-inbox")]
    public async Task<IActionResult> ApprovalInbox(int legalEntityId, CancellationToken ct) => await WithActor(actor => service.GetApprovalInboxAsync(legalEntityId, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/trial-balance")]
    public async Task<IActionResult> TrialBalance(int legalEntityId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct)
        => await WithActor(actor => service.GetTrialBalanceAsync(legalEntityId, fromDate, toDate, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/profit-and-loss")]
    public async Task<IActionResult> ProfitAndLoss(int legalEntityId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct)
        => await WithActor(actor => service.GetProfitAndLossAsync(legalEntityId, fromDate, toDate, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/balance-sheet")]
    public async Task<IActionResult> BalanceSheet(int legalEntityId, [FromQuery] DateOnly asOfDate, CancellationToken ct)
        => await WithActor(actor => service.GetBalanceSheetAsync(legalEntityId, asOfDate, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/cash-movement")]
    public async Task<IActionResult> CashMovement(int legalEntityId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct)
        => await WithActor(actor => service.GetCashMovementAsync(legalEntityId, fromDate, toDate, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/dimensions/{financialDimensionId:int}/balances")]
    public async Task<IActionResult> DimensionBalances(int legalEntityId, int financialDimensionId, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, CancellationToken ct)
        => await WithActor(actor => service.GetDimensionBalanceAsync(legalEntityId, financialDimensionId, fromDate, toDate, actor, ct));

    [HttpGet("legal-entities/{legalEntityId:int}/audit-events")]
    public async Task<IActionResult> AuditEvents(int legalEntityId, [FromQuery] int take, CancellationToken ct)
        => await WithActor(actor => service.GetAuditEventsAsync(legalEntityId, take, actor, ct));

    private async Task<IActionResult> WithActor<T>(Func<string, Task<Application.Abstraction.Result<T>>> action)
    { var actor = User.GetUserId(); return string.IsNullOrWhiteSpace(actor) ? Unauthorized() : ToAction(await action(actor)); }
    private async Task<IActionResult> WithActor(Func<string, Task<Application.Abstraction.Result>> action)
    { var actor = User.GetUserId(); return string.IsNullOrWhiteSpace(actor) ? Unauthorized() : ToAction(await action(actor)); }
    private IActionResult ToAction<T>(Application.Abstraction.Result<T> result) => result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    private IActionResult ToAction(Application.Abstraction.Result result) => result.IsSuccess ? Ok() : result.ToProblem();
}
