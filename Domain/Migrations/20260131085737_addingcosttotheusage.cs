using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingcosttotheusage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Cost",
                table: "SparePartUsages",
                type: "decimal(38,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Cost",
                table: "RiderAccessoryUsages",
                type: "decimal(38,0)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOVnFyBlwxWowJvLRJNSSFGe5PXvUn58rcSsylpKOHlDdBIu0RZ4IY+3sgah+BFJyA==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFT0uIsY8qaMO+29yjIM6jqeGw961uFM5/WfY5Bi/LlKScl1hYfsCZUYwtHgzXeakg==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cost",
                table: "SparePartUsages");

            migrationBuilder.DropColumn(
                name: "Cost",
                table: "RiderAccessoryUsages");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEEn9DhaJHmc5T/RW6xewfiIT7NeTQh2kqbCol4AB0Zufy5JuwpBe9re4WnTZLHDAeg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEEWXke63Hix56m3mERHiTSpVa4sNT8l7fVJX5/ffiqtYP+Q8IR7ZrSTn+cplLRKyZA==");
        }
    }
}
