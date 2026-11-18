using System.Globalization;
using System.Threading.RateLimiting;
using Ecommerce.Api.Contracts;
using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.RateLimiting;

namespace Ecommerce.Api.Security;

/// <summary>Per client IP limits of the public APIs (api-conventions.md "Limits and rate limiting").</summary>
public static class RateLimiting
{
    /// <summary>register, login, reset-password, create-new-password: 10 requests per minute by default.</summary>
    public const string Strict = "public-strict";

    /// <summary>refresh-token, consent/active, storefront and parameter inquiry: 60 requests per minute by default.</summary>
    public const string Standard = "public-standard";

    public static IServiceCollection AddPublicRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var strict = configuration.GetValue("RateLimit:StrictPerMinute", 10);
        var standard = configuration.GetValue("RateLimit:PerMinute", 60);

        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Strict, context => PerIp(context, strict));
            options.AddPolicy(Standard, context => PerIp(context, standard));
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : 60;
                http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var error = Error.Of("RateLimit.Exceeded", "Too many requests. Try again later.", ErrorType.TooManyRequests, "E90002");
                var requestId = http.Request.Headers.TryGetValue("requestId", out var id) && !string.IsNullOrWhiteSpace(id)
                    ? id.ToString()
                    : $"req-{Guid.NewGuid()}";
                var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
                await http.Response.WriteAsJsonAsync(ApiEnvelope.Failure(error, requestId, now), cancellationToken);
            };
        });
    }

    // shortcut: the client IP is the connection's address; behind a proxy, configure forwarded headers in deployment.
    private static RateLimitPartition<string> PerIp(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
