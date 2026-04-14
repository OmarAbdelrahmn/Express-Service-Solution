using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingdeleteinfoandforescapedempoloyees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommercialRegister",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "EscapedEmployeeDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivatedBy",
                table: "EscapedEmployeeDetails",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "EscapedEmployeeDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEF3z7Vvcqep8qLtheniP0vztc6dluo8hJPGAgn8O2lO0Rhb/LYLlF7WlVCCH/Pc+RA==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAELb460ZZWq5ifsJTjoBVleumiG7RgXOlDSZ4w7Ke/PUmf8ssoIH+Mv08udyk5S1xJg==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommercialRegister",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "EscapedEmployeeDetails");

            migrationBuilder.DropColumn(
                name: "DeactivatedBy",
                table: "EscapedEmployeeDetails");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "EscapedEmployeeDetails");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEKElGfXOy2ayztDD4XDRjHRkp2gDx0ER3CTylpykFkovFnTb3VvGneBX413kUwC8Sw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFpS9dK5wQgHXauWDdju2XGAWJds/9ZH/a9r0i6a92tBuInAo04BeA0IhGjMF7pgfQ==");
        }
    }
}
