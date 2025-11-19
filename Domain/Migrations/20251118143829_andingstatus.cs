using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class andingstatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleId",
                table: "RiderDetails");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Vehicles",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_VehicleId",
                table: "RiderDetails");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "RiderDetails");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "Vehicles",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumber",
                table: "RiderDetails",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumber1",
                table: "RiderDetails",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Vehicles",
                table: "Vehicles",
                column: "VehicleNumber");

            migrationBuilder.CreateTable(
                name: "RiderVehicleStatus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<int>(type: "int", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VehicleNumber1 = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StatusType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderVehicleStatus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderVehicleStatus_Vehicles_VehicleNumber1",
                        column: x => x.VehicleNumber1,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEHRZj8Pt+x3m9Ywsb+scsHhqyoeEgq4FY0Y3Z2pTWmBPoAPivikpY8TaEUQaERStaA==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAED/Qvs0qyM3Z2ZEJbGUqyqfsA5+gxlLr/YOnjCOtoXikId30HKvtzAlAq++kjyrQ4A==");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_VehicleNumber",
                table: "Vehicles",
                column: "VehicleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_VehicleNumber1",
                table: "RiderDetails",
                column: "VehicleNumber1");

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber_IsActive",
                table: "RiderVehicleStatus",
                columns: new[] { "VehicleNumber", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber1",
                table: "RiderVehicleStatus",
                column: "VehicleNumber1");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleNumber1",
                table: "RiderDetails",
                column: "VehicleNumber1",
                principalTable: "Vehicles",
                principalColumn: "VehicleNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleNumber1",
                table: "RiderDetails");

            migrationBuilder.DropTable(
                name: "RiderVehicleStatus");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Vehicles",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_VehicleNumber",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_VehicleNumber1",
                table: "RiderDetails");

            migrationBuilder.DropColumn(
                name: "VehicleNumber",
                table: "RiderDetails");

            migrationBuilder.DropColumn(
                name: "VehicleNumber1",
                table: "RiderDetails");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "Vehicles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "Vehicles",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "RiderDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Vehicles",
                table: "Vehicles",
                column: "Id");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAECmF8qLhKtwh1DQG5i1n2Pn+NxsPMNBG6RhvZCQT0TbL/SWifSD/N8IpxrMx/SL6UQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEImi/BE6VmV6vslmrLbTPMhrljdCG5qZwy1ohilUqkJobOiVfX3gCdvuQGzx1E1ZUQ==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_VehicleId",
                table: "RiderDetails",
                column: "VehicleId",
                unique: true,
                filter: "[VehicleId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleId",
                table: "RiderDetails",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id");
        }
    }
}
