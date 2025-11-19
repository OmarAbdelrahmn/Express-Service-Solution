using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleNumber1",
                table: "RiderDetails");

            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_VehicleNumber1",
                table: "RiderDetails");

            migrationBuilder.DropColumn(
                name: "VehicleNumber1",
                table: "RiderDetails");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "RiderDetails",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEGZNKJiY2AK+k76lhXzBW7fK7QwO6MMSxyzhhxDTYGyI4M1/Mqp8/wQi72RwCLMubw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEJ9TVG9GNCJYcdRSRn2mknBkjn9LEl2MaxSvi4j17h+owiKshc6I2Nk4f8jPqH/brA==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_VehicleNumber",
                table: "RiderDetails",
                column: "VehicleNumber",
                unique: true,
                filter: "[VehicleNumber] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleNumber",
                table: "RiderDetails",
                column: "VehicleNumber",
                principalTable: "Vehicles",
                principalColumn: "VehicleNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleNumber",
                table: "RiderDetails");

            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_VehicleNumber",
                table: "RiderDetails");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "RiderDetails",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumber1",
                table: "RiderDetails",
                type: "nvarchar(450)",
                nullable: true);

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
                name: "IX_RiderDetails_VehicleNumber1",
                table: "RiderDetails",
                column: "VehicleNumber1");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderDetails_Vehicles_VehicleNumber1",
                table: "RiderDetails",
                column: "VehicleNumber1",
                principalTable: "Vehicles",
                principalColumn: "VehicleNumber");
        }
    }
}
