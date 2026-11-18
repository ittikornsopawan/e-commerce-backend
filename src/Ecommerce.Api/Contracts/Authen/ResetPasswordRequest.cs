namespace Ecommerce.Api.Contracts.Authen;

/// <summary>Body of POST v1/auth/reset-password.</summary>
public sealed record RequestPasswordResetBody(string? EmailAddress);
