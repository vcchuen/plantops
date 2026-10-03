using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantOps.Modules.WorkOrders.Infrastructure.Migrations
{
    /// <inheritdoc />
    internal partial class InitialWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workorders");

            migrationBuilder.CreateSequence<int>(
                name: "WorkOrderNumbers",
                schema: "workorders");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "workorders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                schema: "workorders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false, defaultValueSql: "NEXT VALUE FOR [workorders].[WorkOrderNumbers]"),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetTag = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    AssetDown = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReportedById = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReportedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovedById = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignedToId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignedToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_AggregateId_OccurredAt",
                schema: "workorders",
                table: "AuditEntries",
                columns: new[] { "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AssetId",
                schema: "workorders",
                table: "WorkOrders",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AssignedToId",
                schema: "workorders",
                table: "WorkOrders",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status",
                schema: "workorders",
                table: "WorkOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_WorkOrders_Number",
                schema: "workorders",
                table: "WorkOrders",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "workorders");

            migrationBuilder.DropTable(
                name: "WorkOrders",
                schema: "workorders");

            migrationBuilder.DropSequence(
                name: "WorkOrderNumbers",
                schema: "workorders");
        }
    }
}
