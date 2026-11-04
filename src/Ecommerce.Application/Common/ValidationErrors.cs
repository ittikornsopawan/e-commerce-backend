using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Common;

internal static class ValidationErrors
{
    /// <summary>Collects the errors of every failed result, so a validator can report all problems at once.</summary>
    public static IReadOnlyList<Error> From(params Result[] results) =>
        results.Where(result => result.IsFailure).SelectMany(result => result.Errors).ToList();
}
