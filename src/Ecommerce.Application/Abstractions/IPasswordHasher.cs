using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Application.Abstractions;

public interface IPasswordHasher
{
    PasswordCredential Hash(string password);

    bool Verify(string password, PasswordCredential credential);
}
