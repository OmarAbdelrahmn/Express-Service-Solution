using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VehicleImagePath",
                table: "Vehicles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "LicenseImagePath",
                table: "Vehicles",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "ExstraImage",
                table: "Vehicles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExstraImage1",
                table: "Vehicles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "RiderDetails",
                type: "int",
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleId",
                table: "RiderDetails");

            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_VehicleId",
                table: "RiderDetails");

            migrationBuilder.DropColumn(
                name: "ExstraImage",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ExstraImage1",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "RiderDetails");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleImagePath",
                table: "Vehicles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LicenseImagePath",
                table: "Vehicles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

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
        }
    }
}
