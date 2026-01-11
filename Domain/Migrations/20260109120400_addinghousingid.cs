using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addinghousingid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HousingId",
                table: "TempRiderShiftComparisons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HousingId",
                table: "RiderShifts",
                type: "int",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_HousingId",
                table: "TempRiderShiftComparisons",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShifts_HousingId",
                table: "RiderShifts",
                column: "HousingId");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderShifts_Housings_HousingId",
                table: "RiderShifts",
                column: "HousingId",
                principalTable: "Housings",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TempRiderShiftComparisons_Housings_HousingId",
                table: "TempRiderShiftComparisons",
                column: "HousingId",
                principalTable: "Housings",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderShifts_Housings_HousingId",
                table: "RiderShifts");

            migrationBuilder.DropForeignKey(
                name: "FK_TempRiderShiftComparisons_Housings_HousingId",
                table: "TempRiderShiftComparisons");

            migrationBuilder.DropIndex(
                name: "IX_TempRiderShiftComparisons_HousingId",
                table: "TempRiderShiftComparisons");

            migrationBuilder.DropIndex(
                name: "IX_RiderShifts_HousingId",
                table: "RiderShifts");

            migrationBuilder.DropColumn(
                name: "HousingId",
                table: "TempRiderShiftComparisons");

            migrationBuilder.DropColumn(
                name: "HousingId",
                table: "RiderShifts");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEDCk9TOffG6qhYqJQfYKnILBM5rOy0+l6uth6ATiiEzVN6NIJazKETQN5UCaENa2Og==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEPgDNLE96V6pJ+gTCpIMGEcnQ7ytiu8MHAZnGIFYFNpZL/0u6emvfdPKhj+nxO1B1A==");
        }
    }
}
