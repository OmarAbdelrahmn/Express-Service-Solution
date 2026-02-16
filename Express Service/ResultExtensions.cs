using Application.Abstraction;
using Microsoft.AspNetCore.Mvc;
using ObjectResult = Microsoft.AspNetCore.Mvc.ObjectResult;

namespace Express_Service;

public static class ResultExtensions
{
    public static ObjectResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("Cannot convert a successful result to a problem.");

        // Create ProblemDetails directly since Result.Problem does not exist
        var problemDetails = new ProblemDetails
        {
            Status = result.Error.StatuesCode,
            Title = result.Error.Code,
            Detail = result.Error.Description
        };

        problemDetails.Extensions = new Dictionary<string, object?>
        {
            { "error", new { result.Error.Code , result.Error.Description } }
        };

        return new ObjectResult(problemDetails);
    }
}

