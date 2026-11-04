using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Extensions;

public static class ResultExtensions
{
    /// <summary>Turns a failed result into an RFC 9457 problem response. The first error decides the status code.</summary>
    public static ObjectResult ToProblem(this Result result, ControllerBase controller)
    {
        var first = result.FirstError;
        var status = first.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = first.Code,
            Detail = first.Description,
            Instance = controller.HttpContext.Request.Path
        };

        problem.Extensions["errors"] = result.Errors
            .Select(error => new { code = error.Code, description = error.Description })
            .ToArray();

        return new ObjectResult(problem) { StatusCode = status };
    }
}
