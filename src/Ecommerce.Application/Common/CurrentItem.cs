namespace Ecommerce.Application.Common;

/// <summary>
/// The batch item being written (author 02, 05, 08). A unique-index failure is thrown out of the transaction runner, after
/// the loop has left; this lets the handler still name that item's position in its 409.
/// </summary>
public sealed class CurrentItem
{
    public int Index { get; set; }
}
