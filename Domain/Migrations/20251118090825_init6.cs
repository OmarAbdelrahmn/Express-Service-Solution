using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ManagerId",
                table: "Housings",
                type: "int",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAECmF8qLhKtwh1DQG5i1n2Pn+NxsPMNBG6RhvZCQT0TbL/SWifSD/N8IpxrMx/SL6UQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEImi/BE6VmV6vslmrLbTPMhrljdCG5qZwy1ohilUqkJobOiVfX3gCdvuQGzx1E1ZUQ==");

            migrationBuilder.CreateIndex(
                name: "IX_Housings_Name",
                table: "Housings",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Housings_Name",
                table: "Housings");

            migrationBuilder.AlterColumn<string>(
                name: "ManagerId",
                table: "Housings",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMp8pDiTZsaZYOQb4Z+M0Xacs73zd/QHIyN8ofnOuY/QifKnyZQy47yH5k0KHaTCQQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEJ2Oe/XvBiyJTThQPyUWJ5imEa/qVZTwod4rrBixTT3gQTAyC36QgRiYQvyUQZKlmQ==");
        }
    }
}
