using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The reads and writes of spec 01-register, run inside one REPEATABLE READ transaction.</summary>
public interface IRegistrationRepository
{
    /// <summary>QUERY - Check existing user: the username or the email exists in any lifecycle state.</summary>
    Task<bool> IdentifierExistsAsync(string username, string emailAddress, CancellationToken cancellationToken);

    /// <summary>QUERY - Get effective consents, then Get active consent types: the consents that apply at <paramref name="nowUtc"/>.</summary>
    Task<IReadOnlyList<ApplicableConsent>> GetApplicableConsentsAsync(DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Check referrer: the user exists in any lifecycle status.</summary>
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Inserts the user and every child row; returns the new user id.</summary>
    /// <exception cref="DuplicateKeyException">A concurrent registration took the username or email.</exception>
    /// <exception cref="MissingReferenceException">A referenced consent or referrer was deleted concurrently.</exception>
    Task<Guid> AddAsync(NewRegistration registration, CancellationToken cancellationToken);
}
