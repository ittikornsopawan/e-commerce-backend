using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Domain;

/// <summary>
/// Resolves the effective grants of a user (spec 02-login, sequence claim notes 1-6). A scope definition is a catalogue,
/// never a grant: a scoped grant needs an ACTIVE definition whose permission equals the granted permission; an unscoped
/// grant counts only for a permission with no non-DELETED definition; a failed link is never read as unscoped.
/// </summary>
public static class AuthorizationClaims
{
    private sealed record Grant(Guid PermissionId, Guid? ScopeId);

    /// <summary>The scope ids the "Get active scopes" query reads: only those of matched assignment-definition pairs.</summary>
    public static IReadOnlyList<Guid> AssignedScopeIds(AuthorizationFacts facts) =>
        Grants(facts).Where(g => g.ScopeId is not null).Select(g => g.ScopeId!.Value).Distinct().ToList();

    public static ResolvedClaims Resolve(AuthorizationFacts facts, IReadOnlyList<ScopeFact> activeScopes)
    {
        var scopes = activeScopes.ToDictionary(s => s.ScopeId, s => s.Code);
        var surviving = Grants(facts).Where(g => g.ScopeId is null || scopes.ContainsKey(g.ScopeId.Value)).ToList();
        var names = facts.ActivePermissions.ToDictionary(p => p.PermissionId, p => p.Name);

        return new ResolvedClaims(
            Roles: facts.Roles.DistinctBy(r => r.RoleId).OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.RoleId)
                .Select(r => r.Name).Distinct().ToList(),
            Permissions: surviving.Select(g => ClaimName(names[g.PermissionId])).Distinct().Order(StringComparer.Ordinal).ToList(),
            Scopes: surviving.Where(g => g.ScopeId is not null).Select(g => scopes[g.ScopeId!.Value].ToLowerInvariant() + ":*")
                .Distinct().Order(StringComparer.Ordinal).ToList());
    }

    /// <summary><c>catalog.manage</c> → <c>catalog:manage</c>; <c>shop.product.manage</c> → <c>shop.product:manage</c>.</summary>
    public static string ClaimName(string permissionName)
    {
        var dot = permissionName.LastIndexOf('.');
        return dot < 0 ? permissionName : permissionName[..dot] + ":" + permissionName[(dot + 1)..];
    }

    private static List<Grant> Grants(AuthorizationFacts facts)
    {
        var active = facts.ActivePermissions.Select(p => p.PermissionId).ToHashSet();
        var scoped = facts.Definitions.Select(d => d.PermissionId).ToHashSet();
        var activeDefinitions = facts.Definitions.Where(d => d.RowStatus == "ACTIVE").ToDictionary(d => d.Id);
        var grants = new List<Grant>();

        foreach (var rolePermission in facts.RolePermissions.Where(rp => active.Contains(rp.PermissionId)))
        {
            var links = facts.RoleScopeLinks.Where(l => l.RolePermissionId == rolePermission.RolePermissionId).ToList();
            if (links.Count == 0)
            {
                if (!scoped.Contains(rolePermission.PermissionId)) grants.Add(new Grant(rolePermission.PermissionId, null));
                continue;
            }

            grants.AddRange(links
                .Select(l => activeDefinitions.GetValueOrDefault(l.PermissionScopeId))
                .Where(d => d is not null && d.PermissionId == rolePermission.PermissionId)
                .Select(d => new Grant(rolePermission.PermissionId, d!.ScopeId)));
        }

        foreach (var direct in facts.DirectPermissions.Where(dp => active.Contains(dp.PermissionId)))
        {
            if (direct.PermissionScopeId is null)
            {
                if (!scoped.Contains(direct.PermissionId)) grants.Add(new Grant(direct.PermissionId, null));
            }
            else if (activeDefinitions.GetValueOrDefault(direct.PermissionScopeId.Value) is { } definition && definition.PermissionId == direct.PermissionId)
            {
                grants.Add(new Grant(direct.PermissionId, definition.ScopeId));
            }
        }

        return grants;
    }
}

/// <summary>Brute-force limit of spec 02-login: 5 FAILED attempts in the window refuse the next one.</summary>
public static class LoginThrottle
{
    public const int MaxFailures = 5;

    public static bool IsRefused(int recentFailures) => recentFailures >= MaxFailures;
}
