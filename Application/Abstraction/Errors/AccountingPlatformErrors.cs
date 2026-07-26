using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class AccountingPlatformErrors
{
    public static readonly Error NotFound = new("Accounting.NotFound", "The requested accounting record was not found.", StatusCodes.Status404NotFound);
    public static readonly Error Duplicate = new("Accounting.Duplicate", "The same business key or content hash already exists.", StatusCodes.Status409Conflict);
    public static readonly Error InvalidState = new("Accounting.InvalidState", "The record cannot make this state transition.", StatusCodes.Status409Conflict);
    public static readonly Error ConcurrencyConflict = new("Accounting.ConcurrencyConflict", "The record was changed by another request. Refresh and retry.", StatusCodes.Status409Conflict);
    public static readonly Error InvalidRequest = new("Accounting.InvalidRequest", "The request violates an accounting rule.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error PayrollLegalEntityNotFound = new("Payroll.LegalEntityNotFound", "The selected legal entity does not exist or is inactive.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error PayrollCurrencyNotFound = new("Payroll.CurrencyNotFound", "The selected payroll currency does not exist or is inactive.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error PayrollPeriodInvalid = new("Payroll.PeriodInvalid", "The payroll period end date must be on or after the start date.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error IdempotencyKeyRequired = new("Accounting.IdempotencyKeyRequired", "The Idempotency-Key header is required.", StatusCodes.Status400BadRequest);
    public static readonly Error PolicyOverlap = new("Compensation.PolicyOverlap", "An active compensation policy overlaps this effective period.", StatusCodes.Status409Conflict);
    public static readonly Error UnsupportedMetric = new("Compensation.UnsupportedMetric", "A rule references a metric that is not on the normalized metric allowlist.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error BlockingImportIssues = new("PlatformImport.BlockingIssues", "Resolve all blocking import issues and reconciliation differences before approval.", StatusCodes.Status409Conflict);
    public static readonly Error SchemaDrift = new("PlatformImport.SchemaDrift", "The workbook schema does not match an active certified template.", StatusCodes.Status409Conflict);
    public static readonly Error StorageUnavailable = new("AccountingStorage.Unavailable", "Private accounting storage is not configured or could not process the file.", StatusCodes.Status503ServiceUnavailable);
    public static readonly Error InvalidFile = new("AccountingStorage.InvalidFile", "The uploaded file signature, size, or content is invalid.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error PayrollFactsMissing = new("Payroll.FactsMissing", "No approved and resolved platform facts are available for this payroll period.", StatusCodes.Status409Conflict);
    public static readonly Error PayrollPolicyMissing = new("Payroll.PolicyMissing", "One or more rider/platform groups do not have an active effective compensation policy.", StatusCodes.Status409Conflict);
    public static readonly Error PayrollValidityRequired = new("Payroll.ValidityRequired", "A Keeta segment component is missing a valid eligibility result and must be resolved or overridden before payroll.", StatusCodes.Status409Conflict);
    public static readonly Error InvalidIban = new("Payroll.InvalidIban", "A bank payment contains a missing or invalid Saudi IBAN.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error PaymentExceedsUnpaid = new("Payroll.PaymentExceedsUnpaid", "The payment allocation exceeds the rider's approved unpaid payroll balance.", StatusCodes.Status409Conflict);
    public static readonly Error HousingCashMemberRequired = new("RiderCash.MemberRequired", "Housing cash delivery access can only be granted to a user with the Member role.", StatusCodes.Status422UnprocessableEntity);
}
