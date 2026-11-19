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

    /// <summary>The lookups both grant sources share: ACTIVE permissions, permissions with any definition, ACTIVE definitions.</summary>
    private sealed record GrantContext(
        HashSet<Guid> Active, HashSet<Guid> Scoped, Dictionary<Guid, PermissionScopeDefinition> ActiveDefinitions)
    {
        /// <summary>An unscoped grant counts only for a permission that has no non-DELETED definition.</summary>
        public bool AllowsUnscoped(Guid permissionId) => !Scoped.Contains(permissionId);

        /// <summary>A scoped grant needs an ACTIVE definition of the same permission.</summary>
        public PermissionScopeDefinition? Definition(Guid permissionScopeId, Guid permissionId) =>
            ActiveDefinitions.GetValueOrDefault(permissionScopeId) is { } d && d.PermissionId == permissionId ? d : null;
    }

    private static List<Grant> Grants(AuthorizationFacts facts)
    {
        var context = new GrantContext(
            facts.ActivePermissions.Select(p => p.PermissionId).ToHashSet(),
            facts.Definitions.Select(d => d.PermissionId).ToHashSet(),
            facts.Definitions.Where(d => d.RowStatus == "ACTIVE").ToDictionary(d => d.Id));

        return RoleGrants(facts, context).Concat(DirectGrants(facts, context)).ToList();
    }

    private static IEnumerable<Grant> RoleGrants(AuthorizationFacts facts, GrantContext context)
    {
        foreach (var rolePermission in facts.RolePermissions.Where(rp => context.Active.Contains(rp.PermissionId)))
        {
            var links = facts.RoleScopeLinks.Where(l => l.RolePermissionId == rolePermission.RolePermissionId).ToList();
            if (links.Count == 0 && context.AllowsUnscoped(rolePermission.PermissionId))
                yield return new Grant(rolePermission.PermissionId, null);

            // A failed link is never read as unscoped: it simply grants nothing.
            foreach (var definition in links.Select(l => context.Definition(l.PermissionScopeId, rolePermission.PermissionId)).OfType<PermissionScopeDefinition>())
                yield return new Grant(rolePermission.PermissionId, definition.ScopeId);
        }
    }

    private static IEnumerable<Grant> DirectGrants(AuthorizationFacts facts, GrantContext context)
    {
        foreach (var direct in facts.DirectPermissions.Where(dp => context.Active.Contains(dp.PermissionId)))
        {
            if (direct.PermissionScopeId is null)
            {
                if (context.AllowsUnscoped(direct.PermissionId)) yield return new Grant(direct.PermissionId, null);
            }
            else if (context.Definition(direct.PermissionScopeId.Value, direct.PermissionId) is { } definition)
            {
                yield return new Grant(direct.PermissionId, definition.ScopeId);
            }
        }
    }
}

/// <summary>Brute-force limit of spec 02-login: 5 FAILED attempts in the window refuse the next one.</summary>
public static class LoginThrottle
{
    public const int MaxFailures = 5;

    public static bool IsRefused(int recentFailures) => recentFailures >= MaxFailures;
}
