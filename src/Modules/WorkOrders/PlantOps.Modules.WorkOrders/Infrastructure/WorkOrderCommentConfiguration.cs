using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlantOps.Modules.WorkOrders.Domain;

namespace PlantOps.Modules.WorkOrders.Infrastructure;

internal sealed class WorkOrderCommentConfiguration : IEntityTypeConfiguration<WorkOrderComment>
{
    public void Configure(EntityTypeBuilder<WorkOrderComment> builder)
    {
        builder.ToTable("WorkOrderComments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.AuthorId).HasMaxLength(WorkOrderComment.PersonIdMaxLength);
        builder.Property(c => c.AuthorName).HasMaxLength(WorkOrderComment.PersonNameMaxLength);
        builder.Property(c => c.Body).HasMaxLength(WorkOrderComment.BodyMaxLength);

        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(c => c.WorkOrderId)
            .HasConstraintName("FK_WorkOrderComments_WorkOrders_WorkOrderId")
            .OnDelete(DeleteBehavior.Cascade);

        // "The comments of this work order" (the detail page).
        builder.HasIndex(c => c.WorkOrderId).HasDatabaseName("IX_WorkOrderComments_WorkOrderId");
    }
}
