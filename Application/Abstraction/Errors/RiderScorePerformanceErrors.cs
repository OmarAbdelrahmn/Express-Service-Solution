using Microsoft.AspNetCore.Http;

namespace Application.Abstraction.Errors;

public static class RiderScorePerformanceErrors
{
    public static readonly Error NotFound = new(
        "RiderScorePerformance.NotFound",
        "Rider score performance record was not found.",
        StatusCodes.Status404NotFound);

    public static readonly Error Duplicate = new(
        "RiderScorePerformance.Duplicate",
        "A daily performance record already exists for this rider and date.",
        StatusCodes.Status409Conflict);

    public static Error RiderNotFound(string workingId) => new(
        "RiderScorePerformance.RiderNotFound",
        $"No rider was found with working ID {workingId}.",
        StatusCodes.Status404NotFound);

    public static Error DeletedRider(string workingId) => new(
        "RiderScorePerformance.DeletedRider",
        $"Working ID {workingId} belongs to a deleted employee and cannot receive a performance record.",
        StatusCodes.Status409Conflict);

    public static Error InvalidRequest(string message) => new(
        "RiderScorePerformance.InvalidRequest",
        message,
        StatusCodes.Status400BadRequest);

    public static Error InvalidExcel(string message) => new(
        "RiderScorePerformance.InvalidExcel",
        message,
        StatusCodes.Status400BadRequest);

    public static Error ServerError(string message) => new(
        "RiderScorePerformance.ServerError",
        message,
        StatusCodes.Status500InternalServerError);
}
