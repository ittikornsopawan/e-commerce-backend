using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.Entities;

public sealed class User : Entity<UserId>, IAggregateRoot
{
    private User(
        UserId id,
        Username username,
        EmailAddress email,
        PersonName name,
        DisplayName displayName,
        string? profilePicturePath,
        UserStatus status) : base(id)
    {
        Username = username;
        Email = email;
        Name = name;
        DisplayName = displayName;
        ProfilePicturePath = profilePicturePath;
        Status = status;
    }

    public Username Username { get; }

    public EmailAddress Email { get; }

    public PersonName Name { get; }

    public DisplayName DisplayName { get; }

    public string? ProfilePicturePath { get; }

    public UserStatus Status { get; }

    public bool IsActive => Status == UserStatus.Active;

    /// <summary>A newly registered user is active straight away.</summary>
    public static User Register(UserId id, Username username, EmailAddress email, PersonName name, DisplayName displayName) =>
        new(id, username, email, name, displayName, profilePicturePath: null, UserStatus.Active);

    /// <summary>Rebuilds a user from storage. No rules are applied, because the data was valid when it was saved.</summary>
    public static User Restore(
        UserId id,
        Username username,
        EmailAddress email,
        PersonName name,
        DisplayName displayName,
        string? profilePicturePath,
        UserStatus status) =>
        new(id, username, email, name, displayName, profilePicturePath, status);

    /// <summary>Only active accounts may sign in.</summary>
    public Result EnsureCanSignIn() =>
        IsActive ? Result.Success() : Result.Failure(AuthenErrors.AccountNotActive);
}
