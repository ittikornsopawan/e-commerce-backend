namespace Ecommerce.Infrastructure.Persistence.Ef;

// Rows of the Showcase schema. Columns map to the snake_case of the property name (EcommerceDbContext).
// Byte columns hold FieldProtector output; only the columns the backend reads or writes are mapped.

/// <summary>authen.m_user</summary>
public sealed class UserRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
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
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "ACTIVE";
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
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "ACTIVE";
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
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "ACTIVE";
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

/// <summary>consent.v_applicable_consent (read only: ACTIVE consents of ACTIVE consent types)</summary>
public sealed class ApplicableConsentRecord
{
    public Guid Id { get; set; }
    public Guid ConsentTypeId { get; set; }
    public string ConsentVersion { get; set; } = "";
    public bool IsRequired { get; set; }
    public DateTime EffectiveAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
}

/// <summary>authen.l_user_login_attempt</summary>
public sealed class LoginAttemptRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public byte[] IdentifierHash { get; set; } = [];
    public Guid? UserId { get; set; }
    public string Result { get; set; } = "";
}

/// <summary>authen.t_user_access_token and authen.t_user_refresh_token (same columns).</summary>
public abstract class TokenRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "ACTIVE";
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public byte[] Token { get; set; } = [];
    public DateTime ExpiredAt { get; set; }
}

public sealed class AccessTokenRecord : TokenRecord;

public sealed class RefreshTokenRecord : TokenRecord;

/// <summary>author.m_role</summary>
public sealed class RoleRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>author.m_permission</summary>
public sealed class PermissionRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>author.m_scope</summary>
public sealed class ScopeRecord
{
    public Guid Id { get; set; }
    public string RowStatus { get; set; } = "";
    public string Scope { get; set; } = "";
}

/// <summary>author.t_permission_scope</summary>
public sealed class PermissionScopeRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "";
    public Guid PermissionId { get; set; }
    public Guid ScopeId { get; set; }
}

/// <summary>author.t_role_permission</summary>
public sealed class RolePermissionRecord
{
    public Guid Id { get; set; }
    public string RowStatus { get; set; } = "";
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

/// <summary>author.t_role_permission_scope</summary>
public sealed class RolePermissionScopeRecord
{
    public Guid Id { get; set; }
    public string RowStatus { get; set; } = "";
    public Guid RolePermissionId { get; set; }
    public Guid PermissionScopeId { get; set; }
}

/// <summary>author.t_user_role</summary>
public sealed class UserRoleRecord
{
    public Guid Id { get; set; }
    public string RowStatus { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

/// <summary>author.t_user_permission</summary>
public sealed class UserPermissionRecord
{
    public Guid Id { get; set; }
    public string RowStatus { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public Guid? PermissionScopeId { get; set; }
}

/// <summary>authen.t_user_reset_password</summary>
public sealed class PasswordResetRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowStatus { get; set; } = "ACTIVE";
    public Guid UserId { get; set; }
    public byte[] ResetToken { get; set; } = [];
    public DateTime ExpiredAt { get; set; }
}
