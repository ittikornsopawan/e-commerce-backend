using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the SQL of spec 02-login (Database Relations), in its lock order.</summary>
internal sealed class LoginRepository(EcommerceDbContext db, FieldProtector protector) : ILoginRepository
{
    public Task LockIdentifierAsync(string identifier, CancellationToken cancellationToken) =>
        db.Database.AcquireTransactionLockAsync(LockKey(IdentifierHash(identifier)), cancellationToken);

    /// <summary>The signed 64-bit big-endian value of the first eight bytes of the identifier hash (sequence advisory note).</summary>
    public static long LockKey(byte[] identifierHash) => BinaryPrimitives.ReadInt64BigEndian(identifierHash.AsSpan(0, 8));

    /// <summary>SHA-256 of the exact UTF-8 identifier as sent.</summary>
    public static byte[] IdentifierHash(string identifier) => SHA256.HashData(Encoding.UTF8.GetBytes(identifier));

    public Task<int> CountRecentFailuresAsync(string identifier, DateTime windowStartUtc, CancellationToken cancellationToken)
    {
        var hash = IdentifierHash(identifier);
        return db.LoginAttempts.CountAsync(a => a.IdentifierHash == hash && a.Result == "FAILED" && a.CreatedAt > windowStartUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<LoginUser>> LockActiveUsersAsync(string identifier, CancellationToken cancellationToken)
    {
        var bytes = protector.Protect(identifier);
        var rows = await db.Users
            .Where(u => (u.Username == bytes || u.EmailAddress == bytes) && u.RowStatus == "ACTIVE")
            .OrderBy(u => u.Id)
            .ForUpdate()
            .Select(u => new { u.Id, u.Username, u.EmailAddress, u.DisplayName })
            .ToListAsync(cancellationToken);

        return rows.Select(u => new LoginUser(u.Id, protector.Unprotect(u.Username), protector.Unprotect(u.EmailAddress), u.DisplayName)).ToList();
    }

    public async Task<PasswordCredential?> GetCurrentPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await db.UserPasswords
            .Where(p => p.UserId == userId && p.RowType == "PASSWORD" && p.RowStatus == "ACTIVE")
            .Select(p => new { p.Password, p.PasswordSalt })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new PasswordCredential(row.Password, row.PasswordSalt);
    }

    public async Task<AuthorizationFacts> GetAuthorizationFactsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // QUERY - Get active role ids, then Get active roles
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == userId && ur.RowStatus == "ACTIVE").Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        var roles = await db.Roles.Where(r => roleIds.Contains(r.Id) && r.RowStatus == "ACTIVE")
            .Select(r => new RoleFact(r.Id, r.Name)).ToListAsync(cancellationToken);
        var activeRoleIds = roles.Select(r => r.RoleId).ToList();

        // QUERY - Get active role permissions, then Get active role permission scopes
        var rolePermissions = await db.RolePermissions.Where(rp => activeRoleIds.Contains(rp.RoleId) && rp.RowStatus == "ACTIVE")
            .Select(rp => new RolePermissionFact(rp.Id, rp.PermissionId)).ToListAsync(cancellationToken);
        var rolePermissionIds = rolePermissions.Select(rp => rp.RolePermissionId).ToList();
        var links = await db.RolePermissionScopes.Where(l => rolePermissionIds.Contains(l.RolePermissionId) && l.RowStatus == "ACTIVE")
            .Select(l => new RoleScopeLinkFact(l.RolePermissionId, l.PermissionScopeId)).ToListAsync(cancellationToken);

        // QUERY - Get active direct permissions
        var direct = await db.UserPermissions.Where(up => up.UserId == userId && up.RowStatus == "ACTIVE")
            .Select(up => new DirectPermissionFact(up.PermissionId, up.PermissionScopeId)).ToListAsync(cancellationToken);

        // QUERY - Get permission scope definitions, then Get active permissions (permissionIds = role and direct union)
        var permissionIds = rolePermissions.Select(rp => rp.PermissionId).Concat(direct.Select(d => d.PermissionId)).Distinct().ToList();
        var definitions = await db.PermissionScopes.Where(ps => permissionIds.Contains(ps.PermissionId) && ps.RowStatus != "DELETED")
            .Select(ps => new PermissionScopeDefinition(ps.Id, ps.PermissionId, ps.ScopeId, ps.RowStatus)).ToListAsync(cancellationToken);
        var permissions = await db.Permissions.Where(p => permissionIds.Contains(p.Id) && p.RowStatus == "ACTIVE")
            .Select(p => new PermissionFact(p.Id, p.Name)).ToListAsync(cancellationToken);

        return new AuthorizationFacts(roles, rolePermissions, links, direct, definitions, permissions);
    }

    public async Task<IReadOnlyList<ScopeFact>> GetActiveScopesAsync(IReadOnlyList<Guid> scopeIds, CancellationToken cancellationToken)
    {
        var ids = scopeIds.ToList();
        return await db.Scopes.Where(s => ids.Contains(s.Id) && s.RowStatus == "ACTIVE")
            .Select(s => new ScopeFact(s.Id, s.Scope)).ToListAsync(cancellationToken);
    }

    public async Task AddSessionTokensAsync(LoginSession session, CancellationToken cancellationToken)
    {
        var createdBy = session.UserId.ToString();
        db.Add(new AccessTokenRecord
        {
            CreatedBy = createdBy, UserId = session.UserId, SessionId = session.SessionId, Token = session.AccessTokenHash, ExpiredAt = session.AccessExpiresAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);
        db.Add(new RefreshTokenRecord
        {
            CreatedBy = createdBy, UserId = session.UserId, SessionId = session.SessionId, Token = session.RefreshTokenHash, ExpiredAt = session.RefreshExpiresAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAttemptAsync(string identifier, Guid? userId, LoginResult result, DateTime nowUtc, CancellationToken cancellationToken)
    {
        db.Add(new LoginAttemptRecord
        {
            CreatedBy = "system", CreatedAt = nowUtc, IdentifierHash = IdentifierHash(identifier), UserId = userId,
            Result = result.ToString().ToUpperInvariant()
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
