namespace Ecommerce.Domain;

/// <summary>Raised by repositories when a foreign key target disappeared, for example a referrer deleted during a registration.</summary>
public sealed class MissingReferenceException(string constraintName, Exception? inner = null)
    : Exception($"Foreign key '{constraintName}' was violated.", inner)
{
    public string ConstraintName { get; } = constraintName;
}
