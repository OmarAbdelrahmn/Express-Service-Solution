using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class init12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TempRiderShiftComparisons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    WorkingId = table.Column<int>(type: "int", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    IsSubstitution = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    OriginalRiderWorkingId = table.Column<int>(type: "int", nullable: true),
                    OldAcceptedDailyOrders = table.Column<int>(type: "int", nullable: true),
                    OldRejectedDailyOrders = table.Column<int>(type: "int", nullable: true),
                    OldRealRejectedDailyOrders = table.Column<int>(type: "int", nullable: true),
                    OldWorkingHours = table.Column<float>(type: "real", nullable: true),
                    OldShiftStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OldCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewAcceptedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    NewRejectedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    NewRealRejectedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    NewWorkingHours = table.Column<float>(type: "real", nullable: false),
                    NewShiftStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempRiderShiftComparisons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TempRiderShiftComparisons_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TempRiderShiftComparisons_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAENJ4Go9Cfy39UmsP5+ihLGdQkfVrbcWOOQn3CzqN9fvSVd0VWObJcECMWtix1VfKlQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEJ5VC5NhcJ4y0mp7hJx7zQ45yRfvnGa9b2zM4Z+j9q83PUEz1oa4Fa4AWA/Qdy2hng==");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_CompanyId",
                table: "TempRiderShiftComparisons",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_IsResolved",
                table: "TempRiderShiftComparisons",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_IsSubstitution",
                table: "TempRiderShiftComparisons",
                column: "IsSubstitution");

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_RiderId_WorkingId_ShiftDate",
                table: "TempRiderShiftComparisons",
                columns: new[] { "RiderId", "WorkingId", "ShiftDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TempRiderShiftComparisons_ShiftDate_WorkingId",
                table: "TempRiderShiftComparisons",
                columns: new[] { "ShiftDate", "WorkingId" });

            migrationBuilder.AddForeignKey(
                name: "FK_RiderShifts_Companies_CompanyId",
                table: "RiderShifts",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderShifts_Companies_CompanyId",
                table: "RiderShifts");

            migrationBuilder.DropTable(
                name: "TempRiderShiftComparisons");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEN2WRgZyzYWwltNRVbQ/8W14MCXCzPv/Uw0xy/3mr1RnWltCzLsh4T48XoBKFolkww==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOWyQUamW2g9kRI9OgUo70BLpywR80ImerfPczsxcLdtDcRU2aRo3SiS++pGcoXocQ==");
        }
    }
}
