using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class RemoveExternalRiders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutageShiftPerformances");

            migrationBuilder.DropTable(
                name: "OutRiderInfos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutRiderInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RiderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutRiderInfos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutageShiftPerformances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutRiderInfoId = table.Column<int>(type: "int", nullable: false),
                    AcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    RejectedOrders = table.Column<int>(type: "int", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WorkingHours = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutageShiftPerformances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutageShiftPerformances_OutRiderInfos_OutRiderInfoId",
                        column: x => x.OutRiderInfoId,
                        principalTable: "OutRiderInfos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutageShiftPerformances_OutRiderInfoId",
                table: "OutageShiftPerformances",
                column: "OutRiderInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_OutageShiftPerformances_OutRiderInfoId_ShiftDate",
                table: "OutageShiftPerformances",
                columns: new[] { "OutRiderInfoId", "ShiftDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutageShiftPerformances_ShiftDate",
                table: "OutageShiftPerformances",
                column: "ShiftDate");

            migrationBuilder.CreateIndex(
                name: "IX_OutRiderInfos_PhoneNumber",
                table: "OutRiderInfos",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_OutRiderInfos_RiderId",
                table: "OutRiderInfos",
                column: "RiderId",
                unique: true);
        }
    }
}
