using Ecommerce.Api.Contracts;
using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Extensions;

public static class ResultExtensions
{
    /// <summary>Turns a failed result into the spec's status envelope. The first error decides the status and bizCode.</summary>
    public static ObjectResult ToFailure(this Result result, ControllerBase controller)
    {
        var envelope = HttpEnvelope.Failure(controller.HttpContext, result.FirstError);
        return new ObjectResult(envelope) { StatusCode = envelope.Status.Code };
    }

    /// <summary>Wraps a successful payload in the spec's status envelope (bizCode S10000).</summary>
    public static ObjectResult ToSuccess<T>(this ControllerBase controller, int httpCode, string message, string bizMessage, T? data)
    {
        var http = controller.HttpContext;
        var envelope = ApiEnvelope.Success(httpCode, message, bizMessage, HttpEnvelope.RequestId(http), HttpEnvelope.NowUtc(http), data);
        return new ObjectResult(envelope) { StatusCode = httpCode };
    }

}
