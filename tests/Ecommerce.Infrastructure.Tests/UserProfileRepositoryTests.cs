using System.Data;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Shared;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Infrastructure.Tests;

// The database behavior of 07-upsert-user-profile test-cases.md, proven on the real schema.
[Collection(PostgresCollection.Name)]
public sealed class UserProfileRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    private async Task<Guid> UserAsync()
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return await new RegistrationRepository(db, _protector).AddAsync(Registration(full: true), CancellationToken.None);
    }

    private async Task<T> InTransaction<T>(Func<UserProfileRepository, Task<T>> work)
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return (await new EfTransactionRunner(db).RunAsync(IsolationLevel.ReadCommitted,
            async _ => Result.Success(await work(new UserProfileRepository(db, _protector))), CancellationToken.None)).Value;
    }

    private static Optional<T> Set<T>(T value) => Optional<T>.Of(value);

    [Fact]
    public async Task TC_PROFILE_003_only_supplied_user_fields_change_and_null_clears()
    {
        var userId = await UserAsync();
        var now = DateTime.UtcNow;

        Assert.True(await InTransaction(r => r.UpdateUserAsync(userId, new ProfileUserChanges(Set<string?>(null), default, Set<string?>("New Name"), default), now, CancellationToken.None)));

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Null(user.FirstName);
        Assert.Equal("Doe", _protector.Unprotect(user.LastName!));
        Assert.Equal("New Name", user.DisplayName);
        Assert.Equal("/profiles/a.png", user.ProfilePicturePath);
        Assert.Equal(userId.ToString(), user.UpdatedBy);
    }

    [Fact]
    public async Task TC_PROFILE_002_information_is_found_inserted_and_updated_by_presence()
    {
        var userId = await UserAsync();
        var existing = await InTransaction(r => r.FindInformationIdAsync(userId, CancellationToken.None));
        Assert.NotNull(existing);

        await ExecuteAsync(postgres.ConnectionString, "UPDATE authen.m_user_information SET row_status = 'DELETED' WHERE user_id = @u", ("u", userId));
        Assert.Null(await InTransaction(r => r.FindInformationIdAsync(userId, CancellationToken.None)));

        var id = await InTransaction(r => r.InsertInformationAsync(userId, new ProfileInformationChanges(default, default, default, Set<string?>("OTHER")), CancellationToken.None));
        Assert.True(await InTransaction(r => r.UpdateInformationAsync(id, userId, new ProfileInformationChanges(Set<string?>("0812345678"), default, default, default), DateTime.UtcNow, CancellationToken.None)));

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var row = await db.UserInformation.SingleAsync(i => i.Id == id);
        Assert.Equal(("OTHER", "0812345678", (byte[]?)null), (row.Gender, _protector.Unprotect(row.PhoneNumber!), row.Address));
        Assert.Equal(2, await db.UserInformation.CountAsync(i => i.UserId == userId));
    }
}
