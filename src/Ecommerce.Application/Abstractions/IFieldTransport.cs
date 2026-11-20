namespace Ecommerce.Application.Abstractions;

/// <summary>
/// Field transport of api-conventions "Encrypted sensitive fields": the frontend wraps a per-request AES-256 key with
/// the backend public key and encrypts each sensitive field with it.
/// </summary>
public interface IFieldTransport
{
    /// <summary>Unwraps the request key. Null when the key id is unknown or the key does not unwrap.</summary>
    IFieldTransportSession? Open(string keyId, string encryptedKey);
}

/// <summary>Holds the request key in memory; dispose it when the request ends so the key is zeroed.</summary>
public interface IFieldTransportSession : IDisposable
{
    /// <summary>Plain text of one field, or null when the value cannot be decrypted for that JSON path.</summary>
    string? Decrypt(string path, string wireValue);
}
