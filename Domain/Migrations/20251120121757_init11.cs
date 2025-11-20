using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeIqamaNo1",
                table: "EmployeeDocuments");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDocuments_EmployeeIqamaNo1",
                table: "EmployeeDocuments");

            migrationBuilder.DropColumn(
                name: "EmployeeIqamaNo1",
                table: "EmployeeDocuments");

            migrationBuilder.AlterColumn<int>(
                name: "EmployeeIqamaNo",
                table: "EmployeeDocuments",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEN2WRgZyzYWwltNRVbQ/8W14MCXCzPv/Uw0xy/3mr1RnWltCzLsh4T48XoBKFolkww==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOWyQUamW2g9kRI9OgUo70BLpywR80ImerfPczsxcLdtDcRU2aRo3SiS++pGcoXocQ==");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeIqamaNo",
                table: "EmployeeDocuments",
                column: "EmployeeIqamaNo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeIqamaNo",
                table: "EmployeeDocuments",
                column: "EmployeeIqamaNo",
                principalTable: "Employees",
                principalColumn: "IqamaNo",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeIqamaNo",
                table: "EmployeeDocuments");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDocuments_EmployeeIqamaNo",
                table: "EmployeeDocuments");

            migrationBuilder.AlterColumn<long>(
                name: "EmployeeIqamaNo",
                table: "EmployeeDocuments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "EmployeeIqamaNo1",
                table: "EmployeeDocuments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEB/UxbSYw3nkRJorwelMjtqo1W0wFd1gTXCArtFnQMzGDT6vSL8LN+qfRPivHDysYQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEHRUyqsCHvg3YmvdeAykcq7oTlx7Bp/22qkPGk0QK2WNmTyTA9o3P2b0gwx11wRsiA==");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDocuments_EmployeeIqamaNo1",
                table: "EmployeeDocuments",
                column: "EmployeeIqamaNo1");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDocuments_Employees_EmployeeIqamaNo1",
                table: "EmployeeDocuments",
                column: "EmployeeIqamaNo1",
                principalTable: "Employees",
                principalColumn: "IqamaNo",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
