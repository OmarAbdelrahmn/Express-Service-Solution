using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addinsomeindexs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WorkingId",
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
                value: "AQAAAAIAAYagAAAAEEpkN3xLNPSZQbdZhy/d5LoHJPJ/VJIrEAQGVlDCrEIEuIU4/yQdr5QA4fKPjP5DSg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEKrz80iyKajJGbs7aIbFfm1OU+dmP9ZVO54zL0KpApKMhRkXS98g2XG8ZnqaSGKYQA==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_EmployeeIqamaNo",
                table: "RiderDetails",
                column: "EmployeeIqamaNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderDetails_WorkingId",
                table: "RiderDetails",
                column: "WorkingId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NameAR",
                table: "Employees",
                column: "NameAR");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NameEN",
                table: "Employees",
                column: "NameEN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_EmployeeIqamaNo",
                table: "RiderDetails");

            migrationBuilder.DropIndex(
                name: "IX_RiderDetails_WorkingId",
                table: "RiderDetails");

            migrationBuilder.DropIndex(
                name: "IX_Employees_NameAR",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_NameEN",
                table: "Employees");

            migrationBuilder.AlterColumn<string>(
                name: "WorkingId",
                table: "RiderDetails",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEHwjw7516nii6nqALDcU/PfEUUqIsfvkGi8LUNqdZoCQ/ce9QssyRJh8pn4MH9fOmg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEF3kd/9+QyWxvK9WBeo4P/NY5f1Mc5t6R9Hv/up1RVTozW3Z0tV+bD7+RY0m+ngHAw==");
        }
    }
}
