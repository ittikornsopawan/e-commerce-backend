namespace Ecommerce.Domain.Authen;

/// <summary>Mirrors the allowed values of <c>authen.m_user.row_status</c>.</summary>
public enum UserStatus
{
    Active,
    Inactive,
    Deleted,
    Locked,
    Archived
}
