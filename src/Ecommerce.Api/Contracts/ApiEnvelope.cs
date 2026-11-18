using System.Globalization;
using Ecommerce.Shared.Results;

namespace Ecommerce.Api.Contracts;

/// <summary>The <c>status</c> object of every API response (api-conventions.md, response examples of the specs).</summary>
public sealed record ApiStatus(
    int Code, string Message, string? BizCode, string BizMessage, string RequestId, string Timestamp, string Status);

public sealed record ApiEnvelope<T>(ApiStatus Status, T? Data);

public static class ApiEnvelope
{
    public const string SuccessCode = "S10000";

    public static ApiEnvelope<T> Success<T>(int httpCode, string message, string bizMessage, string requestId, DateTime nowUtc, T? data) =>
        new(new ApiStatus(httpCode, message, SuccessCode, bizMessage, requestId, Format(nowUtc), "SUCCESS"), data);

    public static ApiEnvelope<object> Failure(Error error, string requestId, DateTime nowUtc)
    {
        var (code, message, bizCode) = error.Type switch
        {
            ErrorType.Validation => (400, "Bad Request", "E10001"),
            ErrorType.Unauthorized => (401, "Unauthorized", "E10002"),
            ErrorType.Forbidden => (403, "Forbidden", "E10009"),
            ErrorType.NotFound => (404, "Not Found", "E10004"),
            ErrorType.Conflict => (409, "Conflict", "E10003"),
            _ => (500, "Internal Server Error", (string?)null)
        };

        // An unexpected failure never leaks its description (spec: "An unexpected error occurred.", no bizCode).
        var bizMessage = code == 500 ? "An unexpected error occurred." : error.Description;
        return new(new ApiStatus(code, message, error.BizCode ?? bizCode, bizMessage, requestId, Format(nowUtc), "ERROR"), null);
    }

    private static string Format(DateTime nowUtc) =>
        nowUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
