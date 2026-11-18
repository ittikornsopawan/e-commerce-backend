namespace Ecommerce.Domain;

/// <summary>Raised by repositories when a unique constraint is violated, for example two requests registering the same username at once.</summary>
/// <param name="Table">The violated table as <c>schema.table</c>, so a use case answers only the duplicate its spec names.</param>
public sealed class DuplicateKeyException(string constraintName, string table, Exception? inner = null)
    : Exception($"Unique constraint '{constraintName}' on '{table}' was violated.", inner)
{
    public string ConstraintName { get; } = constraintName;

    public string Table { get; } = table;
}
