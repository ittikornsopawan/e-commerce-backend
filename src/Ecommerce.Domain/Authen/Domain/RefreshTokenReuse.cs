using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Domain;

/// <summary>Classifies a refresh row read under the user lock (spec 05-refresh-token, sequence reuse note).</summary>
public static class RefreshTokenReuse
{
    public static RefreshDecision Classify(RefreshTokenRow? row, DateTime nowUtc, TimeSpan grace)
    {
        if (row is null || row.ExpiredAt <= nowUtc) return RefreshDecision.Invalid;

        return row.RowStatus switch
        {
            "ACTIVE" => RefreshDecision.Rotate,
            "INACTIVE" when row.UpdatedAt is { } updatedAt && nowUtc < updatedAt + grace => RefreshDecision.RecentReplay,
            "INACTIVE" => RefreshDecision.Replay,
            _ => RefreshDecision.Invalid
        };
    }
}
