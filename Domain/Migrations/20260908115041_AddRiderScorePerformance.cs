using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderScorePerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiderScorePerformances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    WorkingId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceRiderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PerformanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalVerificationRequests = table.Column<int>(type: "int", nullable: false),
                    SuccessfulVerificationRequests = table.Column<int>(type: "int", nullable: false),
                    VerificationSuccessRate = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    GrossOrders = table.Column<int>(type: "int", nullable: false),
                    CompletedOrders = table.Column<int>(type: "int", nullable: false),
                    CompletedOrdersInTime = table.Column<int>(type: "int", nullable: false),
                    FailedOrdersByRider = table.Column<int>(type: "int", nullable: false),
                    OnTimeDeliveryScore = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    FinalDeliveryQualityScore = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    Segment = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderScorePerformances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderScorePerformances_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiderScorePerformances_PerformanceDate",
                table: "RiderScorePerformances",
                column: "PerformanceDate");

            migrationBuilder.CreateIndex(
                name: "IX_RiderScorePerformances_RiderId",
                table: "RiderScorePerformances",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderScorePerformances_RiderId_PerformanceDate",
                table: "RiderScorePerformances",
                columns: new[] { "RiderId", "PerformanceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderScorePerformances_SourceRiderId",
                table: "RiderScorePerformances",
                column: "SourceRiderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiderScorePerformances");
        }
    }
}
