using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalWorkingId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "ShiftDate",
                table: "RiderShiftSubstitutions");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "RiderShiftSubstitutions",
                newName: "StartDate");

            migrationBuilder.RenameColumn(
                name: "DailyOrders",
                table: "RiderShifts",
                newName: "RejectedDailyOrders");

            migrationBuilder.AlterColumn<int>(
                name: "SubstituteWorkingId",
                table: "RiderShiftSubstitutions",
                type: "int",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "ActualRiderId",
                table: "RiderShiftSubstitutions",
                type: "int",
                maxLength: 50,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                table: "RiderShiftSubstitutions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "RiderShiftSubstitutions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AcceptedDailyOrders",
                table: "RiderShifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RealRejectedDailyOrders",
                table: "RiderShifts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "WorkingHours",
                table: "RiderShifts",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAELcF5tt/GrcqtHvksa6sOTPwPx/vbTmTIfh5NBYoM95IC81crL1AgDFK70uVWPnBRg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEN+hNzBionMsNvKJfiqjSK2XmsrcwIemmmK1MX8JIB/OcFUYsyypIbtwVp0+LqKuBg==");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftSubstitutions_ActualRiderId",
                table: "RiderShiftSubstitutions",
                column: "ActualRiderId");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderShiftSubstitutions_RiderDetails_ActualRiderId",
                table: "RiderShiftSubstitutions",
                column: "ActualRiderId",
                principalTable: "RiderDetails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderShiftSubstitutions_RiderDetails_ActualRiderId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropIndex(
                name: "IX_RiderShiftSubstitutions_ActualRiderId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "ActualRiderId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "AcceptedDailyOrders",
                table: "RiderShifts");

            migrationBuilder.DropColumn(
                name: "RealRejectedDailyOrders",
                table: "RiderShifts");

            migrationBuilder.DropColumn(
                name: "WorkingHours",
                table: "RiderShifts");

            migrationBuilder.RenameColumn(
                name: "StartDate",
                table: "RiderShiftSubstitutions",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "RejectedDailyOrders",
                table: "RiderShifts",
                newName: "DailyOrders");

            migrationBuilder.AlterColumn<string>(
                name: "SubstituteWorkingId",
                table: "RiderShiftSubstitutions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "OriginalWorkingId",
                table: "RiderShiftSubstitutions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShiftDate",
                table: "RiderShiftSubstitutions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEPf7PZvC4+7TAiQ9AbE5olaMMFzJW8g4dMB7FEodkVpgCLdV51ArPYqiSTPl4Wpq0w==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMcT5KYCI0lr5Qzr8xOaXIyqqCqS36/wpJoDqi/ZmV39GM9NwokJrLYRHpTJWtZzhg==");
        }
    }
}
