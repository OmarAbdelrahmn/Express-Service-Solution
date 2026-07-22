using Application.Abstraction;
using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class FinancialOperationsErrors
{
    public static readonly Error NotFound = new("FinancialOperations.NotFound", "The financial operation record was not found.", StatusCodes.Status404NotFound);
    public static readonly Error InvalidRequest = new("FinancialOperations.InvalidRequest", "The request is incomplete or violates a financial control.", StatusCodes.Status422UnprocessableEntity);
    public static readonly Error InvalidState = new("FinancialOperations.InvalidState", "The record cannot make this state transition.", StatusCodes.Status409Conflict);
    public static readonly Error Duplicate = new("FinancialOperations.Duplicate", "A record with the same business reference already exists.", StatusCodes.Status409Conflict);
    public static readonly Error EvidenceNotAccepted = new("FinancialOperations.EvidenceNotAccepted", "The source evidence must be accepted before it can support a financial record.", StatusCodes.Status409Conflict);
    public static readonly Error AllocationExceedsBalance = new("FinancialOperations.AllocationExceedsBalance", "The allocation exceeds the unapplied payment or open invoice balance.", StatusCodes.Status422UnprocessableEntity);
}
