using Ecommerce.Domain.Authen.ValueObjects;
using Npgsql;

namespace Ecommerce.Infrastructure.Tests;

/// <summary>Test-only rows for the fixtures of the authen test-cases.md files (raw SQL is allowed in tests).</summary>
internal static class RegistrationFixtures
{
    public static readonly byte[] FieldKey = new byte[32];

    public static async Task<Guid> ConsentTypeAsync(string connectionString, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString,
            "INSERT INTO consent.m_consent_type (id, created_by, row_status, name) VALUES (@id, 'test', @status, @id::text)",
            ("id", id), ("status", rowStatus));
        return id;
    }

    public static async Task<Guid> ConsentAsync(
        string connectionString, Guid typeId, bool required, string version = "1.0.0", string rowStatus = "ACTIVE",
        TimeSpan? effectiveFromNow = null, TimeSpan? expiredFromNow = null)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await ExecuteAsync(connectionString,
            """
            INSERT INTO consent.t_consent (id, created_by, row_status, code, consent_type_id, consent_version, consent_name,
                effective_at, expired_at, is_required, body)
            VALUES (@id, 'test', @status, @id::text, @type, @version, 'Consent', @effective, @expired, @required, '{}'::jsonb)
            """,
            ("id", id), ("status", rowStatus), ("type", typeId), ("version", version),
            ("effective", now + (effectiveFromNow ?? TimeSpan.FromDays(-1))),
            ("expired", expiredFromNow is null ? DBNull.Value : now + expiredFromNow.Value),
            ("required", required));
        return id;
    }

    /// <summary>Makes every existing consent not applicable, so a case controls exactly which consents apply.</summary>
    public static Task RetireAllConsentsAsync(string connectionString) =>
        ExecuteAsync(connectionString, "UPDATE consent.t_consent SET row_status = 'INACTIVE'");

    public static NewRegistration Registration(
        string? suffix = null, Guid? referrerId = null, RegistrationAddress? address = null,
        IReadOnlyList<SubmittedConsent>? consents = null, bool full = false)
    {
        suffix ??= Guid.NewGuid().ToString("N")[..12];
        return new NewRegistration(
            $"user.{suffix}", $"user.{suffix}@example.test", new PasswordCredential([1, 2, 3], new byte[16]),
            full ? "John" : null, full ? "Doe" : null, full ? "John Doe" : null, full ? "/profiles/a.png" : null,
            full ? "0812345678" : null, full ? "123 Road" : null, full ? new DateOnly(1990, 1, 31) : null, full ? "MALE" : null,
            referrerId, address, consents ?? [], DateTime.UtcNow);
    }

    public static async Task<long> CountAsync(string connectionString, string table, Guid userId)
    {
        await using var connection = new NpgsqlConnection(Ecommerce.Infrastructure.Persistence.Ef.EcommerceDbContext.UtcSession(connectionString));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT COUNT(*) FROM {table} WHERE user_id = @id", connection);
        command.Parameters.AddWithValue("id", userId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Ecommerce.Infrastructure.Persistence.Ef.EcommerceDbContext.UtcSession(connectionString));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    // Author fixtures (02-login claims).
    public static async Task<Guid> AuthorAsync(string connectionString, string table, string column, string value, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString,
            $"INSERT INTO {table} (id, created_by, row_status, {column}, description) VALUES (@id, 'test', @status, @value, 'test')",
            ("id", id), ("status", rowStatus), ("value", value));
        return id;
    }

    public static async Task<Guid> ScopeAsync(string connectionString, string code, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString,
            "INSERT INTO author.m_scope (id, created_by, row_status, scope, name, description) VALUES (@id, 'test', @status, @code, @code, 'test')",
            ("id", id), ("status", rowStatus), ("code", code));
        return id;
    }

    public static async Task<Guid> LinkAsync(string connectionString, string table, (string Column, Guid Value) a, (string Column, Guid Value) b, string rowStatus = "ACTIVE")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connectionString,
            $"INSERT INTO {table} (id, created_by, row_status, {a.Column}, {b.Column}) VALUES (@id, 'test', @status, @a, @b)",
            ("id", id), ("status", rowStatus), ("a", a.Value), ("b", b.Value));
        return id;
    }
}
