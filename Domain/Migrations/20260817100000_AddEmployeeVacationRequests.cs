using Domain;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations;

[DbContext(typeof(ApplicationDbcontext))]
[Migration("20260817100000_AddEmployeeVacationRequests")]
public partial class AddEmployeeVacationRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "EmployeeIqamaNo",
            table: "VacationRequests",
            type: "bigint",
            nullable: true);

        // Every existing request belongs to a rider. Copy that rider's employee
        // key before making the new employee relationship required.
        migrationBuilder.Sql("""
            UPDATE vacation
            SET EmployeeIqamaNo = rider.EmployeeIqamaNo
            FROM VacationRequests AS vacation
            INNER JOIN RiderDetails AS rider ON rider.Id = vacation.RiderId;
            """);

        migrationBuilder.AlterColumn<long>(
            name: "EmployeeIqamaNo",
            table: "VacationRequests",
            type: "bigint",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "bigint",
            oldNullable: true);

        migrationBuilder.DropForeignKey(
            name: "FK_VacationRequests_RiderDetails_RiderId",
            table: "VacationRequests");

        migrationBuilder.AlterColumn<int>(
            name: "RiderId",
            table: "VacationRequests",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AddForeignKey(
            name: "FK_VacationRequests_Employees_EmployeeIqamaNo",
            table: "VacationRequests",
            column: "EmployeeIqamaNo",
            principalTable: "Employees",
            principalColumn: "IqamaNo",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_VacationRequests_RiderDetails_RiderId",
            table: "VacationRequests",
            column: "RiderId",
            principalTable: "RiderDetails",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropIndex(
            name: "IX_VacationRequests_RiderId_Status_StartDate_EndDate",
            table: "VacationRequests");

        migrationBuilder.CreateIndex(
            name: "IX_VacationRequests_EmployeeIqamaNo_Status_StartDate_EndDate",
            table: "VacationRequests",
            columns: new[] { "EmployeeIqamaNo", "Status", "StartDate", "EndDate" });

        migrationBuilder.CreateIndex(
            name: "IX_VacationRequests_EmployeeIqamaNo",
            table: "VacationRequests",
            column: "EmployeeIqamaNo");

        migrationBuilder.CreateIndex(
            name: "IX_VacationRequests_RiderId",
            table: "VacationRequests",
            column: "RiderId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_VacationRequests_Employees_EmployeeIqamaNo", table: "VacationRequests");
        migrationBuilder.DropForeignKey(name: "FK_VacationRequests_RiderDetails_RiderId", table: "VacationRequests");
        migrationBuilder.DropIndex(name: "IX_VacationRequests_EmployeeIqamaNo_Status_StartDate_EndDate", table: "VacationRequests");
        migrationBuilder.DropIndex(name: "IX_VacationRequests_EmployeeIqamaNo", table: "VacationRequests");
        migrationBuilder.DropIndex(name: "IX_VacationRequests_RiderId", table: "VacationRequests");

        migrationBuilder.AlterColumn<int>(
            name: "RiderId",
            table: "VacationRequests",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AddForeignKey(
            name: "FK_VacationRequests_RiderDetails_RiderId",
            table: "VacationRequests",
            column: "RiderId",
            principalTable: "RiderDetails",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropColumn(name: "EmployeeIqamaNo", table: "VacationRequests");

        migrationBuilder.CreateIndex(
            name: "IX_VacationRequests_RiderId_Status_StartDate_EndDate",
            table: "VacationRequests",
            columns: new[] { "RiderId", "Status", "StartDate", "EndDate" });
    }
}
