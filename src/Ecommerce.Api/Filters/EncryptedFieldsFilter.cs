using Ecommerce.Api.Contracts;
using Ecommerce.Api.Extensions;
using Ecommerce.Application.Abstractions;
using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ecommerce.Api.Filters;

/// <summary>
/// Marks an action whose body carries encrypted sensitive fields (api-conventions "Encrypted sensitive fields"): the
/// headers encryptionKeyId and encryptedKey are mandatory and the body is decrypted before any validation.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EncryptedFieldsAttribute() : TypeFilterAttribute(typeof(EncryptedFieldsFilter));

/// <summary>Replaces the bound request with its decrypted copy; every decrypt failure is the same 400 E10001.</summary>
public sealed class EncryptedFieldsFilter(IFieldTransport transport) : IAsyncActionFilter
{
    public const string KeyIdHeader = "encryptionKeyId";
    public const string KeyHeader = "encryptedKey";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var headers = context.HttpContext.Request.Headers;
        using var session = transport.Open(headers[KeyIdHeader].ToString(), headers[KeyHeader].ToString());
        var argument = context.ActionArguments.FirstOrDefault(a => a.Value is IEncryptedRequest);

        if (session is not null && argument.Value is IEncryptedRequest request)
        {
            try
            {
                context.ActionArguments[argument.Key] = request.Decrypt(new EncryptedFields(session));
                await next();
                return;
            }
            catch (FieldDecryptionException)
            {
                // fall through to the single answer below
            }
        }

        var envelope = HttpEnvelope.Failure(context.HttpContext, Error.Validation("Body.Encrypted", "Encrypted fields could not be decrypted."));
        context.Result = new ObjectResult(envelope) { StatusCode = envelope.Status.Code };
    }
}
