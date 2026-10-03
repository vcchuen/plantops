using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantOps.Modules.WorkOrders.Infrastructure.Migrations
{
    /// <inheritdoc />
    internal partial class AddSlaEscalationAndPm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EscalatedAt",
                schema: "workorders",
                table: "WorkOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PmDueOn",
                schema: "workorders",
                table: "WorkOrders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PmScheduleId",
                schema: "workorders",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "workorders",
                table: "WorkOrders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Reactive");

            migrationBuilder.CreateTable(
                name: "PmSchedules",
                schema: "workorders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetTag = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: false),
                    LeadDays = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    NextDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_DueAt_NotEscalated",
                schema: "workorders",
                table: "WorkOrders",
                column: "DueAt",
                filter: "[EscalatedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_WorkOrders_PmSchedule_DueOn",
                schema: "workorders",
                table: "WorkOrders",
                columns: new[] { "PmScheduleId", "PmDueOn" },
                unique: true,
                filter: "[PmScheduleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PmSchedules_AssetId",
                schema: "workorders",
                table: "PmSchedules",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PmSchedules_IsActive_NextDueOn",
                schema: "workorders",
                table: "PmSchedules",
                columns: new[] { "IsActive", "NextDueOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PmSchedules",
                schema: "workorders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_DueAt_NotEscalated",
                schema: "workorders",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "UX_WorkOrders_PmSchedule_DueOn",
                schema: "workorders",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                schema: "workorders",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PmDueOn",
                schema: "workorders",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PmScheduleId",
                schema: "workorders",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "workorders",
                table: "WorkOrders");
        }
    }
}
