using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePetrolDailyUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehiclePetrolCosts_VehicleNumber_Date",
                table: "VehiclePetrolCosts");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePetrolCosts_PlateNumberE_Date",
                table: "VehiclePetrolCosts",
                columns: new[] { "PlateNumberE", "Date" },
                unique: true,
                filter: "[VehicleNumber] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePetrolCosts_VehicleNumber_Date",
                table: "VehiclePetrolCosts",
                columns: new[] { "VehicleNumber", "Date" },
                unique: true,
                filter: "[VehicleNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehiclePetrolCosts_PlateNumberE_Date",
                table: "VehiclePetrolCosts");

            migrationBuilder.DropIndex(
                name: "IX_VehiclePetrolCosts_VehicleNumber_Date",
                table: "VehiclePetrolCosts");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePetrolCosts_VehicleNumber_Date",
                table: "VehiclePetrolCosts",
                columns: new[] { "VehicleNumber", "Date" });
        }
    }
}
