using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Tests.Authen;

// Spec 05-refresh-token, sequence reuse note (TC-REFRESH-004, 006, 007).
public sealed class RefreshTokenReuseTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    private static RefreshTokenRow Row(string status, DateTime? updatedAt = null, int expiresInMinutes = 60) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, updatedAt, Now.AddMinutes(expiresInMinutes));

    [Fact]
    public void TC_REFRESH_001_an_active_unexpired_row_rotates() =>
        Assert.Equal(RefreshDecision.Rotate, RefreshTokenReuse.Classify(Row("ACTIVE"), Now, Grace));

    [Fact]
    public void TC_REFRESH_006_an_inactive_row_used_inside_the_grace_window_is_a_recent_replay() =>
        Assert.Equal(RefreshDecision.RecentReplay, RefreshTokenReuse.Classify(Row("INACTIVE", Now.AddSeconds(-9.999)), Now, Grace));

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    public void TC_REFRESH_007_an_inactive_row_at_or_after_the_grace_window_is_a_replay(int secondsAgo) =>
        Assert.Equal(RefreshDecision.Replay, RefreshTokenReuse.Classify(Row("INACTIVE", Now.AddSeconds(-secondsAgo)), Now, Grace));

    [Fact]
    public void TC_REFRESH_007_an_inactive_row_without_updated_at_is_a_replay() =>
        Assert.Equal(RefreshDecision.Replay, RefreshTokenReuse.Classify(Row("INACTIVE"), Now, Grace));

    [Theory]
    [InlineData("ACTIVE", 0)]
    [InlineData("INACTIVE", 0)]
    [InlineData("DELETED", 60)]
    [InlineData("LOCKED", 60)]
    public void TC_REFRESH_004_an_expired_row_or_another_status_is_invalid(string status, int expiresInMinutes) =>
        Assert.Equal(RefreshDecision.Invalid, RefreshTokenReuse.Classify(Row(status, Now.AddMinutes(-5), expiresInMinutes), Now, Grace));

    [Fact]
    public void TC_REFRESH_004_a_missing_row_is_invalid() =>
        Assert.Equal(RefreshDecision.Invalid, RefreshTokenReuse.Classify(null, Now, Grace));
}
