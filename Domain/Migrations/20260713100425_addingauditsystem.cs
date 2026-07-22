using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingauditsystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemType = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    LocationBefore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LocationAfter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    QuantityBefore = table.Column<int>(type: "int", nullable: true),
                    QuantityAfter = table.Column<int>(type: "int", nullable: true),
                    PriceBefore = table.Column<decimal>(type: "decimal(38,0)", nullable: true),
                    PriceAfter = table.Column<decimal>(type: "decimal(38,0)", nullable: true),
                    PerformedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAuditLogs", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAENcN/lSZJq/5fhzAgYxB9lzKj08EKsY5yotYcxjk2MYPCPyA/MzFjvimGAqK3DTNYg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAENFqyKJ/maFmW6gis+E3E/EqS8dpttSs8z3aFucuD/xXD6jyClaftm5kcUQo9EjVDw==");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_ItemType_ItemId",
                table: "InventoryAuditLogs",
                columns: new[] { "ItemType", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_LocationAfter",
                table: "InventoryAuditLogs",
                column: "LocationAfter");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_LocationBefore",
                table: "InventoryAuditLogs",
                column: "LocationBefore");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_PerformedAt",
                table: "InventoryAuditLogs",
                column: "PerformedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAuditLogs");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEA/zZpuqFzbTSnicQa4Tooll0FGxeDLCE2M5TALeSVR6BGE45Era3fs5IhF5zU2ZyQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFpg1iN3qC51jcJrS5Ea9/Ab1Xi7kXnwjCrMOynu6YUpw7q1mrTe8yz+5Cx2W01t5A==");
        }
    }
}
