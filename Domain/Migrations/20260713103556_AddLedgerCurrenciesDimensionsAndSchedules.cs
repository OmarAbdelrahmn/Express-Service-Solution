using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerCurrenciesDimensionsAndSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    FromCurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ToCurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialDimensions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialDimensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialDimensions_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringJournalSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    FrequencyMonths = table.Column<int>(type: "int", nullable: false),
                    NextRunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringJournalSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringJournalSchedules_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringJournalSchedules_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialDimensionValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinancialDimensionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialDimensionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialDimensionValues_FinancialDimensions_FinancialDimensionId",
                        column: x => x.FinancialDimensionId,
                        principalTable: "FinancialDimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringJournalScheduleLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecurringJournalScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringJournalScheduleLines", x => x.Id);
                    table.CheckConstraint("CK_RecurringJournalScheduleLines_OneSide", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)");
                    table.ForeignKey(
                        name: "FK_RecurringJournalScheduleLines_AccountingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringJournalScheduleLines_RecurringJournalSchedules_RecurringJournalScheduleId",
                        column: x => x.RecurringJournalScheduleId,
                        principalTable: "RecurringJournalSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialDocumentLineDimensions",
                columns: table => new
                {
                    FinancialDocumentLineId = table.Column<int>(type: "int", nullable: false),
                    FinancialDimensionValueId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialDocumentLineDimensions", x => new { x.FinancialDocumentLineId, x.FinancialDimensionValueId });
                    table.ForeignKey(
                        name: "FK_FinancialDocumentLineDimensions_FinancialDimensionValues_FinancialDimensionValueId",
                        column: x => x.FinancialDimensionValueId,
                        principalTable: "FinancialDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialDocumentLineDimensions_FinancialDocumentLines_FinancialDocumentLineId",
                        column: x => x.FinancialDocumentLineId,
                        principalTable: "FinancialDocumentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLineDimensions",
                columns: table => new
                {
                    JournalLineId = table.Column<int>(type: "int", nullable: false),
                    FinancialDimensionValueId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLineDimensions", x => new { x.JournalLineId, x.FinancialDimensionValueId });
                    table.ForeignKey(
                        name: "FK_JournalLineDimensions_FinancialDimensionValues_FinancialDimensionValueId",
                        column: x => x.FinancialDimensionValueId,
                        principalTable: "FinancialDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLineDimensions_JournalLines_JournalLineId",
                        column: x => x.JournalLineId,
                        principalTable: "JournalLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_LegalEntityId_FromCurrencyCode_ToCurrencyCode_EffectiveDate",
                table: "ExchangeRates",
                columns: new[] { "LegalEntityId", "FromCurrencyCode", "ToCurrencyCode", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDimensions_LegalEntityId_Code",
                table: "FinancialDimensions",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDimensionValues_FinancialDimensionId_Code",
                table: "FinancialDimensionValues",
                columns: new[] { "FinancialDimensionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDocumentLineDimensions_FinancialDimensionValueId",
                table: "FinancialDocumentLineDimensions",
                column: "FinancialDimensionValueId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLineDimensions_FinancialDimensionValueId",
                table: "JournalLineDimensions",
                column: "FinancialDimensionValueId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalScheduleLines_AccountId",
                table: "RecurringJournalScheduleLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalScheduleLines_RecurringJournalScheduleId_LineNumber",
                table: "RecurringJournalScheduleLines",
                columns: new[] { "RecurringJournalScheduleId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalSchedules_BranchId",
                table: "RecurringJournalSchedules",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalSchedules_LegalEntityId",
                table: "RecurringJournalSchedules",
                column: "LegalEntityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "FinancialDocumentLineDimensions");

            migrationBuilder.DropTable(
                name: "JournalLineDimensions");

            migrationBuilder.DropTable(
                name: "RecurringJournalScheduleLines");

            migrationBuilder.DropTable(
                name: "FinancialDimensionValues");

            migrationBuilder.DropTable(
                name: "RecurringJournalSchedules");

            migrationBuilder.DropTable(
                name: "FinancialDimensions");
        }
    }
}
