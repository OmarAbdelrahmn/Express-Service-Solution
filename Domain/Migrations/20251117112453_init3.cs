using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ShiftStatus",
                table: "RiderShifts",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateTable(
                name: "DeletedEmployees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IqamaNo = table.Column<int>(type: "int", nullable: false),
                    IqamaEndM = table.Column<DateOnly>(type: "date", nullable: false),
                    IqamaEndH = table.Column<DateOnly>(type: "date", nullable: false),
                    PassportNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Sponsor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameAR = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEN = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IBAN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    INKSA = table.Column<bool>(type: "bit", nullable: false),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    WorkingId = table.Column<int>(type: "int", nullable: true),
                    TshirtSize = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedEmployees", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOr3mQSwzu127oiq23hwtH2KXYVIPwLz2BdL3c/sichT9DMw+TnmHPNjGaedPmWjOA==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEHkkk1Yy6dM8b8vxWJ1jKmLDiZTd4v3N0SC2ypR5TyvYLsHdCXWhy5E3GYL8x+XpZA==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_RiderId",
                table: "RiderShifts",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_ShiftDate",
                table: "RiderShifts",
                column: "ShiftDate");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_ShiftStatus",
                table: "RiderShifts",
                column: "ShiftStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_WorkingId",
                table: "RiderShifts",
                column: "WorkingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedEmployees");

            migrationBuilder.DropIndex(
                name: "IX_RiderShifts_RiderId",
                table: "RiderShifts");

            migrationBuilder.DropIndex(
                name: "IX_RiderShifts_ShiftDate",
                table: "RiderShifts");

            migrationBuilder.DropIndex(
                name: "IX_RiderShifts_ShiftStatus",
                table: "RiderShifts");

            migrationBuilder.DropIndex(
                name: "IX_RiderShifts_WorkingId",
                table: "RiderShifts");

            migrationBuilder.AlterColumn<string>(
                name: "ShiftStatus",
                table: "RiderShifts",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEP1zb8q9gOTg4rDdPDodsZ++2R++dPLe7+LBG05CTFUp4WHtHAlCmU+qaX/SyX8bJg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEBpEKEP1p5Qp9OPDcVylw/Nzm/6BEcwLYlw2qPhQfigq5dJmDnkYAdGnCjfBVjrjbA==");
        }
    }
}
