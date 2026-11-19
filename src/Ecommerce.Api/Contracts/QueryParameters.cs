using Ecommerce.Shared.Validation;

namespace Ecommerce.Api.Contracts;

/// <summary>
/// Reads the query string of a GET with no body (spec 10): every key known (exact case), each given once, and no body.
/// Returns the shape error message, or null with the present values (an omitted key is absent, never defaulted here).
/// </summary>
public static class QueryParameters
{
    public const string BodyNotAllowed = "Request Body must be empty.";

    public static string? TryRead(HttpRequest request, IReadOnlySet<string> keys, out IReadOnlyDictionary<string, string> values)
    {
        var read = new Dictionary<string, string>();
        values = read;
        var messages = new List<string>();
        if (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0) messages.Add(BodyNotAllowed);

        foreach (var (key, value) in request.Query)
        {
            // The query collection merges keys case-insensitively, so "id" and "ID" arrive as one repeated key.
            if (!keys.Contains(key)) messages.Add(ValidationMessages.UnknownField(key));
            else if (value.Count != 1) messages.Add(ValidationMessages.Duplicated(key));
            else read[key] = value.ToString();
        }

        return messages.Count switch { 0 => null, 1 => messages[0], _ => ValidationMessages.Combine(messages) };
    }
}
