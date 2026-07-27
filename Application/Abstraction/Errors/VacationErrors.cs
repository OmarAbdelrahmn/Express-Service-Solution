using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class VacationErrors
{
    public static readonly Error AccessDenied = new("Vacation.AccessDenied", "You are not allowed to perform this vacation action.", StatusCodes.Status403Forbidden);
    public static readonly Error NotFound = new("Vacation.NotFound", "Vacation request was not found.", StatusCodes.Status404NotFound);
    public static readonly Error RiderNotFound = new("Vacation.RiderNotFound", "Rider was not found in your housing.", StatusCodes.Status404NotFound);
    public static readonly Error InvalidState = new("Vacation.InvalidState", "The vacation request cannot be changed in its current state.", StatusCodes.Status409Conflict);
    public static readonly Error WorkflowPaused = new("Vacation.WorkflowPaused", "A date change or cancellation request is waiting for Master review.", StatusCodes.Status409Conflict);
    public static readonly Error Overlap = new("Vacation.Overlap", "The rider already has an overlapping pending, approved, or active vacation.", StatusCodes.Status409Conflict);
    public static readonly Error ConcurrentUpdate = new("Vacation.ConcurrentUpdate", "The vacation request changed while it was being processed. Refresh and try again.", StatusCodes.Status409Conflict);
}
