namespace Ecommerce.Api.Contracts.Authen;

/// <summary>Body of POST v1/auth/login.</summary>
public sealed record LoginRequest(string? Username, string? Password);
