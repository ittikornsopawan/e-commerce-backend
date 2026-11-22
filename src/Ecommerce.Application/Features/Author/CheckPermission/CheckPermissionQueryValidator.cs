using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.CheckPermission;

/// <summary>Spec 17 query rules, before the JWT and SQL: a mandatory permission claim and an optional scope claim (an empty value is wrong).</summary>
public sealed class CheckPermissionQueryValidator : IValidator<CheckPermissionQuery>
{
    public IReadOnlyList<Error> Validate(CheckPermissionQuery q)
    {
        var messages = new List<string>();
        if (q.Permission is null || !ClaimForm.IsPermission(q.Permission)) messages.Add("Permission is required and must be a permission claim such as catalog:manage.");
        if (q.Scope is not null && !ClaimForm.IsScope(q.Scope)) messages.Add("Scope must be a scope claim such as shop:*.");
        return messages.Count == 0 ? [] : [Error.Validation("Check.Query", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
