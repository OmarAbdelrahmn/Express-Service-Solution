using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddKeetaBreakScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeetaBreakConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    BreakPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RoundingPolicy = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaBreakConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeetaBreakBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaBreakBatches", x => x.Id);
                    table.CheckConstraint("CK_KeetaBreakBatches_DateRange", "[PeriodEnd] >= [PeriodStart]");
                    table.ForeignKey(
                        name: "FK_KeetaBreakBatches_KeetaBreakConfigurations_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalTable: "KeetaBreakConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KeetaBreakShiftDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    MinimumRiders = table.Column<int>(type: "int", nullable: false),
                    MaximumRiders = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaBreakShiftDefinitions", x => x.Id);
                    table.CheckConstraint("CK_KeetaBreakShiftDefinitions_Staffing", "[MinimumRiders] >= 0 AND [MaximumRiders] >= [MinimumRiders]");
                    table.ForeignKey(
                        name: "FK_KeetaBreakShiftDefinitions_KeetaBreakConfigurations_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalTable: "KeetaBreakConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KeetaBreakAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderIdentifier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BreakDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedShiftsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaBreakAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeetaBreakAssignments_KeetaBreakBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "KeetaBreakBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KeetaBreakImportedRiders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RiderIdentifier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RiderName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HousingGroup = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShiftsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaBreakImportedRiders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeetaBreakImportedRiders_KeetaBreakBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "KeetaBreakBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakAssignments_BatchId",
                table: "KeetaBreakAssignments",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakAssignments_BreakDate_Status",
                table: "KeetaBreakAssignments",
                columns: new[] { "BreakDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakAssignments_RiderIdentifier_BreakDate",
                table: "KeetaBreakAssignments",
                columns: new[] { "RiderIdentifier", "BreakDate" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakBatches_ConfigurationId",
                table: "KeetaBreakBatches",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakBatches_PeriodStart_PeriodEnd_Status",
                table: "KeetaBreakBatches",
                columns: new[] { "PeriodStart", "PeriodEnd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakConfigurations_IsActive_EffectiveFrom",
                table: "KeetaBreakConfigurations",
                columns: new[] { "IsActive", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakImportedRiders_BatchId_RiderIdentifier",
                table: "KeetaBreakImportedRiders",
                columns: new[] { "BatchId", "RiderIdentifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakShiftDefinitions_ConfigurationId_ShiftKey",
                table: "KeetaBreakShiftDefinitions",
                columns: new[] { "ConfigurationId", "ShiftKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeetaBreakAssignments");

            migrationBuilder.DropTable(
                name: "KeetaBreakImportedRiders");

            migrationBuilder.DropTable(
                name: "KeetaBreakShiftDefinitions");

            migrationBuilder.DropTable(
                name: "KeetaBreakBatches");

            migrationBuilder.DropTable(
                name: "KeetaBreakConfigurations");
        }
    }
}
