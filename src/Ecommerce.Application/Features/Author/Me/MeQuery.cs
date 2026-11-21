using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.Me;

/// <summary>GET v1/author/me (spec 12): the caller is null when the JWT did not verify; no target user is accepted.</summary>
public sealed record MeQuery(Caller? Caller) : IQuery<MeResult>;

/// <summary>data of spec 12: the caller's id, the PLATFORM_ADMIN flag and the role names of the calling token, in token order.</summary>
public sealed record MeResult(Guid UserId, bool IsPlatformAdmin, IReadOnlyList<string> Roles);
