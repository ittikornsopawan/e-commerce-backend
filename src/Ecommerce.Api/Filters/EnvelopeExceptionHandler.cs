using Ecommerce.Api.Extensions;
using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.Diagnostics;

namespace Ecommerce.Api.Filters;

/// <summary>An unexpected failure answers the spec's generic 500 (no bizCode, "An unexpected error occurred.") and is logged.</summary>
public sealed class EnvelopeExceptionHandler(ILogger<EnvelopeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
        await HttpEnvelope.WriteFailureAsync(http, Error.Failure("Unexpected", exception.GetType().Name), cancellationToken);
        return true;
    }
}
