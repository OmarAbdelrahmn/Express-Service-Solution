using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    From = table.Column<DateOnly>(type: "date", nullable: true),
                    To = table.Column<DateOnly>(type: "date", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Housings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    ManagerId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Housings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiderShiftSubstitutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalWorkingId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubstituteWorkingId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderShiftSubstitutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LicenseNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LicenseExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    VehicleImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LicenseImagePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    IqamaNo = table.Column<int>(type: "int", nullable: false),
                    IqamaEndM = table.Column<DateOnly>(type: "date", nullable: false),
                    IqamaEndH = table.Column<DateOnly>(type: "date", nullable: false),
                    PassportNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Sponsor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    NameAR = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    IBAN = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    INKSA = table.Column<bool>(type: "bit", nullable: false),
                    HousingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.IqamaNo);
                    table.ForeignKey(
                        name: "FK_Employees_Housings_HousingId",
                        column: x => x.HousingId,
                        principalTable: "Housings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    ProfileImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PassportImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IqamaImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkPermitImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionImage3 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmployeeIqamaNo1 = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeDocuments_Employees_EmployeeIqamaNo1",
                        column: x => x.EmployeeIqamaNo1,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkingId = table.Column<int>(type: "int", nullable: true),
                    EmployeeIqamaNo = table.Column<int>(type: "int", nullable: false),
                    TshirtSize = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderDetails_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderDetails_Employees_EmployeeIqamaNo",
                        column: x => x.EmployeeIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderShifts",
                columns: table => new
                {
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    WorkingId = table.Column<int>(type: "int", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyOrders = table.Column<int>(type: "int", nullable: false),
                    ShiftStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderShifts", x => new { x.RiderId, x.ShiftDate, x.WorkingId });
                    table.ForeignKey(
                        name: "FK_RiderShifts_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEL3XVXx/d34jls+27INeh5Zn1Cn+vhUoyGBYC9lqB4YlqJwbWi6JKSC0KxoaLW4D2g==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEBrvq2n/ZcPiM1B/RIYhz3V1wjqKb5bKo+nQh5vkDL19Zl/xbfkd9dChdju2qOR3Lw==");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeIqamaNo1",
                table: "EmployeeDocuments",
                column: "EmployeeIqamaNo1");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_HousingId",
                table: "Employees",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Status",
                table: "Employees",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_CompanyId",
                table: "RiderDetails",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_EmployeeIqamaNo",
                table: "RiderDetails",
                column: "EmployeeIqamaNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeDocuments");

            migrationBuilder.DropTable(
                name: "RiderShifts");

            migrationBuilder.DropTable(
                name: "RiderShiftSubstitutions");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "RiderDetails");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Housings");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEKdiI5dLEgLoUFA+sEzMbg2p0LVF0e5/ShLlDScD6/ipfu0pbFycBSssgmidFA208Q==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEP5DPVcrkJSTS68Hw5De48nZNgGx4mwtwbHtvkxNRnOufMcd/gBw29edchaAjIfmQw==");
        }
    }
}
