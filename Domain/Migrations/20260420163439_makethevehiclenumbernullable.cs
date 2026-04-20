using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class makethevehiclenumbernullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "RiderPetrolCosts",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFphRG1ywy+j//Rzucs5s1aq6yh/GIbSraBaK87AtKBQM9io4n8DJAHsAdSHjYlN2A==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEG2min07j6OqTY/jPvm87gWdsRiENEmGmBHb3sywVw+VYnoIONVZ6BkHw+h2CZDisA==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "RiderPetrolCosts",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEPeD0sSnR4xw5l0PsCwbMvOvh2TowsN5JqzilmB5aBYU3E/zoD/YlI9I2hbxZb2c4Q==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMtJW7rshLPErtY5ZGSGzM0TARv2PQnApx27T8lFXkdMRipLsocHp2uNV//xqlg3/Q==");
        }
    }
}
