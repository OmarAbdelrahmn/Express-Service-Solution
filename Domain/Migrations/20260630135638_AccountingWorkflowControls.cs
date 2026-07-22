using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AccountingWorkflowControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankAccountId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BankAccountId",
                table: "CompanyPaymentReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BankAccountId",
                table: "CompanyExpenses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RiderFinalSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    FinalSalaryAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReimbursementAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ManualDeductionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OutstandingLoanBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LoanWriteOffAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LoanFinalDeductionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetSettlementAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderFinalSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderFinalSettlements_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AccountingAccounts",
                columns: new[] { "Id", "Code", "IsActive", "IsSystem", "Name", "ParentAccountId", "Type" },
                values: new object[] { 18, "2200", true, true, "VAT Payable", null, 2 });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_BankAccountId",
                table: "JournalEntryLines",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPaymentReceipts_BankAccountId",
                table: "CompanyPaymentReceipts",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_BankAccountId",
                table: "CompanyExpenses",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinalSettlements_RiderId_Year_Month",
                table: "RiderFinalSettlements",
                columns: new[] { "RiderId", "Year", "Month" });

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyExpenses_BankAccounts_BankAccountId",
                table: "CompanyExpenses",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyPaymentReceipts_BankAccounts_BankAccountId",
                table: "CompanyPaymentReceipts",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_BankAccounts_BankAccountId",
                table: "JournalEntryLines",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyExpenses_BankAccounts_BankAccountId",
                table: "CompanyExpenses");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanyPaymentReceipts_BankAccounts_BankAccountId",
                table: "CompanyPaymentReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_BankAccounts_BankAccountId",
                table: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "RiderFinalSettlements");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_BankAccountId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_CompanyPaymentReceipts_BankAccountId",
                table: "CompanyPaymentReceipts");

            migrationBuilder.DropIndex(
                name: "IX_CompanyExpenses_BankAccountId",
                table: "CompanyExpenses");

            migrationBuilder.DeleteData(
                table: "AccountingAccounts",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "CompanyPaymentReceipts");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "CompanyExpenses");

        }
    }
}
