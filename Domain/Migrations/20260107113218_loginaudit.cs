using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class loginaudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastLogin",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                columns: new[] { "LastLogin", "PasswordHash" },
                values: new object[] { null, "AQAAAAIAAYagAAAAEEP3/weY/B//UfhEsKrpLTrCF1/5YYsLSENSiWviVY1O3InJLCr2RJE1Szn5b2PYCA==" });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                columns: new[] { "LastLogin", "PasswordHash" },
                values: new object[] { null, "AQAAAAIAAYagAAAAEF0bYr0YvqPHrXV8/DhjXdlMuymEhLFQNYzTFpcANX11FLPtDJRK+Jz6/rHscVgClA==" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLogin",
                table: "AspNetUsers");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEE8WS9658JhFs/6KSRhpWWQRXSLSmYt3iofAjpgEd7EtWdJUXqJ5IbYMeqrNLg2Gbw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEGAbJ/0YTI4LELpJa71STkGmtDPvHT8aTpweOFHVxZqdtcBoJ6RuUXype3M9FSXdUw==");
        }
    }
}
