namespace Ecommerce.Domain;

/// <summary>Raised by repositories when a unique constraint is violated, for example two requests registering the same username at once.</summary>
public sealed class DuplicateKeyException(string constraintName, Exception? inner = null)
    : Exception($"Unique constraint '{constraintName}' was violated.", inner)
{
    public string ConstraintName { get; } = constraintName;
}
