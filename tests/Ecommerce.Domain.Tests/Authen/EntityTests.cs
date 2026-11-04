using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Tests.Authen;

public class EntityTests
{
    private static User NewUser(UserId? id = null)
    {
        var name = PersonName.Create("Ittikorn", "S").Value;
        return User.Register(
            id ?? UserId.New(),
            Username.Create("aof").Value,
            EmailAddress.Create("aof@example.com").Value,
            name,
            DisplayName.Create(null, name).Value);
    }

    [Fact]
    public void A_registered_user_is_active_and_may_sign_in()
    {
        var user = NewUser();

        Assert.True(user.IsActive);
        Assert.True(user.EnsureCanSignIn().IsSuccess);
    }

    [Theory]
    [InlineData(UserStatus.Inactive)]
    [InlineData(UserStatus.Locked)]
    [InlineData(UserStatus.Deleted)]
    [InlineData(UserStatus.Archived)]
    public void Only_active_users_may_sign_in(UserStatus status)
    {
        var u = NewUser();
        var restored = User.Restore(u.Id, u.Username, u.Email, u.Name, u.DisplayName, null, status);

        Assert.Equal(AuthenErrors.AccountNotActive, restored.EnsureCanSignIn().FirstError);
    }

    [Fact]
    public void Entities_with_the_same_id_are_equal()
    {
        var id = UserId.New();

        Assert.Equal(NewUser(id), NewUser(id));
        Assert.NotEqual(NewUser(), NewUser());
    }

    [Fact]
    public void A_reset_token_can_be_used_once_and_only_before_it_expires()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var token = PasswordResetToken.Issue(UserId.New(), [1], now.AddMinutes(30));

        Assert.True(token.IsUsableAt(now));
        Assert.False(token.IsUsableAt(now.AddMinutes(30)));

        Assert.True(token.Consume(now).IsSuccess);
        Assert.Equal(AuthenErrors.InvalidResetToken, token.Consume(now).FirstError);
        Assert.False(token.IsUsableAt(now));
    }

    [Fact]
    public void An_expired_reset_token_cannot_be_consumed()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var token = PasswordResetToken.Issue(UserId.New(), [1], now.AddMinutes(30));

        Assert.True(token.Consume(now.AddHours(1)).IsFailure);
        Assert.Null(token.ConsumedAtUtc);
    }
}
