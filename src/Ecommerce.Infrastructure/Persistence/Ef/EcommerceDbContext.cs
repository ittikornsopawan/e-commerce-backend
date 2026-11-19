using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Ef;

/// <summary>The EF Core model over the schema owned by the Showcase migrations (the migrations are the source of truth).</summary>
public sealed partial class EcommerceDbContext(DbContextOptions<EcommerceDbContext> options) : DbContext(options)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<UserPasswordRecord> UserPasswords => Set<UserPasswordRecord>();
    public DbSet<UserInformationRecord> UserInformation => Set<UserInformationRecord>();
    public DbSet<UserAddressRecord> UserAddresses => Set<UserAddressRecord>();
    public DbSet<UserConsentRecord> UserConsents => Set<UserConsentRecord>();
    public DbSet<UserReferralRecord> UserReferrals => Set<UserReferralRecord>();
    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();
    public DbSet<ConsentTypeRecord> ConsentTypes => Set<ConsentTypeRecord>();
    public DbSet<LoginAttemptRecord> LoginAttempts => Set<LoginAttemptRecord>();
    public DbSet<AccessTokenRecord> AccessTokens => Set<AccessTokenRecord>();
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<ScopeRecord> Scopes => Set<ScopeRecord>();
    public DbSet<PermissionScopeRecord> PermissionScopes => Set<PermissionScopeRecord>();
    public DbSet<RolePermissionRecord> RolePermissions => Set<RolePermissionRecord>();
    public DbSet<RolePermissionScopeRecord> RolePermissionScopes => Set<RolePermissionScopeRecord>();
    public DbSet<UserRoleRecord> UserRoles => Set<UserRoleRecord>();
    public DbSet<UserPermissionRecord> UserPermissions => Set<UserPermissionRecord>();
    public DbSet<PasswordResetRecord> PasswordResets => Set<PasswordResetRecord>();

    public static EcommerceDbContext Create(string connectionString, int commandTimeoutSeconds = 30) =>
        new(Configure(new DbContextOptionsBuilder<EcommerceDbContext>(), connectionString, commandTimeoutSeconds).Options);

    public static DbContextOptionsBuilder<EcommerceDbContext> Configure(
        DbContextOptionsBuilder<EcommerceDbContext> builder, string connectionString, int commandTimeoutSeconds = 30) =>
        builder.UseNpgsql(UtcSession(connectionString), o => o.CommandTimeout(commandTimeoutSeconds)).AddInterceptors(new ForUpdateInterceptor());

    /// <summary>
    /// The schema stores UTC in TIMESTAMP WITHOUT TIME ZONE. A UTC parameter is sent as timestamptz, which PostgreSQL
    /// converts with the session time zone, so the session must be UTC or every stored time shifts by the server offset.
    /// </summary>
    public static string UtcSession(string connectionString) =>
        new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { Timezone = "UTC" }.ConnectionString;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        MapTables(modelBuilder);
        MapDatabaseDefaults(modelBuilder);
        ApplyConventions(modelBuilder);
    }

    private static void MapTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRecord>().ToTable("m_user", "authen");
        modelBuilder.Entity<UserPasswordRecord>().ToTable("m_user_password", "authen");
        modelBuilder.Entity<UserInformationRecord>().ToTable("m_user_information", "authen");
        modelBuilder.Entity<UserAddressRecord>().ToTable("t_user_address", "authen");
        modelBuilder.Entity<UserConsentRecord>().ToTable("t_user_consent", "authen");
        modelBuilder.Entity<UserReferralRecord>().ToTable("l_user_referral", "authen");
        modelBuilder.Entity<ConsentRecord>().ToTable("t_consent", "consent");
        modelBuilder.Entity<ConsentTypeRecord>().ToTable("m_consent_type", "consent");
        modelBuilder.Entity<LoginAttemptRecord>().ToTable("l_user_login_attempt", "authen");
        modelBuilder.Entity<AccessTokenRecord>().ToTable("t_user_access_token", "authen");
        modelBuilder.Entity<RefreshTokenRecord>().ToTable("t_user_refresh_token", "authen");
        modelBuilder.Entity<RoleRecord>().ToTable("m_role", "author");
        modelBuilder.Entity<PermissionRecord>().ToTable("m_permission", "author");
        modelBuilder.Entity<ScopeRecord>().ToTable("m_scope", "author");
        modelBuilder.Entity<PermissionScopeRecord>().ToTable("t_permission_scope", "author");
        modelBuilder.Entity<RolePermissionRecord>().ToTable("t_role_permission", "author");
        modelBuilder.Entity<RolePermissionScopeRecord>().ToTable("t_role_permission_scope", "author");
        modelBuilder.Entity<UserRoleRecord>().ToTable("t_user_role", "author");
        modelBuilder.Entity<UserPermissionRecord>().ToTable("t_user_permission", "author");
        modelBuilder.Entity<PasswordResetRecord>().ToTable("t_user_reset_password", "authen");
    }

    private static void MapDatabaseDefaults(ModelBuilder modelBuilder)
    {
        // created_at comes from the database default unless the code sets it (spec inserts that bind now explicitly).
        modelBuilder.Entity<UserRecord>().Property(u => u.CreatedAt).HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
        modelBuilder.Entity<UserAddressRecord>().Property(a => a.CreatedAt).HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
    }

    /// <summary>snake_case columns, UTC times and database-generated ids for every mapped table.</summary>
    private static void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties()) ApplyColumnConventions(property);

            // Ids come from the database default (INSERT ... RETURNING id), as in the spec SQL.
            entity.FindPrimaryKey()!.Properties[0].SetDefaultValueSql("GEN_RANDOM_UUID()");
        }
    }

    private static void ApplyColumnConventions(Microsoft.EntityFrameworkCore.Metadata.IMutableProperty property)
    {
        property.SetColumnName(SnakeCase(property.Name));

        // The columns hold UTC without a time zone; a value read back must be UTC too, or Npgsql refuses to send it again.
        if (property.ClrType == typeof(DateTime)) property.SetValueConverter(UtcDateTime);
        if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(UtcNullableDateTime);
    }

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime> UtcDateTime =
        new(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?> UtcNullableDateTime =
        new(v => v, v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

    private static string SnakeCase(string name) => UpperLetter().Replace(name, "_$1").TrimStart('_').ToLowerInvariant();

    [GeneratedRegex("([A-Z])")]
    private static partial Regex UpperLetter();
}
