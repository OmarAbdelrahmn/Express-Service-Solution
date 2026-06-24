using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AccountingCoreFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanyReceivables_CompanyBillImportId",
                table: "CompanyReceivables");

            migrationBuilder.DropIndex(
                name: "IX_CompanyPaymentReceipts_CompanyId",
                table: "CompanyPaymentReceipts");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "CompanyPaymentReceipts",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BankAccount",
                table: "CompanyPaymentReceipts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversedEntryId",
                table: "JournalEntries",
                column: "ReversedEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyReceivables_CompanyBillImportId",
                table: "CompanyReceivables",
                column: "CompanyBillImportId",
                unique: true,
                filter: "[CompanyBillImportId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPaymentReceipts_CompanyId_ReceiptDate_ReferenceNumber_BankAccount",
                table: "CompanyPaymentReceipts",
                columns: new[] { "CompanyId", "ReceiptDate", "ReferenceNumber", "BankAccount" },
                unique: true,
                filter: "[ReferenceNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_ReversedEntryId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_CompanyReceivables_CompanyBillImportId",
                table: "CompanyReceivables");

            migrationBuilder.DropIndex(
                name: "IX_CompanyPaymentReceipts_CompanyId_ReceiptDate_ReferenceNumber_BankAccount",
                table: "CompanyPaymentReceipts");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "CompanyPaymentReceipts",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BankAccount",
                table: "CompanyPaymentReceipts",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyReceivables_CompanyBillImportId",
                table: "CompanyReceivables",
                column: "CompanyBillImportId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPaymentReceipts_CompanyId",
                table: "CompanyPaymentReceipts",
                column: "CompanyId");
        }
    }
}
