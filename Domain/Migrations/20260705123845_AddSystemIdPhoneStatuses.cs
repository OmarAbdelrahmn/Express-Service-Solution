using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemIdPhoneStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemIdPhoneStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StatusDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RawStatus = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemIdPhoneStatuses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemIdPhoneStatuses_PhoneNumber",
                table: "SystemIdPhoneStatuses",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_SystemIdPhoneStatuses_StatusDate",
                table: "SystemIdPhoneStatuses",
                column: "StatusDate");

            migrationBuilder.CreateIndex(
                name: "IX_SystemIdPhoneStatuses_SystemId",
                table: "SystemIdPhoneStatuses",
                column: "SystemId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemIdPhoneStatuses_SystemId_StatusDate",
                table: "SystemIdPhoneStatuses",
                columns: new[] { "SystemId", "StatusDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemIdPhoneStatuses");

        }
    }
}
