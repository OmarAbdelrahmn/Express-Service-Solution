using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class updatevalidaiontconfige : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ThursdayIsCriticalDay",
                table: "Company2ValidationConfig",
                newName: "IsThursdayCritical");

            migrationBuilder.RenameColumn(
                name: "CriticalDaysOfMonthRaw",
                table: "Company2ValidationConfig",
                newName: "CriticalDaysOfMonth");

            migrationBuilder.AddColumn<bool>(
                name: "IsFridayCritical",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSaturdayCritical",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEJrAaEG9LVxZwajZur2uxHMX7xaAh2aCFR7ejXMZgPckumDSjp3Vw2GKS5miBkq7XQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAELf5gwj0QKlxG30FTEiQXEocs/0TP7MZ9spCa76D5Uo8IaVb7+frE/QlwRLwCwu/kg==");

            migrationBuilder.UpdateData(
                table: "Company2ValidationConfig",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "IsFridayCritical", "IsSaturdayCritical", "IsThursdayCritical" },
                values: new object[] { false, false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFridayCritical",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "IsSaturdayCritical",
                table: "Company2ValidationConfig");

            migrationBuilder.RenameColumn(
                name: "IsThursdayCritical",
                table: "Company2ValidationConfig",
                newName: "ThursdayIsCriticalDay");

            migrationBuilder.RenameColumn(
                name: "CriticalDaysOfMonth",
                table: "Company2ValidationConfig",
                newName: "CriticalDaysOfMonthRaw");

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
                column: "ThursdayIsCriticalDay",
                value: true);
        }
    }
}
