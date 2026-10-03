using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantOps.Modules.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    internal partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "inventory",
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
                name: "InboxMessages",
                schema: "inventory",
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
                name: "SpareParts",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BinLocation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    QuantityOnHand = table.Column<int>(type: "int", nullable: false),
                    QuantityReserved = table.Column<int>(type: "int", nullable: false),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpareParts", x => x.Id);
                    table.CheckConstraint("CK_SpareParts_Quantities", "[QuantityReserved] >= 0 AND [QuantityReserved] <= [QuantityOnHand] AND [ReorderLevel] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SparePartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReservedById = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReservedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_Reservations_SpareParts_SparePartId",
                        column: x => x.SparePartId,
                        principalSchema: "inventory",
                        principalTable: "SpareParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_AggregateId_OccurredAt",
                schema: "inventory",
                table: "AuditEntries",
                columns: new[] { "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_WorkOrderId",
                schema: "inventory",
                table: "Reservations",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_Active_PartId_WorkOrderId",
                schema: "inventory",
                table: "Reservations",
                columns: new[] { "SparePartId", "WorkOrderId" },
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_SpareParts_PartNumber",
                schema: "inventory",
                table: "SpareParts",
                column: "PartNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "SpareParts",
                schema: "inventory");
        }
    }
}
