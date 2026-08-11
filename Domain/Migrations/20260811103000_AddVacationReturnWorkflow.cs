using System;
using Domain;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    [DbContext(typeof(ApplicationDbcontext))]
    [Migration("20260811103000_AddVacationReturnWorkflow")]
    public partial class AddVacationReturnWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuperseded",
                table: "VacationApprovalDecisions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "VacationApprovalDecisions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetRole",
                table: "VacationApprovalDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.DropIndex(
                name: "IX_VacationApprovalDecisions_VacationRequestId_Role",
                table: "VacationApprovalDecisions");

            migrationBuilder.CreateIndex(
                name: "IX_VacationApprovalDecisions_VacationRequestId_DecidedAt",
                table: "VacationApprovalDecisions",
                columns: new[] { "VacationRequestId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationApprovalDecisions_VacationRequestId_Role",
                table: "VacationApprovalDecisions",
                columns: new[] { "VacationRequestId", "Role" },
                unique: true,
                filter: "[Decision] = 1 AND [IsSuperseded] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VacationApprovalDecisions_VacationRequestId_DecidedAt",
                table: "VacationApprovalDecisions");

            migrationBuilder.DropIndex(
                name: "IX_VacationApprovalDecisions_VacationRequestId_Role",
                table: "VacationApprovalDecisions");

            migrationBuilder.DropColumn(
                name: "IsSuperseded",
                table: "VacationApprovalDecisions");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "VacationApprovalDecisions");

            migrationBuilder.DropColumn(
                name: "TargetRole",
                table: "VacationApprovalDecisions");

            migrationBuilder.CreateIndex(
                name: "IX_VacationApprovalDecisions_VacationRequestId_Role",
                table: "VacationApprovalDecisions",
                columns: new[] { "VacationRequestId", "Role" },
                unique: true);
        }
    }
}
