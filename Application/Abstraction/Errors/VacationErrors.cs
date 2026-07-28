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
    public static readonly Error HrNotReady = new("Vacation.HrNotReady", "HR work is available only after all three approval stages are approved.", StatusCodes.Status409Conflict);
    public static readonly Error TicketRequired = new("Vacation.TicketRequired", "Complete the ticket task before completing the exit/re-entry visa task.", StatusCodes.Status409Conflict);
    public static readonly Error DocumentNotFound = new("Vacation.DocumentNotFound", "Vacation HR document was not found.", StatusCodes.Status404NotFound);
    public static readonly Error InvalidDocument = new("Vacation.InvalidDocument", "Upload a PDF, JPG, PNG, or WEBP file no larger than 20 MB.", StatusCodes.Status400BadRequest);
}
