using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NewStackedDeliveries",
                table: "TempRiderShiftComparisons",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OldStackedDeliveries",
                table: "TempRiderShiftComparisons",
                type: "int",
                nullable: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewStackedDeliveries",
                table: "TempRiderShiftComparisons");

            migrationBuilder.DropColumn(
                name: "OldStackedDeliveries",
                table: "TempRiderShiftComparisons");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEE1Svo010H2My2ObQElZ71uryViux9OvvMs3GNnIg2KCXFja+asnNi+mt67NhzfvLg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEGi49Ui+u+23pKW8dUdv3UQKa0prn/DJX/nxMDPDcNlVjVvIRj1aQsfxEBA6KezHIg==");
        }
    }
}
