using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingshiftsforamazon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransporterShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    WorkingId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftIndex = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DurationHours = table.Column<float>(type: "real", nullable: false),
                    IsBreakDay = table.Column<bool>(type: "bit", nullable: false),
                    RawEntry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsManuallyEdited = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransporterShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransporterShifts_RiderDetails_RiderId",
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
                value: "AQAAAAIAAYagAAAAEJwfvKSAwU0w4Gd6XSMlk5jsu3wCXiSA+cSy7OssNm7+cuf4Ho3c3v4UnRwC4XimoQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEDnntJd8tKI2A6cin1Qo8AtGn/HArnpWirs6qfcyZZp723o9IvdQmXsYEPu5ZsT2sQ==");

            migrationBuilder.CreateIndex(
                name: "IX_TransporterShifts_RiderId",
                table: "TransporterShifts",
                column: "RiderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransporterShifts");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEHyQLC5suXXY+qRTGw6ocR7iPRjEvZtbTCVQcY5TL+GbdNKvnoxXQOn5aBCYmbA4VQ==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEITTX706FQdP+eEkh30Ab/w95CqhCTxFVa3d7y0Qmeef+cDZi695iUr26fJrj3EP3w==");
        }
    }
}
