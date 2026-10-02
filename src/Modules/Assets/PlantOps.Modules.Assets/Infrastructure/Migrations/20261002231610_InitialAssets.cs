using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PlantOps.Modules.Assets.Infrastructure.Migrations
{
    /// <inheritdoc />
    internal partial class InitialAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assets");

            migrationBuilder.CreateTable(
                name: "ProductionLines",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                schema: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Station = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Criticality = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CommissionedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DecommissionedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DecommissionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Tag = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_ProductionLines_LineId",
                        column: x => x.LineId,
                        principalSchema: "assets",
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "assets",
                table: "ProductionLines",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("0197a5c0-0000-7000-8000-000000000001"), "SMT-1", "SMT Line 1" },
                    { new Guid("0197a5c0-0000-7000-8000-000000000002"), "SMT-2", "SMT Line 2" },
                    { new Guid("0197a5c0-0000-7000-8000-000000000003"), "FA-1", "Final Assembly 1" },
                    { new Guid("0197a5c0-0000-7000-8000-000000000004"), "TEST-1", "Functional Test 1" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_LineId",
                schema: "assets",
                table: "Assets",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "UX_Assets_Tag",
                schema: "assets",
                table: "Assets",
                column: "Tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductionLines_Code",
                schema: "assets",
                table: "ProductionLines",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assets",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "ProductionLines",
                schema: "assets");
        }
    }
}
