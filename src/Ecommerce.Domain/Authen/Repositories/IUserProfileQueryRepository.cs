namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The m_user part of the profile, decrypted.</summary>
public sealed record ProfileUser(string EmailAddress, string? FirstName, string? LastName, string? DisplayName, string? ProfilePicturePath);

/// <summary>The m_user_information part of the profile, decrypted; an empty gender is null.</summary>
public sealed record ProfileInformation(string? PhoneNumber, string? Address, DateOnly? DateOfBirth, string? Gender);

/// <summary>The reads of spec 08-inquiry-user-profile, in one read-only snapshot.</summary>
public interface IUserProfileQueryRepository
{
    /// <summary>QUERY - Get user: joined with the caller's ACTIVE, unexpired stored access token; null when either fails.</summary>
    Task<ProfileUser?> GetUserAsync(Guid userId, byte[] accessTokenHash, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Get information: the non-DELETED row, or null.</summary>
    Task<ProfileInformation?> GetInformationAsync(Guid userId, CancellationToken cancellationToken);
}
