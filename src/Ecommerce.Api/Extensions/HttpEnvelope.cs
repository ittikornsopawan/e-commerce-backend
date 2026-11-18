using Ecommerce.Api.Contracts;
using Ecommerce.Shared.Results;

namespace Ecommerce.Api.Extensions;

/// <summary>One place for the envelope of every response written outside a controller action (middleware, filters).</summary>
public static class HttpEnvelope
{
    /// <summary>The caller's <c>requestId</c> header, or a new one (spec: "generate requestId if omitted").</summary>
    public static string RequestId(HttpContext http) =>
        http.Request.Headers.TryGetValue("requestId", out var id) && !string.IsNullOrWhiteSpace(id) ? id.ToString() : $"req-{Guid.NewGuid()}";

    public static DateTime NowUtc(HttpContext http) =>
        http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    public static ApiEnvelope<object> Failure(HttpContext http, Error error) => ApiEnvelope.Failure(error, RequestId(http), NowUtc(http));

    public static Task WriteFailureAsync(HttpContext http, Error error, CancellationToken cancellationToken = default)
    {
        var envelope = Failure(http, error);
        http.Response.StatusCode = envelope.Status.Code;
        return http.Response.WriteAsJsonAsync(envelope, cancellationToken);
    }
}
