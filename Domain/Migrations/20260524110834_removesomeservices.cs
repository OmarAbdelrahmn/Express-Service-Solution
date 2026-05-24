using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class removesomeservices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleMaintenanceBaselines");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEKGLiqN82usDF1OOOLranMDzVr2Go7K4Ttde4fPOyUJknIwlWV4VoHpBRSc54D1CmQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMfoYkERX991JbMWBa64iTF9I7lWc6N7mcMQN77xKVfSR9qV/pHRIFZvURWY2by8gQ==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleMaintenanceBaselines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceIntervalId = table.Column<int>(type: "int", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: true),
                    VehicleNumber1 = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastDoneAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SetBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
    }
}
