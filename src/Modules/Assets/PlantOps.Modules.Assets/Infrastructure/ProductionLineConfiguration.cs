using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Infrastructure;

internal sealed class ProductionLineConfiguration : IEntityTypeConfiguration<ProductionLine>
{
    // Fixed ids: seed data must be identical in every environment, or migrations would diff forever.
    public static readonly ProductionLineId Smt1 = new(Guid.Parse("0197a5c0-0000-7000-8000-000000000001"));
    public static readonly ProductionLineId Smt2 = new(Guid.Parse("0197a5c0-0000-7000-8000-000000000002"));
    public static readonly ProductionLineId Fa1 = new(Guid.Parse("0197a5c0-0000-7000-8000-000000000003"));
    public static readonly ProductionLineId Test1 = new(Guid.Parse("0197a5c0-0000-7000-8000-000000000004"));

    public void Configure(EntityTypeBuilder<ProductionLine> builder)
    {
        builder.ToTable("ProductionLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Code).HasMaxLength(20);
        builder.Property(l => l.Name).HasMaxLength(100);
        builder.HasIndex(l => l.Code).IsUnique().HasDatabaseName("UX_ProductionLines_Code");

        builder.HasData(
            new ProductionLine(Smt1, "SMT-1", "SMT Line 1"),
            new ProductionLine(Smt2, "SMT-2", "SMT Line 2"),
            new ProductionLine(Fa1, "FA-1", "Final Assembly 1"),
            new ProductionLine(Test1, "TEST-1", "Functional Test 1"));
    }
}
