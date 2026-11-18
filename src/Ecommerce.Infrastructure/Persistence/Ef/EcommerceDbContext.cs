using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Ef;

/// <summary>The EF Core model over the schema owned by the Showcase migrations (the migrations are the source of truth).</summary>
public sealed class EcommerceDbContext(DbContextOptions<EcommerceDbContext> options) : DbContext(options)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();

    public static EcommerceDbContext Create(string connectionString) =>
        new(Configure(new DbContextOptionsBuilder<EcommerceDbContext>(), connectionString).Options);

    public static DbContextOptionsBuilder<EcommerceDbContext> Configure(DbContextOptionsBuilder<EcommerceDbContext> builder, string connectionString) =>
        builder.UseNpgsql(connectionString).AddInterceptors(new ForUpdateInterceptor());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRecord>(e =>
        {
            e.ToTable("m_user", "authen");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(u => u.CreatedBy).HasColumnName("created_by");
            e.Property(u => u.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd();
            e.Property(u => u.RowStatus).HasColumnName("row_status");
            e.Property(u => u.Username).HasColumnName("username");
            e.Property(u => u.EmailAddress).HasColumnName("email_address");
        });
    }
}
