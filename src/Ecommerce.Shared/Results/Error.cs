namespace Ecommerce.Shared.Results;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Timeout,
    TooManyRequests
}

/// <param name="BizCode">The API business code when the default of <paramref name="Type"/> does not fit, such as E10005 for a missing reference.</param>
public sealed record Error(string Code, string Description, ErrorType Type = ErrorType.Failure, string? BizCode = null)
{
    // One bizCode belongs to one HTTP status (api-conventions.md): a bizCode is accepted only on the error types that answer its status.
    private static readonly Dictionary<ErrorType, string[]> AllowedBizCodes = new()
    {
        [ErrorType.Validation] = ["E10001"],
        [ErrorType.Unauthorized] = ["E10002"],
        [ErrorType.Forbidden] = ["E10009"],
        [ErrorType.NotFound] = ["E10004", "E10005"],
        [ErrorType.Conflict] = ["E10003", "E10006", "E10007", "E10008"],
        [ErrorType.Timeout] = ["E90001"],
        [ErrorType.TooManyRequests] = ["E90002"]
    };

    public static Error Of(string code, string description, ErrorType type, string bizCode) =>
        AllowedBizCodes.TryGetValue(type, out var allowed) && allowed.Contains(bizCode)
            ? new(code, description, type, bizCode)
            : throw new ArgumentException($"bizCode {bizCode} is not valid for {type}.", nameof(bizCode));

    public static Error Failure(string code, string description) => new(code, description);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    /// <summary>The same error for one batch item: its message starts with the position, <c>roles[2]: Role not found.</c> (api-conventions Batch).</summary>
    public Error At(string position) => this with { Description = Ecommerce.Shared.Validation.ValidationMessages.At(position, Description) };
}
