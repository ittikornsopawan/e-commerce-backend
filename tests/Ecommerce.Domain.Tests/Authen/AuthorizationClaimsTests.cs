using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Tests.Authen;

// Spec 02-login, sequence claim notes 1-6 (TC-LOGIN-017).
public sealed class AuthorizationClaimsTests
{
    private static readonly Guid RoleA = Guid.NewGuid(), RoleB = Guid.NewGuid();
    private static readonly Guid CatalogManage = Guid.NewGuid(), ShopProductManage = Guid.NewGuid(), OrderView = Guid.NewGuid(), ReportView = Guid.NewGuid();
    private static readonly Guid Own = Guid.NewGuid(), All = Guid.NewGuid();
    private static readonly Guid ShopOwnDef = Guid.NewGuid(), ShopAllDef = Guid.NewGuid(), ReportAllDef = Guid.NewGuid();
    private static readonly Guid RpCatalog = Guid.NewGuid(), RpShop = Guid.NewGuid(), RpReport = Guid.NewGuid();

    private static AuthorizationFacts Facts(params DirectPermissionFact[] direct) => new(
        Roles: [new RoleFact(RoleB, "B_ROLE"), new RoleFact(RoleA, "A_ROLE")],
        RolePermissions: [new RolePermissionFact(RpCatalog, CatalogManage), new RolePermissionFact(RpShop, ShopProductManage), new RolePermissionFact(RpReport, ReportView)],
        RoleScopeLinks: [new RoleScopeLinkFact(RpShop, ShopOwnDef)],
        DirectPermissions: direct,
        Definitions:
        [
            new PermissionScopeDefinition(ShopOwnDef, ShopProductManage, Own, "ACTIVE"),
            new PermissionScopeDefinition(ShopAllDef, ShopProductManage, All, "ACTIVE"),
            new PermissionScopeDefinition(ReportAllDef, ReportView, All, "ACTIVE")
        ],
        ActivePermissions:
        [
            new PermissionFact(CatalogManage, "catalog.manage"), new PermissionFact(ShopProductManage, "shop.product.manage"),
            new PermissionFact(OrderView, "order.view"), new PermissionFact(ReportView, "report.view")
        ]);

    private static readonly ScopeFact[] Scopes = [new(Own, "OWN"), new(All, "ALL")];

    [Fact]
    public void TC_LOGIN_017_resolves_roles_permissions_and_scopes_as_the_spec_maps_them()
    {
        var facts = Facts(new DirectPermissionFact(OrderView, null), new DirectPermissionFact(CatalogManage, null));

        var claims = AuthorizationClaims.Resolve(facts, Scopes);

        Assert.Equal(["A_ROLE", "B_ROLE"], claims.Roles);
        Assert.Equal(["catalog:manage", "order:view", "shop.product:manage"], claims.Permissions);
        Assert.Equal(["own:*"], claims.Scopes);
    }

    [Fact]
    public void TC_LOGIN_017_only_scopes_of_matched_assignments_are_read()
    {
        Assert.Equal([Own], AuthorizationClaims.AssignedScopeIds(Facts()));
    }

    [Fact]
    public void TC_LOGIN_017_a_scoped_grant_whose_scope_is_not_active_is_omitted()
    {
        var claims = AuthorizationClaims.Resolve(Facts(), [new ScopeFact(All, "ALL")]);
        Assert.DoesNotContain("shop.product:manage", claims.Permissions);
        Assert.Empty(claims.Scopes);
    }

    [Fact]
    public void TC_LOGIN_017_a_direct_grant_needs_the_exact_active_definition_of_its_permission()
    {
        // ShopOwnDef belongs to ShopProductManage, not to OrderView: no grant.
        var claims = AuthorizationClaims.Resolve(Facts(new DirectPermissionFact(OrderView, ShopOwnDef)) with { RoleScopeLinks = [] }, Scopes);
        Assert.DoesNotContain("order:view", claims.Permissions);
        // A NULL direct scope does not unlock a scoped permission.
        claims = AuthorizationClaims.Resolve(Facts(new DirectPermissionFact(ShopProductManage, null)) with { RoleScopeLinks = [] }, Scopes);
        Assert.DoesNotContain("shop.product:manage", claims.Permissions);
        // An exact active definition does.
        claims = AuthorizationClaims.Resolve(Facts(new DirectPermissionFact(ShopProductManage, ShopAllDef)) with { RoleScopeLinks = [] }, Scopes);
        Assert.Contains("shop.product:manage", claims.Permissions);
        Assert.Equal(["all:*"], claims.Scopes);
    }

    [Fact]
    public void TC_LOGIN_017_an_inactive_definition_grants_nothing()
    {
        var facts = Facts() with
        {
            Definitions = [new PermissionScopeDefinition(ShopOwnDef, ShopProductManage, Own, "INACTIVE"), new PermissionScopeDefinition(ReportAllDef, ReportView, All, "ACTIVE")]
        };
        Assert.DoesNotContain("shop.product:manage", AuthorizationClaims.Resolve(facts, Scopes).Permissions);
    }

    [Fact]
    public void TC_LOGIN_017_no_grants_give_empty_arrays()
    {
        var claims = AuthorizationClaims.Resolve(new AuthorizationFacts([], [], [], [], [], []), []);
        Assert.Empty(claims.Roles);
        Assert.Empty(claims.Permissions);
        Assert.Empty(claims.Scopes);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void TC_LOGIN_013_five_failures_refuse(int failed, bool refused) => Assert.Equal(refused, LoginThrottle.IsRefused(failed));
}
