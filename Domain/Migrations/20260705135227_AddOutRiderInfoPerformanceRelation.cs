using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddOutRiderInfoPerformanceRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutageShiftPerformances");

            migrationBuilder.CreateTable(
                name: "OutRiderInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
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
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    RejectedOrders = table.Column<int>(type: "int", nullable: false),
                    WorkingHours = table.Column<float>(type: "real", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutageShiftPerformances");

            migrationBuilder.DropTable(
                name: "OutRiderInfos");

            migrationBuilder.CreateTable(
                name: "OutageShiftPerformances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    RejectedOrders = table.Column<int>(type: "int", nullable: false),
                    WorkingHours = table.Column<float>(type: "real", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutageShiftPerformances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutageShiftPerformances_PhoneNumber",
                table: "OutageShiftPerformances",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_OutageShiftPerformances_ShiftDate",
                table: "OutageShiftPerformances",
                column: "ShiftDate");

            migrationBuilder.CreateIndex(
                name: "IX_OutageShiftPerformances_SystemId",
                table: "OutageShiftPerformances",
                column: "SystemId");

            migrationBuilder.CreateIndex(
                name: "IX_OutageShiftPerformances_SystemId_ShiftDate",
                table: "OutageShiftPerformances",
                columns: new[] { "SystemId", "ShiftDate" });
        }
    }
}
