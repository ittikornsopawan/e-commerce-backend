namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>A refresh row reread under the user lock (spec 05 "Reread the refresh token after user lock").</summary>
public sealed record RefreshTokenRow(Guid Id, Guid UserId, Guid SessionId, string RowStatus, DateTime? UpdatedAt, DateTime ExpiredAt);

/// <summary>What spec 05 does with the freshly read row.</summary>
public enum RefreshDecision
{
    /// <summary>ACTIVE and unexpired: consume once and issue a fresh pair.</summary>
    Rotate,

    /// <summary>INACTIVE less than the grace window ago: 401, nothing changes (a concurrent duplicate).</summary>
    RecentReplay,

    /// <summary>INACTIVE at or after the grace window, or with no updated_at: close the session, commit, 401.</summary>
    Replay,

    /// <summary>Missing, expired or another status: 401, nothing changes.</summary>
    Invalid
}
