using Ecommerce.Api.Extensions;
using Ecommerce.Shared.Results;
using Ecommerce.Shared.Validation;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Filters;

/// <summary>
/// A body that is not valid JSON for the request shape (an array where an object belongs, a string for a boolean):
/// 400 E10001 with the field in the convention form, for example <c>consents[1]: Result must be a boolean.</c>
/// </summary>
public static class InvalidBody
{
    public static IActionResult Response(ActionContext context)
    {
        var failed = context.ModelState.Where(e => e.Value?.Errors.Count > 0).ToList();
        // A JSON error also leaves "<parameter> is required" under the parameter name; the JSON path names the real field.
        if (failed.Any(e => e.Key.StartsWith('$'))) failed = failed.Where(e => e.Key.StartsWith('$')).ToList();
        var messages = failed.Select(e => Message(e.Key, e.Value!.Errors[0])).Distinct().ToList();
        var message = messages.Count == 1 ? messages[0] : ValidationMessages.Combine(messages);
        var envelope = HttpEnvelope.Failure(context.HttpContext, Error.Validation("Body.Invalid", message));
        return new ObjectResult(envelope) { StatusCode = envelope.Status.Code };
    }

    private static string Message(string key, Microsoft.AspNetCore.Mvc.ModelBinding.ModelError error)
    {
        // Keys look like "$.consents[1].result", "$.addresses" or "$" / "request" for the whole body.
        var path = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : "";
        if (path.Length == 0) return ValidationMessages.JsonType("requestBody", "an object");

        var lastDot = path.LastIndexOf('.');
        var field = path[(lastDot + 1)..];
        var bracket = field.IndexOf('[');
        if (bracket >= 0) field = field[..bracket];
        var message = ValidationMessages.JsonType(field, JsonTypeOf(error));
        return lastDot < 0 ? message : ValidationMessages.At(path[..lastDot], message);
    }

    private static string JsonTypeOf(Microsoft.AspNetCore.Mvc.ModelBinding.ModelError error)
    {
        var text = error.Exception?.Message ?? error.ErrorMessage;
        if (text.Contains("System.Boolean", StringComparison.Ordinal)) return "a boolean";
        if (text.Contains("System.String", StringComparison.Ordinal)) return "a string";
        if (new[] { "System.Int32", "System.Int64", "System.Decimal", "System.Double" }.Any(t => text.Contains(t, StringComparison.Ordinal))) return "a number";
        if (text.Contains("List`1", StringComparison.Ordinal) || text.Contains("[]", StringComparison.Ordinal)) return "an array";
        return "an object";
    }
}
