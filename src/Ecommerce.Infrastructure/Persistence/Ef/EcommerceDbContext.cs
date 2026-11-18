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
        modelBuilder.Entity<UserRecord>().ToTable("m_user", "authen");
        modelBuilder.Entity<UserPasswordRecord>().ToTable("m_user_password", "authen");
        modelBuilder.Entity<UserInformationRecord>().ToTable("m_user_information", "authen");
        modelBuilder.Entity<UserAddressRecord>().ToTable("t_user_address", "authen");
        modelBuilder.Entity<UserConsentRecord>().ToTable("t_user_consent", "authen");
        modelBuilder.Entity<UserReferralRecord>().ToTable("l_user_referral", "authen");
        modelBuilder.Entity<ConsentRecord>().ToTable("t_consent", "consent");
        modelBuilder.Entity<ConsentTypeRecord>().ToTable("m_consent_type", "consent");

        modelBuilder.Entity<UserRecord>().Property(u => u.CreatedAt).HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase(property.Name));
            }

            // Ids come from the database default (INSERT ... RETURNING id), as in the spec SQL.
            entity.FindPrimaryKey()!.Properties[0].SetDefaultValueSql("GEN_RANDOM_UUID()");
        }
    }

    private static string SnakeCase(string name) => UpperLetter().Replace(name, "_$1").TrimStart('_').ToLowerInvariant();

    [GeneratedRegex("([A-Z])")]
    private static partial Regex UpperLetter();
}
