using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantOps.Modules.Reporting.Infrastructure.Migrations
{
    /// <inheritdoc />
    internal partial class AddFactsCompletedAtCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderFacts_CompletedAt",
                schema: "reporting",
                table: "WorkOrderFacts",
                column: "CompletedAt")
                .Annotation("SqlServer:Include", new[] { "LineId", "LineName", "Priority", "MetSla", "RepairMinutes", "DowntimeMinutes", "AssetId", "AssetTag", "CompletedMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrderFacts_CompletedAt",
                schema: "reporting",
                table: "WorkOrderFacts");
        }
    }
}
