namespace Ecommerce.Infrastructure.Persistence.Ef;

/// <summary>Row of authen.m_user. Sensitive columns hold the encrypted bytes (FieldProtector).</summary>
public sealed class UserRecord
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string RowStatus { get; set; } = "ACTIVE";
    public byte[] Username { get; set; } = [];
    public byte[] EmailAddress { get; set; } = [];
}
