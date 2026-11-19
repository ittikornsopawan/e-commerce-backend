using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Infrastructure.Tests;

// The shared current-credential check of specs 07 to 10 (TC-PROFILE-010, TC-PROFILE-011).
[Collection(PostgresCollection.Name)]
public sealed class CallerCredentialRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    [Fact]
    public async Task TC_PROFILE_011_the_user_row_is_locked_and_TC_PROFILE_010_the_token_must_be_current()
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
        var otherId = await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
        var hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        await ExecuteAsync(postgres.ConnectionString,
            "INSERT INTO authen.t_user_access_token (created_by, user_id, session_id, token, expired_at) VALUES ('test', @u, @s, @t, @x)",
            ("u", userId), ("s", Guid.NewGuid()), ("t", hash), ("x", DateTime.UtcNow.AddMinutes(15)));
        var repository = new CallerCredentialRepository(db);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.True(await repository.LockActiveUserAsync(userId, CancellationToken.None));
            await using var other = new NpgsqlConnection(postgres.ConnectionString);
            await other.OpenAsync();
            await using var command = new NpgsqlCommand("SET lock_timeout = '300ms'; SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", other);
            command.Parameters.AddWithValue("id", userId);
            Assert.Equal("55P03", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync())).SqlState);
            await transaction.RollbackAsync();
        }

        Assert.True(await repository.IsCurrentAccessTokenAsync(hash, userId, DateTime.UtcNow, CancellationToken.None));
        Assert.False(await repository.IsCurrentAccessTokenAsync(hash, otherId, DateTime.UtcNow, CancellationToken.None));
        Assert.False(await repository.IsCurrentAccessTokenAsync(hash, userId, DateTime.UtcNow.AddMinutes(16), CancellationToken.None));
    }
}
