using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addwallettothesystme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(38,0)", nullable: false),
                    MainRiderId = table.Column<int>(type: "int", nullable: true),
                    WorkedRiderId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wallets_RiderDetails_MainRiderId",
                        column: x => x.MainRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Wallets_RiderDetails_WorkedRiderId",
                        column: x => x.WorkedRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFcacLTItrcca7RC1656U+paEcuElHJHQKFVoVyKbCYFjNQmxsO1m/c8zOlQSp/Ziw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMwIK4NrIYuotGD+RHttb6QfVoUX2ItVMEauyQZqVkGmsVfZQLLlv6J0o50RwUtCJg==");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_MainRiderId",
                table: "Wallets",
                column: "MainRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_WorkedRiderId",
                table: "Wallets",
                column: "WorkedRiderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEDYZ4Od2j+qp+6F26pYK4j6eSFh1refSArZXllvLlH+pNZRlEPfMwD0SOUCk8yQi5Q==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEEa3YQeJ3EYdr477Oh5H4G/QtkOXDHH9JhFYYZ/DOE4aGbplA5e8p+wI5264/1nHgg==");
        }
    }
}
