using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderSalaryRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiderSalaryRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    TemplateType = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MinimumAcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExtraOrderAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BelowThresholdOrderAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderSalaryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderSalaryRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "RiderSalaryRules",
                columns: new[] { "Id", "BaseAmount", "BelowThresholdOrderAmount", "CompanyId", "EffectiveFrom", "EffectiveTo", "ExtraOrderAmount", "IsActive", "MinimumAcceptedOrders", "Name", "Notes", "Priority", "TemplateType" },
                values: new object[] { 1, 2000m, 3m, null, new DateOnly(2026, 1, 1), null, 6m, true, 500, "Default FTR Hunger salary", "Default rule matching the previous hardcoded Hunger/FTR salary formula.", 0, 5 });

            migrationBuilder.CreateIndex(
                name: "IX_RiderSalaryRules_CompanyId_TemplateType_IsActive_EffectiveFrom",
                table: "RiderSalaryRules",
                columns: new[] { "CompanyId", "TemplateType", "IsActive", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiderSalaryRules");

        }
    }
}
