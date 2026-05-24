using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addreminderservices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceIntervals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SparePartId = table.Column<int>(type: "int", nullable: true),
                    AccessoryId = table.Column<int>(type: "int", nullable: true),
                    ItemType = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: false),
                    AlertDaysBeforeDue = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceIntervals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceIntervals_RiderAccessories_AccessoryId",
                        column: x => x.AccessoryId,
                        principalTable: "RiderAccessories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MaintenanceIntervals_SpareParts_SparePartId",
                        column: x => x.SparePartId,
                        principalTable: "SpareParts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VehicleMaintenanceBaselines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceIntervalId = table.Column<int>(type: "int", nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VehicleNumber1 = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RiderId = table.Column<int>(type: "int", nullable: true),
                    LastDoneAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SetBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleMaintenanceBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleMaintenanceBaselines_MaintenanceIntervals_MaintenanceIntervalId",
                        column: x => x.MaintenanceIntervalId,
                        principalTable: "MaintenanceIntervals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleMaintenanceBaselines_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VehicleMaintenanceBaselines_Vehicles_VehicleNumber1",
                        column: x => x.VehicleNumber1,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber");
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMQsWKKca1mfmG+yYxY8u1AH5DftUjZM+1WoGnOYzhoYkGoLFnlblXKPRbtCnAMACQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOEiIgsnuy5q2D4yKxazNQh2cbJa0Uyn6b4CRUDL8AdlDi5Ul7/soFjxpZGQJB47Lw==");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceIntervals_AccessoryId",
                table: "MaintenanceIntervals",
                column: "AccessoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceIntervals_SparePartId",
                table: "MaintenanceIntervals",
                column: "SparePartId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMaintenanceBaselines_MaintenanceIntervalId",
                table: "VehicleMaintenanceBaselines",
                column: "MaintenanceIntervalId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMaintenanceBaselines_RiderId",
                table: "VehicleMaintenanceBaselines",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMaintenanceBaselines_VehicleNumber1",
                table: "VehicleMaintenanceBaselines",
                column: "VehicleNumber1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleMaintenanceBaselines");

            migrationBuilder.DropTable(
                name: "MaintenanceIntervals");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFmRY8nGMYfeRKBVxgSGDjAMl5WHh+SsPo79a3jDVjYzuec6lLWLtwm4wP64HBiUdg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEAs0d//TnO5aTvAcpovNcfqPWb7s1o3SlJ9aY4MDy6WSRiNYn8EFRXNJDRQ69j5ldw==");
        }
    }
}
