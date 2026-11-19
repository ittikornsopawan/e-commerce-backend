using Ecommerce.Shared;

namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>The m_user fields of a profile upsert; text is plain here, storage encrypts the names.</summary>
public sealed record ProfileUserChanges(
    Optional<string?> FirstName, Optional<string?> LastName, Optional<string?> DisplayName, Optional<string?> ProfilePicturePath)
{
    public bool Any => FirstName.IsSet || LastName.IsSet || DisplayName.IsSet || ProfilePicturePath.IsSet;
}

/// <summary>The m_user_information fields of a profile upsert; storage encrypts phone and address.</summary>
public sealed record ProfileInformationChanges(
    Optional<string?> PhoneNumber, Optional<string?> Address, Optional<DateOnly?> DateOfBirth, Optional<string?> Gender)
{
    public bool Any => PhoneNumber.IsSet || Address.IsSet || DateOfBirth.IsSet || Gender.IsSet;
}
