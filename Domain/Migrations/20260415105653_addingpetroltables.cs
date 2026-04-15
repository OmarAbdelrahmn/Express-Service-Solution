using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingpetroltables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehiclePetrolCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlateNumberE = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(38,0)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsAttributed = table.Column<bool>(type: "bit", nullable: false),
                    HasResolutionError = table.Column<bool>(type: "bit", nullable: false),
                    ResolutionErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehiclePetrolCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehiclePetrolCosts_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPetrolCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehiclePetrolCostId = table.Column<int>(type: "int", nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(38,0)", nullable: false),
                    AttributionSource = table.Column<int>(type: "int", nullable: false),
                    ResolvedFromStatusId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPetrolCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderPetrolCosts_Employees_RiderIqamaNo",
                        column: x => x.RiderIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPetrolCosts_VehiclePetrolCosts_VehiclePetrolCostId",
                        column: x => x.VehiclePetrolCostId,
                        principalTable: "VehiclePetrolCosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPetrolCosts_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEEIFoPDY41HbDRx682oDMWkbxyZbhJWE63xaMyICycxvyCYFrkonwbndxU5RuERzbg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEO2JMckPuAPauocoa0/cqjmefKPtlUCCQ5lRfKBWjtJIK/ERbpSfVYshuM8gWNdVXQ==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPetrolCosts_RiderIqamaNo_AttributionSource",
                table: "RiderPetrolCosts",
                columns: new[] { "RiderIqamaNo", "AttributionSource" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderPetrolCosts_RiderIqamaNo_Date",
                table: "RiderPetrolCosts",
                columns: new[] { "RiderIqamaNo", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderPetrolCosts_VehicleNumber_Date",
                table: "RiderPetrolCosts",
                columns: new[] { "VehicleNumber", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderPetrolCosts_VehiclePetrolCostId",
                table: "RiderPetrolCosts",
                column: "VehiclePetrolCostId");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePetrolCosts_IsAttributed",
                table: "VehiclePetrolCosts",
                column: "IsAttributed");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePetrolCosts_VehicleNumber_Date",
                table: "VehiclePetrolCosts",
                columns: new[] { "VehicleNumber", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiderPetrolCosts");

            migrationBuilder.DropTable(
                name: "VehiclePetrolCosts");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEF3z7Vvcqep8qLtheniP0vztc6dluo8hJPGAgn8O2lO0Rhb/LYLlF7WlVCCH/Pc+RA==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAELb460ZZWq5ifsJTjoBVleumiG7RgXOlDSZ4w7Ke/PUmf8ssoIH+Mv08udyk5S1xJg==");
        }
    }
}
