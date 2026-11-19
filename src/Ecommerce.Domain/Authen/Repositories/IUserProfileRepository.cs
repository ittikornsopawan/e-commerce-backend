using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The profile writes of spec 07-upsert-user-profile.</summary>
public interface IUserProfileRepository
{
    /// <summary>UPDATE - Update user: only the supplied fields. False when no ACTIVE row matched.</summary>
    Task<bool> UpdateUserAsync(Guid userId, ProfileUserChanges changes, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Check existing information: the non-DELETED row, or null.</summary>
    Task<Guid?> FindInformationIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>INSERT - Create information: omitted fields are NULL.</summary>
    Task<Guid> InsertInformationAsync(Guid userId, ProfileInformationChanges changes, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update information: only the supplied fields. False when no non-DELETED row matched.</summary>
    Task<bool> UpdateInformationAsync(Guid informationId, Guid userId, ProfileInformationChanges changes, DateTime nowUtc, CancellationToken cancellationToken);
}
