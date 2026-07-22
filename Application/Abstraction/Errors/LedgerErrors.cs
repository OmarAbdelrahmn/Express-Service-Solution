using Application.Abstraction;
using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class LedgerErrors
{
    public static readonly Error LegalEntityNotFound = new("Ledger.LegalEntityNotFound", "The legal entity was not found.", StatusCodes.Status404NotFound);
    public static readonly Error AccountNotFound = new("Ledger.AccountNotFound", "An account was not found in the legal entity chart.", StatusCodes.Status404NotFound);
    public static readonly Error DimensionNotFound = new("Ledger.DimensionNotFound", "The financial dimension was not found.", StatusCodes.Status404NotFound);
    public static readonly Error PostingProfileNotFound = new("Ledger.PostingProfileNotFound", "The posting profile was not found.", StatusCodes.Status404NotFound);
    public static readonly Error FiscalYearNotFound = new("Ledger.FiscalYearNotFound", "The fiscal year was not found.", StatusCodes.Status404NotFound);
    public static readonly Error RecurringScheduleNotFound = new("Ledger.RecurringScheduleNotFound", "The recurring journal schedule was not found.", StatusCodes.Status404NotFound);
    public static readonly Error AccountNotPostable = new("Ledger.AccountNotPostable", "The selected account cannot be posted manually.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error DuplicateCode = new("Ledger.DuplicateCode", "The code already exists in this legal entity.", StatusCodes.Status409Conflict);
    public static readonly Error InvalidPeriod = new("Ledger.InvalidPeriod", "The fiscal period does not cover the requested date or is closed.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error InvalidQuery = new("Ledger.InvalidQuery", "The ledger query contains an invalid date range or sort direction.", StatusCodes.Status400BadRequest);
    public static readonly Error InvalidJournal = new("Ledger.InvalidJournal", "Journal lines must balance and each line must contain exactly one positive side.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error DocumentNotFound = new("Ledger.DocumentNotFound", "The financial document was not found.", StatusCodes.Status404NotFound);
    public static readonly Error InvalidTransition = new("Ledger.InvalidTransition", "The financial document cannot make this state transition.", StatusCodes.Status409Conflict);
    public static readonly Error MakerCheckerViolation = new("Ledger.MakerCheckerViolation", "The document creator cannot approve or post the same document.", StatusCodes.Status403Forbidden);
    public static readonly Error IdempotencyConflict = new("Ledger.IdempotencyConflict", "The idempotency key has already been used for a different document.", StatusCodes.Status409Conflict);
    public static readonly Error ReversalExists = new("Ledger.ReversalExists", "The posted journal has already been reversed.", StatusCodes.Status409Conflict);
    public static readonly Error AccessDenied = new("Ledger.AccessDenied", "The current user does not have the required permission for this legal entity.", StatusCodes.Status403Forbidden);
    public static readonly Error FinancialUserNotFound = new("Ledger.FinancialUserNotFound", "The financial user was not found.", StatusCodes.Status404NotFound);
    public static readonly Error MissingPostingRoute = new("Ledger.MissingPostingRoute", "The effective posting profile does not contain every requested event route.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error ExchangeRateMissing = new("Ledger.ExchangeRateMissing", "No effective exchange rate exists for the transaction date and base currency.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error RequiredDimensionMissing = new("Ledger.RequiredDimensionMissing", "A required financial dimension is missing or ambiguous.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error PeriodLocked = new("Ledger.PeriodLocked", "The fiscal period is not open for this accounting module.", StatusCodes.Status409Conflict);
}
