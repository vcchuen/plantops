using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantOps.Modules.Assets.Infrastructure.Migrations
{
    /// <inheritdoc />
    internal partial class AddMaintenanceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "assets",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Handler = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => new { x.MessageId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TechnicianName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DowntimeMinutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_AssetId_CompletedAt",
                schema: "assets",
                table: "MaintenanceRecords",
                columns: new[] { "AssetId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_MaintenanceRecords_WorkOrderId",
                schema: "assets",
                table: "MaintenanceRecords",
                column: "WorkOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "MaintenanceRecords",
                schema: "assets");
        }
    }
}
