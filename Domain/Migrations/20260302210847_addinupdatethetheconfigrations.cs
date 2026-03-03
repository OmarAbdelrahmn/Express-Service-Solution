using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addinupdatethetheconfigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CriticalDaysOfMonthRaw",
                table: "Company2ValidationConfig",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ThursdayIsCriticalDay",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAECWPHL40eOY3SP8lPM6XWSjOXpGEFNJUHp0YLIMUscPmsWzjBvACqNdohpADU6bkEw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEH5U7XUfZkqdA+VCBIP7p/H5OhpApkItvyjXNNS+zwadmPt+KnmUAUGYEItML2Z/0w==");

            migrationBuilder.UpdateData(
                table: "Company2ValidationConfig",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CriticalDaysOfMonthRaw", "ThursdayIsCriticalDay" },
                values: new object[] { "", true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CriticalDaysOfMonthRaw",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "ThursdayIsCriticalDay",
                table: "Company2ValidationConfig");

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
        }
    }
}
