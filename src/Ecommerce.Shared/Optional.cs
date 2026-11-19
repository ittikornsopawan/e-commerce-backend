namespace Ecommerce.Shared;

/// <summary>
/// A field of a partial update: <c>IsSet</c> is false when the key was not sent (keep the stored value); when set, a
/// null <c>Value</c> clears it. Shared because the API reads it from JSON key presence and the inner layers act on it.
/// </summary>
public readonly record struct Optional<T>(bool IsSet, T Value)
{
    public static Optional<T> Unset => default;

    public static Optional<T> Of(T value) => new(true, value);
}
