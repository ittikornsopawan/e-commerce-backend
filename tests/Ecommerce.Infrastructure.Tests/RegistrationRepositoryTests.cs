using System.Data;
using Ecommerce.Domain;
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

// Durable effects of 01-register test-cases.md, proven on the real schema.
[Collection(PostgresCollection.Name)]
public sealed class RegistrationRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    private (EcommerceDbContext Db, RegistrationRepository Repository, EfTransactionRunner Runner) Create(int commandTimeoutSeconds = 30)
    {
        var db = EcommerceDbContext.Create(postgres.ConnectionString, commandTimeoutSeconds);
        return (db, new RegistrationRepository(db, _protector), new EfTransactionRunner(db));
    }

    private async Task<Guid> RegisterAsync(NewRegistration registration)
    {
        var (db, repository, runner) = Create();
        await using var _ = db;
        var result = await runner.RunAsync(IsolationLevel.RepeatableRead,
            async ct => Result.Success(await repository.AddAsync(registration, ct)), CancellationToken.None);
        return result.Value;
    }

    [Fact]
    public async Task TC_REG_001_every_row_is_written_and_personal_fields_are_encrypted()
    {
        var referrer = await RegisterAsync(Registration());
        var type = await ConsentTypeAsync(postgres.ConnectionString);
        var required = await ConsentAsync(postgres.ConnectionString, type, required: true);
        var registration = Registration(full: true, referrerId: referrer,
            address: new RegistrationAddress("Home", "1 Road", "Watthana", "Bangkok", "10110", "Thailand"),
            consents: [new SubmittedConsent(required, "1.0.0", true)]);

        var userId = await RegisterAsync(registration);

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal("auth-register", user.CreatedBy);
        Assert.Equal(registration.Username, _protector.Unprotect(user.Username));
        Assert.Equal(registration.EmailAddress, _protector.Unprotect(user.EmailAddress));
        Assert.Equal("John", _protector.Unprotect(user.FirstName!));
        Assert.Equal("John Doe", user.DisplayName);
        var password = await db.UserPasswords.SingleAsync(p => p.UserId == userId);
        Assert.Equal("PASSWORD", password.RowType);
        Assert.Equal(registration.Password.Hash, password.Password);
        var information = await db.UserInformation.SingleAsync(i => i.UserId == userId);
        Assert.Equal("0812345678", _protector.Unprotect(information.PhoneNumber!));
        Assert.Equal(new DateOnly(1990, 1, 31), information.DateOfBirth);
        Assert.Equal("MALE", information.Gender);
        var address = await db.UserAddresses.SingleAsync(a => a.UserId == userId);
        Assert.Equal("Home", address.Title);
        Assert.Equal("10110", _protector.Unprotect(address.PostalCode));
        Assert.Equal(referrer, (await db.UserReferrals.SingleAsync(r => r.UserId == userId)).ReferrerId);
        Assert.Equal(1, await CountAsync(postgres.ConnectionString, "authen.t_user_consent", userId));
        Assert.Equal(0, await CountAsync(postgres.ConnectionString, "author.t_user_role", userId));
    }

    [Fact]
    public async Task TC_REG_020_minimal_registration_writes_sql_nulls_and_no_optional_rows()
    {
        var userId = await RegisterAsync(Registration());

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Null(user.FirstName);
        Assert.Null(user.DisplayName);
        var information = await db.UserInformation.SingleAsync(i => i.UserId == userId);
        Assert.Null(information.PhoneNumber);
        Assert.Null(information.Gender);
        Assert.Equal(0, await CountAsync(postgres.ConnectionString, "authen.t_user_address", userId));
        Assert.Equal(0, await CountAsync(postgres.ConnectionString, "authen.l_user_referral", userId));
    }

    [Fact]
    public async Task TC_REG_021_the_first_address_is_always_default()
    {
        var userId = await RegisterAsync(Registration(address: new RegistrationAddress("Home", "a", "b", "c", "d", "e")));

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.True((await db.UserAddresses.SingleAsync(a => a.UserId == userId)).IsDefault);
    }

    [Fact]
    public async Task TC_REG_019_each_consent_is_read_evidence_with_its_answer_or_false()
    {
        var type = await ConsentTypeAsync(postgres.ConnectionString);
        var first = await ConsentAsync(postgres.ConnectionString, type, required: true);
        var second = await ConsentAsync(postgres.ConnectionString, type, required: false);
        var registration = Registration(consents: [new SubmittedConsent(second, "1.0.0", null), new SubmittedConsent(first, "1.0.0", true)]);

        var userId = await RegisterAsync(registration);

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var rows = await db.UserConsents.Where(c => c.UserId == userId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows.Single(r => r.ConsentId == first).Result);
        Assert.False(rows.Single(r => r.ConsentId == second).Result);
        Assert.All(rows, r =>
        {
            Assert.True(r.IsRead);
            Assert.Equal("1.0.0", r.ConsentVersion);
            Assert.Equal("auth-register", r.CreatedBy);
            Assert.Equal(registration.NowUtc, r.ReadAt!.Value, TimeSpan.FromMilliseconds(1));
        });
    }

    [Fact]
    public async Task TC_REG_011_an_identifier_exists_in_every_lifecycle_state_and_compares_as_sent()
    {
        var registration = Registration();
        var userId = await RegisterAsync(registration);
        await ExecuteAsync(postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'DELETED' WHERE id = @id", ("id", userId));

        var (db, repository, _) = Create();
        await using var _db = db;
        Assert.True(await repository.IdentifierExistsAsync(registration.Username, "other@example.test", CancellationToken.None));
        Assert.True(await repository.IdentifierExistsAsync("other", registration.EmailAddress, CancellationToken.None));
        // TC-REG-027: the comparison is on the stored bytes of the value as sent.
        Assert.False(await repository.IdentifierExistsAsync(registration.Username.ToUpperInvariant(), "x@example.test", CancellationToken.None));
    }

    [Fact]
    public async Task TC_REG_017_only_effective_active_consents_of_active_types_apply()
    {
        var active = await ConsentTypeAsync(postgres.ConnectionString);
        var inactiveType = await ConsentTypeAsync(postgres.ConnectionString, "INACTIVE");
        var applies = await ConsentAsync(postgres.ConnectionString, active, required: true);
        var future = await ConsentAsync(postgres.ConnectionString, active, required: true, effectiveFromNow: TimeSpan.FromDays(1));
        var expired = await ConsentAsync(postgres.ConnectionString, active, required: true, expiredFromNow: TimeSpan.FromMinutes(-1));
        var inactive = await ConsentAsync(postgres.ConnectionString, active, required: true, rowStatus: "INACTIVE");
        var typeOff = await ConsentAsync(postgres.ConnectionString, inactiveType, required: true);

        var (db, repository, _) = Create();
        await using var _db = db;
        var ids = (await repository.GetApplicableConsentsAsync(DateTime.UtcNow, CancellationToken.None)).Select(c => c.ConsentId).ToList();

        Assert.Contains(applies, ids);
        Assert.DoesNotContain(future, ids);
        Assert.DoesNotContain(expired, ids);
        Assert.DoesNotContain(inactive, ids);
        Assert.DoesNotContain(typeOff, ids);
        // PostgreSQL orders uuid like its lower-case text, which is not Guid.CompareTo order.
        Assert.Equal(ids.OrderBy(i => i.ToString(), StringComparer.Ordinal).ToList(), ids);
    }

    [Fact]
    public async Task TC_REG_013_a_referrer_of_any_status_exists_and_an_unknown_one_does_not()
    {
        var referrer = await RegisterAsync(Registration());
        await ExecuteAsync(postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'DELETED' WHERE id = @id", ("id", referrer));

        var (db, repository, _) = Create();
        await using var _db = db;
        Assert.True(await repository.UserExistsAsync(referrer, CancellationToken.None));
        Assert.False(await repository.UserExistsAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task TC_REG_012_a_concurrent_duplicate_is_a_DuplicateKeyException_and_leaves_no_rows()
    {
        var registration = Registration();
        await RegisterAsync(registration);
        var again = registration with { EmailAddress = "another." + registration.EmailAddress };

        await Assert.ThrowsAsync<DuplicateKeyException>(() => RegisterAsync(again));

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Username == _protector.Protect(registration.Username)));
    }

    [Fact]
    public async Task TC_REG_023_a_reference_deleted_before_insert_is_a_MissingReferenceException_with_full_rollback()
    {
        var registration = Registration(consents: [new SubmittedConsent(Guid.NewGuid(), "1.0.0", true)]);

        await Assert.ThrowsAsync<MissingReferenceException>(() => RegisterAsync(registration));

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.False(await db.Users.AnyAsync(u => u.Username == _protector.Protect(registration.Username)));
    }

    [Fact]
    public async Task TC_REG_022_a_failure_result_rolls_back_every_write()
    {
        var registration = Registration();
        var (db, repository, runner) = Create();
        await using var _db = db;

        var result = await runner.RunAsync<Guid>(IsolationLevel.RepeatableRead, async ct =>
        {
            await repository.AddAsync(registration, ct);
            return Result.Failure<Guid>(Error.Failure("Test.Fail", "injected"));
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        await using var check = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.False(await check.Users.AnyAsync(u => u.Username == _protector.Protect(registration.Username)));
    }

    [Fact]
    public async Task TC_REG_025_a_serialization_conflict_reruns_the_whole_work_once_more()
    {
        var registration = Registration();
        var (db, repository, runner) = Create();
        await using var _db = db;
        var attempts = 0;

        var result = await runner.RunAsync(IsolationLevel.RepeatableRead, async ct =>
        {
            attempts++;
            var id = await repository.AddAsync(registration with { NowUtc = DateTime.UtcNow }, ct);
            if (attempts == 1) throw new PostgresException("injected", "ERROR", "ERROR", "40001");
            return Result.Success(id);
        }, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(1, await CountAsync(postgres.ConnectionString, "authen.m_user_password", result.Value));
        await using var check = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.Equal(1, await check.Users.CountAsync(u => u.Username == _protector.Protect(registration.Username)));
    }

    [Fact]
    public async Task TC_REG_024_a_database_timeout_is_a_TimeoutException_with_full_rollback()
    {
        var registration = Registration();
        // Another transaction inserts the same username and keeps it uncommitted, so the insert waits on the unique index.
        await using var blocker = new NpgsqlConnection(postgres.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO authen.m_user (created_by, username, email_address) VALUES ('test', @u, @e)", blocker, transaction))
        {
            insert.Parameters.AddWithValue("u", _protector.Protect(registration.Username));
            insert.Parameters.AddWithValue("e", _protector.Protect("blocker." + registration.EmailAddress));
            await insert.ExecuteNonQueryAsync();
        }

        var (db, repository, runner) = Create(commandTimeoutSeconds: 1);
        await using var _db = db;
        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(IsolationLevel.RepeatableRead,
            async ct => Result.Success(await repository.AddAsync(registration, ct)), CancellationToken.None));

        await transaction.RollbackAsync();
        await using var check = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.False(await check.Users.AnyAsync(u => u.Username == _protector.Protect(registration.Username)));
    }

    [Fact]
    public async Task TC_REG_025_serialization_conflicts_on_every_attempt_end_as_a_timeout()
    {
        var (db, _, runner) = Create();
        await using var _db = db;
        var attempts = 0;

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync<Guid>(IsolationLevel.RepeatableRead, _ =>
        {
            attempts++;
            throw new PostgresException("injected", "ERROR", "ERROR", "40001");
        }, CancellationToken.None));

        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData("authen", "m_user", "authen.m_user")]
    [InlineData("authen", "m_user_information", "authen.m_user_information")]
    public async Task TC_REG_012_a_unique_violation_names_its_table(string schema, string table, string expected)
    {
        var (db, _, runner) = Create();
        await using var _db = db;

        var error = await Assert.ThrowsAsync<DuplicateKeyException>(() => runner.RunAsync<Guid>(IsolationLevel.RepeatableRead, _ =>
            throw new PostgresException("injected", "ERROR", "ERROR", "23505", schemaName: schema, tableName: table, constraintName: "x"),
            CancellationToken.None));

        Assert.Equal(expected, error.Table);
    }
}
