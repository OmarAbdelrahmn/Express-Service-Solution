using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderCountToKeetaBreakPatterns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RiderCount",
                table: "KeetaBreakShiftPatterns",
                type: "int",
                nullable: false,
                // Existing historical patterns predate rider counts. Keep them valid with a
                // conservative placeholder; the next configuration version supplies real totals.
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_KeetaBreakShiftPatterns_RiderCount",
                table: "KeetaBreakShiftPatterns",
                sql: "[RiderCount] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_KeetaBreakShiftPatterns_RiderCount",
                table: "KeetaBreakShiftPatterns");

            migrationBuilder.DropColumn(
                name: "RiderCount",
                table: "KeetaBreakShiftPatterns");
        }
    }
}
