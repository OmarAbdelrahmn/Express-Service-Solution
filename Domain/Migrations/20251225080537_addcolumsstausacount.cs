using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addcolumsstausacount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcountStatus",
                table: "DeletedEmployees",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEC3yi9Y5FAfetSfeIeJxfxvJQZ4xY+HTeC6ysukwUoaPK/dV9k08M9ytb+3w7jl+Zg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEM6sqzPP6rK5fBxAN86N2KGo7ubO8xMaIDn6rHHjc21CPvBZAaR6iIALA19wZQo6Fw==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcountStatus",
                table: "DeletedEmployees");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEL6RlmqJ6zMZrjighNK/ptCQeOiOjWMa8gBtzQ6M/7abFKG9kGept/ThmXUd5EbIQg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEG0IhVr73eKY+3TiJUZ4FxT51sFknfO7E7KqkW02S42Io4A5VVqwt01YhNnU98hIWg==");
        }
    }
}
