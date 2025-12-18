using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    public partial class updatetrackingworkingid : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "EndDate",
                table: "RiderShiftSubstitutions",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<string>(
                name: "ActualRiderWorkingId",
                table: "RiderShiftSubstitutions",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<long>(
                name: "OriginalRiderIqamaNo",
                table: "RiderShiftSubstitutions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DateOfBirth",
                table: "Employees",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.CreateTable(
                name: "RiderWorkingIdHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    WorkingId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderWorkingIdHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderWorkingIdHistories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderWorkingIdHistories_Employees_RiderIqamaNo",
                        column: x => x.RiderIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAECYZuxEsw1admP9p4OzkIj/DliARu/GRYnbk11SooOHJ/Xozd94JUY7t8zb4kvU/uw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEI9cGFJjCH909Ivk0Pmm7bOOwkCJ5KWVdWJduCq0E7kAT7KdqQ2oXJZgIIOjwHJA+w==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftSubstitutions_ActualRiderWorkingId_IsActive",
                table: "RiderShiftSubstitutions",
                columns: new[] { "ActualRiderWorkingId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftSubstitutions_SubstituteWorkingId_IsActive",
                table: "RiderShiftSubstitutions",
                columns: new[] { "SubstituteWorkingId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderWorkingIdHistories_CompanyId",
                table: "RiderWorkingIdHistories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderWorkingIdHistories_RiderIqamaNo",
                table: "RiderWorkingIdHistories",
                column: "RiderIqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_RiderWorkingIdHistories_RiderIqamaNo_IsActive",
                table: "RiderWorkingIdHistories",
                columns: new[] { "RiderIqamaNo", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderWorkingIdHistories_WorkingId",
                table: "RiderWorkingIdHistories",
                column: "WorkingId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderWorkingIdHistories_WorkingId_IsActive",
                table: "RiderWorkingIdHistories",
                columns: new[] { "WorkingId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiderWorkingIdHistories");

            migrationBuilder.DropIndex(
                name: "IX_RiderShiftSubstitutions_ActualRiderWorkingId_IsActive",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropIndex(
                name: "IX_RiderShiftSubstitutions_SubstituteWorkingId_IsActive",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "OriginalRiderIqamaNo",
                table: "RiderShiftSubstitutions");

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndDate",
                table: "RiderShiftSubstitutions",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "ActualRiderWorkingId",
                table: "RiderShiftSubstitutions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DateOfBirth",
                table: "Employees",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEGbQN3/VJdLUsr4jPzTC3QUAUeljrDYqewJupBrFtYfm96Edgd3Dj3mq5Mbybhg90g==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFsjSMsBbxbuNj97h5MJGBRcK7aYjw6DYKbof9Cgy/1QSUGn/Lp/f5kHOcxFKZgSRw==");
        }
    }
}
