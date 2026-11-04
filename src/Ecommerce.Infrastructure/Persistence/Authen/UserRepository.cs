using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Security;
using Npgsql;

namespace Ecommerce.Infrastructure.Persistence.Authen;

internal sealed class UserRepository(DbSession session, FieldProtector protector) : IUserRepository
{
    private const string UniqueViolation = "23505";

    private const string SelectUser = """
        SELECT id AS Id, username AS Username, email_address AS Email, first_name AS FirstName,
               last_name AS LastName, display_name AS DisplayName,
               profile_picture_path AS ProfilePicturePath, row_status AS RowStatus
        FROM authen.m_user
        """;

    public Task<bool> UsernameExistsAsync(Username username, CancellationToken cancellationToken) =>
        session.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM authen.m_user WHERE username = @value)",
            new { value = protector.Protect(username.Value) }, cancellationToken);

    public Task<bool> EmailExistsAsync(EmailAddress email, CancellationToken cancellationToken) =>
        session.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM authen.m_user WHERE email_address = @value)",
            new { value = protector.Protect(email.Value) }, cancellationToken);

    public async Task<User?> FindByIdAsync(UserId id, CancellationToken cancellationToken) =>
        ToUser(await session.QuerySingleOrDefaultAsync<UserRow>(
            $"{SelectUser} WHERE id = @id", new { id = id.Value }, cancellationToken));

    public async Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken) =>
        ToUser(await session.QuerySingleOrDefaultAsync<UserRow>(
            $"{SelectUser} WHERE email_address = @value", new { value = protector.Protect(email.Value) }, cancellationToken));

    public async Task<User?> FindByLoginAsync(LoginIdentifier login, CancellationToken cancellationToken) =>
        ToUser(await session.QuerySingleOrDefaultAsync<UserRow>(
            $"{SelectUser} WHERE username = @value OR email_address = @value",
            new { value = protector.Protect(login.Value) }, cancellationToken));

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await session.ExecuteAsync(
                """
                INSERT INTO authen.m_user
                    (id, created_by, created_at, row_status, username, email_address, first_name, last_name, display_name)
                VALUES
                    (@Id, @CreatedBy, now() AT TIME ZONE 'UTC', @RowStatus, @Username, @Email, @FirstName, @LastName, @DisplayName)
                """,
                new
                {
                    Id = user.Id.Value,
                    CreatedBy = user.Id.ToString(),
                    RowStatus = user.Status.ToString().ToUpperInvariant(),
                    Username = protector.Protect(user.Username.Value),
                    Email = protector.Protect(user.Email.Value),
                    FirstName = protector.Protect(user.Name.First),
                    LastName = protector.Protect(user.Name.Last),
                    DisplayName = user.DisplayName.Value
                },
                cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            throw new DuplicateKeyException(ex.ConstraintName ?? string.Empty, ex);
        }
    }

    private User? ToUser(UserRow? row) => row is null
        ? null
        : User.Restore(
            new UserId(row.Id),
            Username.Restore(protector.Unprotect(row.Username)),
            EmailAddress.Restore(protector.Unprotect(row.Email)),
            PersonName.Restore(protector.Unprotect(row.FirstName), protector.Unprotect(row.LastName)),
            DisplayName.Restore(row.DisplayName),
            row.ProfilePicturePath,
            Enum.Parse<UserStatus>(row.RowStatus, ignoreCase: true));

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public byte[] Username { get; init; } = [];
        public byte[] Email { get; init; } = [];
        public byte[] FirstName { get; init; } = [];
        public byte[] LastName { get; init; } = [];
        public string DisplayName { get; init; } = string.Empty;
        public string? ProfilePicturePath { get; init; }
        public string RowStatus { get; init; } = string.Empty;
    }
}
