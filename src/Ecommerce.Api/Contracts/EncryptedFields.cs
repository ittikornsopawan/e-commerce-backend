using Ecommerce.Application.Abstractions;

namespace Ecommerce.Api.Contracts;

/// <summary>A request whose sensitive fields arrive encrypted; returns the same request with the plain values.</summary>
public interface IEncryptedRequest
{
    IEncryptedRequest Decrypt(EncryptedFields fields);
}

public sealed class FieldDecryptionException : Exception;

/// <summary>Decrypts one field by its JSON path. An absent or null field stays as it is; a sent field must be ciphertext.</summary>
public sealed class EncryptedFields(IFieldTransportSession session)
{
    public string? Field(string path, string? wireValue) =>
        wireValue is null ? null : session.Decrypt(path, wireValue) ?? throw new FieldDecryptionException();
}
