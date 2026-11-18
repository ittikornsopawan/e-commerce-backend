namespace Ecommerce.Shared.Results;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden
}

/// <param name="BizCode">The API business code when the default of <paramref name="Type"/> does not fit, such as E10005 for a missing reference.</param>
public sealed record Error(string Code, string Description, ErrorType Type = ErrorType.Failure, string? BizCode = null)
{
    public static Error Of(string code, string description, ErrorType type, string bizCode) => new(code, description, type, bizCode);

    public static Error Failure(string code, string description) => new(code, description);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}
