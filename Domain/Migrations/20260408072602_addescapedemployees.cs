using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addescapedemployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EscapedEmployeeDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    EscapedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    ActivePath = table.Column<int>(type: "int", nullable: false),
                    IsReported = table.Column<bool>(type: "bit", nullable: true),
                    ReportedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsOutage = table.Column<bool>(type: "bit", nullable: true),
                    DateOfOutage = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OutageVisaNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RemovalDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenDayNotificationSent = table.Column<bool>(type: "bit", nullable: false),
                    TenDayNotificationSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EscapedEmployeeDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EscapedEmployeeDetails_Employees_EmployeeIqamaNo",
                        column: x => x.EmployeeIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Restrict);
                });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EscapedEmployeeDetails");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFcacLTItrcca7RC1656U+paEcuElHJHQKFVoVyKbCYFjNQmxsO1m/c8zOlQSp/Ziw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMwIK4NrIYuotGD+RHttb6QfVoUX2ItVMEauyQZqVkGmsVfZQLLlv6J0o50RwUtCJg==");
        }
    }
}
