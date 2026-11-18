namespace Ecommerce.Infrastructure.Persistence.Ef;

// Rows of the Showcase schema. Columns map to the snake_case of the property name (EcommerceDbContext).
// Byte columns hold FieldProtector output; only the columns the backend reads or writes are mapped.

/// <summary>authen.m_user</summary>
public sealed class UserRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string RowStatus { get; set; } = "ACTIVE";
    public byte[] Username { get; set; } = [];
    public byte[] EmailAddress { get; set; } = [];
    public byte[]? FirstName { get; set; }
    public byte[]? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? ProfilePicturePath { get; set; }
}

/// <summary>authen.m_user_password</summary>
public sealed class UserPasswordRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public string RowType { get; set; } = "PASSWORD";
    public Guid UserId { get; set; }
    public byte[] Password { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
}

/// <summary>authen.m_user_information</summary>
public sealed class UserInformationRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public Guid UserId { get; set; }
    public byte[]? PhoneNumber { get; set; }
    public byte[]? Address { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
}

/// <summary>authen.t_user_address</summary>
public sealed class UserAddressRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public bool IsDefault { get; set; }
    public byte[] Address { get; set; } = [];
    public byte[] City { get; set; } = [];
    public byte[] State { get; set; } = [];
    public byte[] PostalCode { get; set; } = [];
    public byte[] Country { get; set; } = [];
}

/// <summary>authen.t_user_consent</summary>
public sealed class UserConsentRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid ConsentId { get; set; }
    public string ConsentVersion { get; set; } = "";
    public bool Result { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>authen.l_user_referral</summary>
public sealed class UserReferralRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid? ReferrerId { get; set; }
}

/// <summary>consent.t_consent</summary>
public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public string RowStatus { get; set; } = "";
    public Guid ConsentTypeId { get; set; }
    public string ConsentVersion { get; set; } = "";
    public DateTime EffectiveAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public bool IsRequired { get; set; }
}

/// <summary>consent.m_consent_type</summary>
public sealed class ConsentTypeRecord
{
    public Guid Id { get; set; }
    public string RowStatus { get; set; } = "";
}
