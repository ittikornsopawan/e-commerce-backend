using System.Data;
using System.Security.Cryptography;
using System.Text;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Infrastructure.Tests;

// The database behavior of 02-login test-cases.md, proven on the real schema.
[Collection(PostgresCollection.Name)]
public sealed class LoginRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    private (EcommerceDbContext Db, LoginRepository Repository, EfTransactionRunner Runner) Create(int commandTimeoutSeconds = 30)
    {
        var db = EcommerceDbContext.Create(postgres.ConnectionString, commandTimeoutSeconds);
        return (db, new LoginRepository(db, _protector), new EfTransactionRunner(db));
    }

    private async Task<(Guid Id, NewRegistration Registration)> UserAsync()
    {
        var registration = Registration();
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var id = await new RegistrationRepository(db, _protector).AddAsync(registration, CancellationToken.None);
        return (id, registration);
    }

    private static byte[] Hash(string identifier) => SHA256.HashData(Encoding.UTF8.GetBytes(identifier));

    private async Task<T> InTransaction<T>(Func<LoginRepository, Task<T>> work, int commandTimeoutSeconds = 30)
    {
        var (db, repository, runner) = Create(commandTimeoutSeconds);
        await using var _ = db;
        return (await runner.RunAsync(IsolationLevel.ReadCommitted, async _ => Result.Success(await work(repository)), CancellationToken.None)).Value;
    }

    [Fact]
    public async Task TC_LOGIN_016_the_identifier_lock_is_held_until_the_transaction_ends()
    {
        var identifier = "lock." + Guid.NewGuid();
        var (db, repository, _) = Create();
        await using var _db = db;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await repository.LockIdentifierAsync(identifier, CancellationToken.None);

        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", other);
        command.Parameters.AddWithValue("key", LoginRepository.LockKey(Hash(identifier)));

        Assert.False((bool)(await command.ExecuteScalarAsync())!);
        await transaction.RollbackAsync();
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public void TC_LOGIN_016_the_lock_key_is_the_first_eight_hash_bytes_big_endian() =>
        Assert.Equal(unchecked((long)0x8000000000000001UL), LoginRepository.LockKey([0x80, 0, 0, 0, 0, 0, 0, 1, 0xFF]));

    [Fact]
    public async Task TC_LOGIN_013_TC_LOGIN_014_only_recent_FAILED_attempts_of_that_identifier_count()
    {
        var identifier = "count." + Guid.NewGuid();
        var now = DateTime.UtcNow;
        await InTransaction<int>(async r =>
        {
            foreach (var (result, minutesAgo) in new[] { (LoginResult.Failed, 1), (LoginResult.Failed, 14), (LoginResult.Failed, 16), (LoginResult.Refused, 1), (LoginResult.Success, 1) })
                await r.RecordAttemptAsync(identifier, null, result, now.AddMinutes(-minutesAgo), CancellationToken.None);
            await r.RecordAttemptAsync("other." + identifier, null, LoginResult.Failed, now, CancellationToken.None);
            return 0;
        });

        Assert.Equal(2, await InTransaction(r => r.CountRecentFailuresAsync(identifier, now.AddMinutes(-15), CancellationToken.None)));
    }

    [Fact]
    public async Task TC_LOGIN_008_an_attempt_row_has_the_hash_result_user_and_system_creator()
    {
        var (userId, registration) = await UserAsync();
        var now = DateTime.UtcNow;
        await InTransaction(async r => { await r.RecordAttemptAsync(registration.Username, userId, LoginResult.Success, now, CancellationToken.None); return 0; });

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var row = await db.LoginAttempts.SingleAsync(a => a.UserId == userId);
        Assert.Equal(Hash(registration.Username), row.IdentifierHash);
        Assert.Equal("SUCCESS", row.Result);
        Assert.Equal("system", row.CreatedBy);
        Assert.Equal(now, row.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task TC_LOGIN_010_TC_LOGIN_011_TC_LOGIN_007_only_exact_matches_of_active_users_are_returned()
    {
        var (userId, registration) = await UserAsync();
        var (inactiveId, inactive) = await UserAsync();
        await ExecuteAsync(postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", inactiveId));

        var byUsername = await InTransaction(r => r.LockActiveUsersAsync(registration.Username, CancellationToken.None));
        var byEmail = await InTransaction(r => r.LockActiveUsersAsync(registration.EmailAddress, CancellationToken.None));
        var upper = await InTransaction(r => r.LockActiveUsersAsync(registration.Username.ToUpperInvariant(), CancellationToken.None));
        var locked = await InTransaction(r => r.LockActiveUsersAsync(inactive.Username, CancellationToken.None));

        var user = Assert.Single(byUsername);
        Assert.Equal(new LoginUser(userId, registration.Username, registration.EmailAddress, null), user);
        Assert.Equal(userId, Assert.Single(byEmail).Id);
        Assert.Empty(upper);
        Assert.Empty(locked);
    }

    [Fact]
    public async Task TC_LOGIN_019_the_user_rows_are_locked_FOR_UPDATE()
    {
        var (userId, registration) = await UserAsync();
        var (db, repository, _) = Create();
        await using var _db = db;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await repository.LockActiveUsersAsync(registration.Username, CancellationToken.None);

        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync();
        await using var command = new NpgsqlCommand("SET lock_timeout = '300ms'; SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", other);
        command.Parameters.AddWithValue("id", userId);
        Assert.Equal("55P03", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync())).SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TC_LOGIN_009_the_current_password_is_the_active_PASSWORD_row()
    {
        var (userId, registration) = await UserAsync();
        Assert.Equal(registration.Password, await InTransaction(r => r.GetCurrentPasswordAsync(userId, CancellationToken.None)));

        await ExecuteAsync(postgres.ConnectionString, "UPDATE authen.m_user_password SET row_status = 'INACTIVE' WHERE user_id = @id", ("id", userId));
        Assert.Null(await InTransaction(r => r.GetCurrentPasswordAsync(userId, CancellationToken.None)));
    }

    [Fact]
    public async Task TC_LOGIN_017_authorization_facts_follow_the_status_filters_of_the_spec_queries()
    {
        var (userId, _) = await UserAsync();
        var s = postgres.ConnectionString;
        var activeRole = await AuthorAsync(s, "author.m_role", "name", "A_" + Guid.NewGuid().ToString("N")[..8]);
        var inactiveRole = await AuthorAsync(s, "author.m_role", "name", "Y_" + Guid.NewGuid().ToString("N")[..8], "INACTIVE");
        var unassignedRole = await AuthorAsync(s, "author.m_role", "name", "X_" + Guid.NewGuid().ToString("N")[..8]);
        await LinkAsync(s, "author.t_user_role", ("user_id", userId), ("role_id", activeRole));
        await LinkAsync(s, "author.t_user_role", ("user_id", userId), ("role_id", inactiveRole));
        await LinkAsync(s, "author.t_user_role", ("user_id", userId), ("role_id", unassignedRole), "INACTIVE");
        var permission = await AuthorAsync(s, "author.m_permission", "name", "p." + Guid.NewGuid().ToString("N")[..8]);
        var inactivePermission = await AuthorAsync(s, "author.m_permission", "name", "q." + Guid.NewGuid().ToString("N")[..8], "INACTIVE");
        var scope = await ScopeAsync(s, "OWN");
        var definition = await LinkAsync(s, "author.t_permission_scope", ("permission_id", permission), ("scope_id", scope));
        var rolePermission = await LinkAsync(s, "author.t_role_permission", ("role_id", activeRole), ("permission_id", permission));
        await LinkAsync(s, "author.t_role_permission", ("role_id", inactiveRole), ("permission_id", inactivePermission));
        await LinkAsync(s, "author.t_role_permission_scope", ("role_permission_id", rolePermission), ("permission_scope_id", definition));
        await LinkAsync(s, "author.t_user_permission", ("user_id", userId), ("permission_id", inactivePermission));

        var facts = await InTransaction(r => r.GetAuthorizationFactsAsync(userId, CancellationToken.None));
        var scopes = await InTransaction(r => r.GetActiveScopesAsync(Ecommerce.Domain.Authen.Domain.AuthorizationClaims.AssignedScopeIds(facts), CancellationToken.None));

        Assert.Equal([activeRole], facts.Roles.Select(r => r.RoleId));
        Assert.Equal([new RolePermissionFact(rolePermission, permission)], facts.RolePermissions);
        Assert.Equal([new RoleScopeLinkFact(rolePermission, definition)], facts.RoleScopeLinks);
        Assert.Equal([new DirectPermissionFact(inactivePermission, null)], facts.DirectPermissions);
        Assert.Equal([permission], facts.ActivePermissions.Select(p => p.PermissionId));
        Assert.Contains(facts.Definitions, d => d.Id == definition && d.RowStatus == "ACTIVE");
        Assert.Equal([new ScopeFact(scope, "OWN")], scopes);
    }

    [Fact]
    public async Task TC_LOGIN_001_TC_LOGIN_018_tokens_of_one_session_are_stored_as_given()
    {
        var (userId, _) = await UserAsync();
        var session = new LoginSession(userId, Guid.NewGuid(), [1, 2, 3], DateTime.UtcNow.AddMinutes(15), [4, 5, 6], DateTime.UtcNow.AddDays(14));

        await InTransaction(async r => { await r.AddSessionTokensAsync(session, CancellationToken.None); return 0; });

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var access = await db.AccessTokens.SingleAsync(t => t.UserId == userId);
        var refresh = await db.RefreshTokens.SingleAsync(t => t.UserId == userId);
        Assert.Equal(session.SessionId, access.SessionId);
        Assert.Equal(session.SessionId, refresh.SessionId);
        Assert.Equal(session.AccessTokenHash, access.Token);
        Assert.Equal(session.RefreshTokenHash, refresh.Token);
        Assert.Equal(userId.ToString(), access.CreatedBy);
        Assert.Equal(session.AccessExpiresAtUtc, access.ExpiredAt, TimeSpan.FromMilliseconds(1));
    }

    internal static async Task<Guid> AuthorAsync(string connectionString, string table, string column, string value, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString,
            $"INSERT INTO {table} (id, created_by, row_status, {column}, description) VALUES (@id, 'test', @status, @value, 'test')",
            ("id", id), ("status", rowStatus), ("value", value));
        return id;
    }

    internal static async Task<Guid> ScopeAsync(string connectionString, string code, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString,
            "INSERT INTO author.m_scope (id, created_by, row_status, scope, name, description) VALUES (@id, 'test', @status, @code, @code, 'test')",
            ("id", id), ("status", rowStatus), ("code", code));
        return id;
    }

    internal static async Task<Guid> LinkAsync(string connectionString, string table, (string Column, Guid Value) a, (string Column, Guid Value) b, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString,
            $"INSERT INTO {table} (id, created_by, row_status, {a.Column}, {b.Column}) VALUES (@id, 'test', @status, @a, @b)",
            ("id", id), ("status", rowStatus), ("a", a.Value), ("b", b.Value));
        return id;
    }
}
