using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingconfigrationtothecompany2validation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Company2ValidationConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    TargetOrdersPerDay = table.Column<int>(type: "int", nullable: false),
                    TargetHoursPerDay = table.Column<float>(type: "real", nullable: false),
                    MinWorkingHoursPerDay = table.Column<float>(type: "real", nullable: false),
                    FullMonthTargetOrders = table.Column<int>(type: "int", nullable: false),
                    FirstCriticalDaysCount = table.Column<int>(type: "int", nullable: false),
                    LastCriticalDaysCount = table.Column<int>(type: "int", nullable: false),
                    MaxStartDayForExistingRiders = table.Column<int>(type: "int", nullable: false),
                    AllowedMissingDays28 = table.Column<int>(type: "int", nullable: false),
                    AllowedMissingDays29 = table.Column<int>(type: "int", nullable: false),
                    AllowedMissingDays30 = table.Column<int>(type: "int", nullable: false),
                    AllowedMissingDays31 = table.Column<int>(type: "int", nullable: false),
                    SundayIsSpecialDay = table.Column<bool>(type: "bit", nullable: false),
                    MondayIsSpecialDay = table.Column<bool>(type: "bit", nullable: false),
                    TuesdayIsSpecialDay = table.Column<bool>(type: "bit", nullable: false),
                    WednesdayIsSpecialDay = table.Column<bool>(type: "bit", nullable: false),
                    ThursdayIsSpecialDay = table.Column<bool>(type: "bit", nullable: false),
                    FridayIsSpecialDay = table.Column<bool>(type: "bit", nullable: false),
                    SaturdayIsSpecialDay = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Company2ValidationConfig", x => x.Id);
                    table.CheckConstraint("CK_Company2ValidationConfig_Singleton", "[Id] = 1");
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAELmnCSkCaI/5jb3fF3w4Goe8xO7NgUGH7ecjrl2OaNBxLy4sbePdnFbiLTt/ZqiT3A==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEB3jmRHbNMLyWbvB/sy29qaT1jjHE8ZGsfRebPS01KpLqRQ8qPihwx8e2cDw7RVE2A==");

            migrationBuilder.InsertData(
                table: "Company2ValidationConfig",
                columns: new[] { "Id", "AllowedMissingDays28", "AllowedMissingDays29", "AllowedMissingDays30", "AllowedMissingDays31", "FirstCriticalDaysCount", "FridayIsSpecialDay", "FullMonthTargetOrders", "LastCriticalDaysCount", "MaxStartDayForExistingRiders", "MinWorkingHoursPerDay", "MondayIsSpecialDay", "SaturdayIsSpecialDay", "SundayIsSpecialDay", "TargetHoursPerDay", "TargetOrdersPerDay", "ThursdayIsSpecialDay", "TuesdayIsSpecialDay", "UpdatedAt", "UpdatedBy", "WednesdayIsSpecialDay" },
                values: new object[] { 1, 3, 3, 4, 5, 3, true, 300, 4, 5, 10f, false, false, false, 10.5f, 12, true, false, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Company2ValidationConfig");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEByGVhWtWLpIO4GsspOCI8qyg9V5M8Ux361fx4uczAnwYOHBfrAR/1720hA+Wbd5ng==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEBfFmavaVC/ckUNAthWTIz1tO2yjt/bL/lvVJQEXzUtrAYa/GIW82Trrtj/LYarTlQ==");
        }
    }
}
