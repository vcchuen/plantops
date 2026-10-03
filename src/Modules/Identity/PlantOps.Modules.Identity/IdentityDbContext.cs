using Microsoft.EntityFrameworkCore;

namespace PlantOps.Modules.Identity;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("Users");
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Id).HasMaxLength(User.IdMaxLength).ValueGeneratedNever();
            builder.Property(u => u.Name).HasMaxLength(User.NameMaxLength);
            builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
            builder.Property(u => u.RolesText).HasColumnName("Roles").HasMaxLength(User.RolesMaxLength);
            builder.Ignore(u => u.Roles);
        });
    }
}
