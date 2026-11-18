using Ecommerce.Api.Contracts;
using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Extensions;

public static class ResultExtensions
{
    /// <summary>Turns a failed result into the spec's status envelope. The first error decides the status and bizCode.</summary>
    public static ObjectResult ToFailure(this Result result, ControllerBase controller)
    {
        var envelope = ApiEnvelope.Failure(result.FirstError, RequestId(controller), Now(controller));
        return new ObjectResult(envelope) { StatusCode = envelope.Status.Code };
    }

    /// <summary>Wraps a successful payload in the spec's status envelope (bizCode S10000).</summary>
    public static ObjectResult ToSuccess<T>(this ControllerBase controller, int httpCode, string message, string bizMessage, T? data)
    {
        var envelope = ApiEnvelope.Success(httpCode, message, bizMessage, RequestId(controller), Now(controller), data);
        return new ObjectResult(envelope) { StatusCode = httpCode };
    }

    private static DateTime Now(ControllerBase controller) =>
        controller.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    // The caller's requestId header is echoed; without one the API makes its own.
    private static string RequestId(ControllerBase controller) =>
        controller.Request.Headers.TryGetValue("requestId", out var id) && !string.IsNullOrWhiteSpace(id)
            ? id.ToString()
            : $"req-{Guid.NewGuid()}";
}
