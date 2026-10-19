using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleDocumentExpiryDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "LicenseExpiryDate",
                table: "Vehicles",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<DateOnly>(
                name: "InspectionExpiryDate",
                table: "Vehicles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InsuranceExpiryDate",
                table: "Vehicles",
                type: "date",
                nullable: true);

            // Legacy empty dates represent missing documentation, not an expired registration.
            migrationBuilder.Sql("UPDATE [Vehicles] SET [LicenseExpiryDate] = NULL WHERE [LicenseExpiryDate] = '00010101';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InspectionExpiryDate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "InsuranceExpiryDate",
                table: "Vehicles");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "LicenseExpiryDate",
                table: "Vehicles",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
