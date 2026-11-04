namespace Ecommerce.Domain.Authen.ValueObjects;

public readonly record struct UserId(Guid Value)
{
    /// <summary>Time-ordered, so new users insert at the end of the primary-key index.</summary>
    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
