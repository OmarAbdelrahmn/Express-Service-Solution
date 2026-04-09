using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingupdatedattoemployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EscapedEmployeeDetails_EmployeeIqamaNo",
                table: "EscapedEmployeeDetails");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Employees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEKsFIretIX6QKFrgMB27GBFARBhK4OxtOLzvqHaI0guCe5N78ihbfbXYGe2uig2EUw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOTcsCOs75bXyb9izxcow+8dUa0gCN0gXV2V/j9GrwxkIJHQRp6z4hMe3/qOABEkhQ==");

            migrationBuilder.CreateIndex(
                name: "IX_EscapedEmployeeDetails_EmployeeIqamaNo",
                table: "EscapedEmployeeDetails",
                column: "EmployeeIqamaNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EscapedEmployeeDetails_EmployeeIqamaNo",
                table: "EscapedEmployeeDetails");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Employees");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEBMlyfWrjWKLVmQ7i+vMOEYOmaabHH0M87C8BU2RoFClLLqysZxLCwMrzHsITEj8Pg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEBMCwXZc26EhUH+eVoMxk61aHfmKxHlueu6Xa4wEh1zE/SAGy4tqb+iGZDWS4gT7Cg==");

            migrationBuilder.CreateIndex(
                name: "IX_EscapedEmployeeDetails_EmployeeIqamaNo",
                table: "EscapedEmployeeDetails",
                column: "EmployeeIqamaNo");
        }
    }
}
