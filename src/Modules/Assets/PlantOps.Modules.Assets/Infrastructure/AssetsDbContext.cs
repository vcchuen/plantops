using Microsoft.EntityFrameworkCore;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Infrastructure;

internal sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options) : DbContext(options)
{
    public const string Schema = "assets";

    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Convention-level so the converters apply to every property of these types.
        configurationBuilder.Properties<AssetId>().HaveConversion<AssetIdConverter>();
        configurationBuilder.Properties<ProductionLineId>().HaveConversion<ProductionLineIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AssetConfiguration());
        modelBuilder.ApplyConfiguration(new ProductionLineConfiguration());
        modelBuilder.ApplyAuditEntries();
    }
}
