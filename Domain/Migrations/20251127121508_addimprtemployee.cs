using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addimprtemployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_EmployeeIqamaNo",
                table: "RiderDetails");

            migrationBuilder.AddColumn<int>(
                name: "SponsorNo",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_RiderDetails_EmployeeIqamaNo",
                table: "RiderDetails",
                column: "EmployeeIqamaNo");

            migrationBuilder.CreateTable(
                name: "TempEmployeeStatusChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempEmployeeStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempEmployeeStatusChanges_Employees_EmployeeIqamaNo",
                        column: x => x.EmployeeIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TempEmployeeUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IqamaNo = table.Column<int>(type: "int", nullable: false),
                    OldIqamaEndM = table.Column<DateOnly>(type: "date", nullable: true),
                    OldIqamaEndH = table.Column<DateOnly>(type: "date", nullable: true),
                    OldPassportNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldPassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    OldSponsor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldSponsorNo = table.Column<int>(type: "int", nullable: true),
                    OldJobTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldNameAR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldNameEN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldDateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OldStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldIBAN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldINKSA = table.Column<bool>(type: "bit", nullable: true),
                    NewIqamaEndM = table.Column<DateOnly>(type: "date", nullable: true),
                    NewIqamaEndH = table.Column<DateOnly>(type: "date", nullable: true),
                    NewPassportNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewPassportEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    NewSponsor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewSponsorNo = table.Column<int>(type: "int", nullable: true),
                    NewJobTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewNameAR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewNameEN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewCountry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewDateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewIBAN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewINKSA = table.Column<bool>(type: "bit", nullable: true),
                    IsNewEmployee = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempEmployeeUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempEmployeeUpdates_Employees_IqamaNo",
                        column: x => x.IqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo");
                });

            migrationBuilder.CreateTable(
                name: "TempVehicleOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderIqamaNo = table.Column<int>(type: "int", nullable: false),
                    VehiclePlateNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VehicleStatusType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempVehicleOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempVehicleOperations_RiderDetails_RiderIqamaNo",
                        column: x => x.RiderIqamaNo,
                        principalTable: "RiderDetails",
                        principalColumn: "EmployeeIqamaNo",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TempVehicleOperations_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEC7A2b7HQlB/gbuoUXBKaSJx7xfFB7TgbSMASGavFKZc5RyKSyQuw8bYg7JtUclUvg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFrr7bwcQbmAtj0zFSy9egbDE01mXKoHRxTr/xfw6d1xJvVqXn+NB+NWqFp03Syfog==");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeStatusChanges_EmployeeIqamaNo",
                table: "TempEmployeeStatusChanges",
                column: "EmployeeIqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeStatusChanges_IsResolved",
                table: "TempEmployeeStatusChanges",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeStatusChanges_RequestedAt",
                table: "TempEmployeeStatusChanges",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeUpdates_IqamaNo",
                table: "TempEmployeeUpdates",
                column: "IqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeUpdates_IsResolved",
                table: "TempEmployeeUpdates",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempEmployeeUpdates_UploadedAt",
                table: "TempEmployeeUpdates",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_IsResolved",
                table: "TempVehicleOperations",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_RequestedAt",
                table: "TempVehicleOperations",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_RiderIqamaNo",
                table: "TempVehicleOperations",
                column: "RiderIqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_VehicleNumber",
                table: "TempVehicleOperations",
                column: "VehicleNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TempEmployeeStatusChanges");

            migrationBuilder.DropTable(
                name: "TempEmployeeUpdates");

            migrationBuilder.DropTable(
                name: "TempVehicleOperations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_RiderDetails_EmployeeIqamaNo",
                table: "RiderDetails");

            migrationBuilder.DropColumn(
                name: "SponsorNo",
                table: "Employees");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAECpXtZQKdeAzUt/IC51SQm45HLGJJUfMyWa/5HQhzkNJJH5DRMX6GZtOu/tWEGSeyg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAENdZVQ7YX5I9sfOKG/a3DKOyWUeV7ueDkR6dYVvlzZ6vc+gic1KSkJmaxPuZYk4ToQ==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_EmployeeIqamaNo",
                table: "RiderDetails",
                column: "EmployeeIqamaNo",
                unique: true);
        }
    }
}
