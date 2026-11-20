using System.Globalization;
using Ecommerce.Api.Extensions;
using Ecommerce.Shared.Results;
using Ecommerce.Shared.Validation;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Ecommerce.Api.Filters;

/// <summary>
/// The mandatory headers of every spec, checked before the body is read: 400 E10001, never 415
/// (api-conventions.md "Validation messages", Josh 2026-11-18). content-type is checked only when a body is sent.
/// </summary>
public sealed class RequiredHeadersFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var encrypted = context.ActionDescriptor.EndpointMetadata.OfType<EncryptedFieldsAttribute>().Any();
        var messages = Check(context.HttpContext.Request, encrypted).ToList();
        if (messages.Count == 0)
        {
            await next();
            return;
        }

        var message = messages.Count == 1 ? messages[0] : ValidationMessages.Combine(messages);
        var envelope = HttpEnvelope.Failure(context.HttpContext, Error.Validation("Header.Invalid", message));
        context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(envelope) { StatusCode = envelope.Status.Code };
    }

    private static IEnumerable<string> Check(HttpRequest request, bool encrypted)
    {
        // Only a request that carries a body must declare JSON (logout has no body and no content-type).
        if (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0)
        {
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type) || !type.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                yield return "Content-Type must be application/json.";
        }

        var timestamp = request.Headers["timestamp"].ToString();
        if (string.IsNullOrWhiteSpace(timestamp)) yield return "Timestamp is a required header.";
        else if (!IsUtcTimestamp(timestamp)) yield return "Timestamp must be a valid UTC timestamp.";

        if (!encrypted) yield break;
        if (string.IsNullOrWhiteSpace(request.Headers[EncryptedFieldsFilter.KeyIdHeader])) yield return "Encryption Key Id is a required header.";
        if (string.IsNullOrWhiteSpace(request.Headers[EncryptedFieldsFilter.KeyHeader])) yield return "Encrypted Key is a required header.";
    }

    private static bool IsUtcTimestamp(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
        && parsed.Offset == TimeSpan.Zero
        && (value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal));
}
