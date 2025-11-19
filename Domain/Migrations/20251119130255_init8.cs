using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderVehicleStatus_Vehicles_VehicleNumber1",
                table: "RiderVehicleStatus");

            migrationBuilder.DropIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber1",
                table: "RiderVehicleStatus");

            migrationBuilder.DropColumn(
                name: "VehicleNumber1",
                table: "RiderVehicleStatus");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEPf7PZvC4+7TAiQ9AbE5olaMMFzJW8g4dMB7FEodkVpgCLdV51ArPYqiSTPl4Wpq0w==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMcT5KYCI0lr5Qzr8xOaXIyqqCqS36/wpJoDqi/ZmV39GM9NwokJrLYRHpTJWtZzhg==");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderVehicleStatus_Vehicles_VehicleNumber",
                table: "RiderVehicleStatus",
                column: "VehicleNumber",
                principalTable: "Vehicles",
                principalColumn: "VehicleNumber",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderVehicleStatus_Vehicles_VehicleNumber",
                table: "RiderVehicleStatus");

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumber1",
                table: "RiderVehicleStatus",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEC+4iDVRgG/0kjcGytl7hV5M1opMRkfLJFHge8xGaGfsvQJ35SumHdnrEjG4WbO5Gw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEM+Hx9kmSk6NZRjBQpkLwmMuoSrkp68xG4ZNXPStWLaP2n+gxJketZCsxEA8L6Lv+Q==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber1",
                table: "RiderVehicleStatus",
                column: "VehicleNumber1");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderVehicleStatus_Vehicles_VehicleNumber1",
                table: "RiderVehicleStatus",
                column: "VehicleNumber1",
                principalTable: "Vehicles",
                principalColumn: "VehicleNumber",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
