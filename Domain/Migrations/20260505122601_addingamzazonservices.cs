using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingamzazonservices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<bool>(type: "bit", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeOrders_Employees_EmployeeIqamaNo",
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
                value: "AQAAAAIAAYagAAAAEHyQLC5suXXY+qRTGw6ocR7iPRjEvZtbTCVQcY5TL+GbdNKvnoxXQOn5aBCYmbA4VQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEITTX706FQdP+eEkh30Ab/w95CqhCTxFVa3d7y0Qmeef+cDZi695iUr26fJrj3EP3w==");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOrders_CompanyId",
                table: "EmployeeOrders",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOrders_EmployeeIqamaNo",
                table: "EmployeeOrders",
                column: "EmployeeIqamaNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeOrders");

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
    }
}
