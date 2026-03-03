using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class updatecomny2configration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FridayIsSpecialDay",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "MondayIsSpecialDay",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "SaturdayIsSpecialDay",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "SundayIsSpecialDay",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "ThursdayIsSpecialDay",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "TuesdayIsSpecialDay",
                table: "Company2ValidationConfig");

            migrationBuilder.DropColumn(
                name: "WednesdayIsSpecialDay",
                table: "Company2ValidationConfig");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEB2gFQRO4dZ7rlgW+eQtGsxbWo1UlJVHFCM/pM2wxFkIYPQKqsK6F6tcygRVB5dVHg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEBR9f+pScd/T9MECgyPlLzE4oLhKxXNwK7QvjfzKc0+rF27crJkSGE8+FbE9awRy6A==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FridayIsSpecialDay",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MondayIsSpecialDay",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SaturdayIsSpecialDay",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SundayIsSpecialDay",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ThursdayIsSpecialDay",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TuesdayIsSpecialDay",
                table: "Company2ValidationConfig",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WednesdayIsSpecialDay",
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
                columns: new[] { "FridayIsSpecialDay", "MondayIsSpecialDay", "SaturdayIsSpecialDay", "SundayIsSpecialDay", "ThursdayIsSpecialDay", "TuesdayIsSpecialDay", "WednesdayIsSpecialDay" },
                values: new object[] { true, false, false, false, true, false, false });
        }
    }
}
