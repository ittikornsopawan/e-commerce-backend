namespace Ecommerce.Domain.Authen.Domain;

/// <summary>Spec 03-reset-password: a user may receive at most <c>maxRequests</c> resets in the rolling window (3 per hour by default).</summary>
public static class PasswordResetThrottle
{
    public static bool IsRefused(int requestsInWindow, int maxRequests) => requestsInWindow >= maxRequests;
}
