using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.Entities;

/// <summary>A single-use reset link. Only the hash of the token is kept; the token itself goes to the user by email.</summary>
public sealed class PasswordResetToken : Entity<Guid>
{
    private PasswordResetToken(Guid id, UserId userId, byte[] tokenHash, DateTime expiresAtUtc, DateTime? consumedAtUtc)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        ConsumedAtUtc = consumedAtUtc;
    }

    public UserId UserId { get; }

    public byte[] TokenHash { get; }

    public DateTime ExpiresAtUtc { get; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public static PasswordResetToken Issue(UserId userId, byte[] tokenHash, DateTime expiresAtUtc) =>
        new(Guid.CreateVersion7(), userId, tokenHash, expiresAtUtc, consumedAtUtc: null);

    public static PasswordResetToken Restore(Guid id, UserId userId, byte[] tokenHash, DateTime expiresAtUtc, DateTime? consumedAtUtc) =>
        new(id, userId, tokenHash, expiresAtUtc, consumedAtUtc);

    public bool IsUsableAt(DateTime nowUtc) => ConsumedAtUtc is null && nowUtc < ExpiresAtUtc;

    /// <summary>Marks the token as used. A token can be used once, and only before it expires.</summary>
    public Result Consume(DateTime nowUtc)
    {
        if (!IsUsableAt(nowUtc))
        {
            return Result.Failure(AuthenErrors.InvalidResetToken);
        }

        ConsumedAtUtc = nowUtc;
        return Result.Success();
    }
}
