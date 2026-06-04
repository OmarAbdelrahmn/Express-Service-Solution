using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingmonthlyvalidityservice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeetaDriverShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlatformDriverId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkingId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RiderId = table.Column<int>(type: "int", nullable: true),
                    Supervisor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsInShift = table.Column<bool>(type: "bit", nullable: false),
                    TotalConnectionTimeRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalConnectionMinutes = table.Column<int>(type: "int", nullable: false),
                    TasksDelivered = table.Column<int>(type: "int", nullable: false),
                    RawShiftSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QualifiedSlotsCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaDriverShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeetaDriverShifts_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "KeetaShiftSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KeetaDriverShiftId = table.Column<int>(type: "int", nullable: false),
                    SlotKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    IsOnShift = table.Column<bool>(type: "bit", nullable: false),
                    IsQualified = table.Column<bool>(type: "bit", nullable: false),
                    DurationRaw = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    SlotOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaShiftSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeetaShiftSlots_KeetaDriverShifts_KeetaDriverShiftId",
                        column: x => x.KeetaDriverShiftId,
                        principalTable: "KeetaDriverShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEA/zZpuqFzbTSnicQa4Tooll0FGxeDLCE2M5TALeSVR6BGE45Era3fs5IhF5zU2ZyQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEFpg1iN3qC51jcJrS5Ea9/Ab1Xi7kXnwjCrMOynu6YUpw7q1mrTe8yz+5Cx2W01t5A==");

            migrationBuilder.CreateIndex(
                name: "IX_KeetaDriverShifts_RiderId",
                table: "KeetaDriverShifts",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_KeetaShiftSlots_KeetaDriverShiftId",
                table: "KeetaShiftSlots",
                column: "KeetaDriverShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeetaShiftSlots");

            migrationBuilder.DropTable(
                name: "KeetaDriverShifts");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEKGLiqN82usDF1OOOLranMDzVr2Go7K4Ttde4fPOyUJknIwlWV4VoHpBRSc54D1CmQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMfoYkERX991JbMWBa64iTF9I7lWc6N7mcMQN77xKVfSR9qV/pHRIFZvURWY2by8gQ==");
        }
    }
}
