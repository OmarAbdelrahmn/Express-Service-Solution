using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialOperationsPhaseThreeToFive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PostingProfileCode",
                table: "FinancialDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "FinancialDocuments",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    LedgerAccountId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankAccounts_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.CheckConstraint("CK_Budgets_Dates", "[EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_Budgets_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxRegistrationNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAccounts_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    GrossSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FixedDeduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayContracts", x => x.Id);
                    table.CheckConstraint("CK_EmployeePayContracts_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                });

            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    AssetNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AcquisitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AcquisitionCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ResidualValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    AssetAccountId = table.Column<int>(type: "int", nullable: false),
                    AccumulatedDepreciationAccountId = table.Column<int>(type: "int", nullable: false),
                    DepreciationExpenseAccountId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                    table.CheckConstraint("CK_FixedAssets_Values", "[AcquisitionCost] >= 0 AND [ResidualValue] >= 0 AND [ResidualValue] <= [AcquisitionCost] AND [UsefulLifeMonths] > 0");
                    table.ForeignKey(
                        name: "FK_FixedAssets_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryItems_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PayrollExpenseAccountId = table.Column<int>(type: "int", nullable: false),
                    PayrollPayableAccountId = table.Column<int>(type: "int", nullable: false),
                    DeductionLiabilityAccountId = table.Column<int>(type: "int", nullable: false),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AccrualFinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentFinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeductionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.CheckConstraint("CK_PayrollRuns_Dates", "[PeriodEnd] >= [PeriodStart]");
                    table.ForeignKey(
                        name: "FK_PayrollRuns_FinancialDocuments_AccrualFinancialDocumentId",
                        column: x => x.AccrualFinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_FinancialDocuments_PaymentFinancialDocumentId",
                        column: x => x.PaymentFinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceEvidences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "int", nullable: true),
                    EvidenceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StorageLocator = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReceivedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceEvidences_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEvidences_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxRegistrationNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierAccounts_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    TaxAccountId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodes", x => x.Id);
                    table.CheckConstraint("CK_TaxCodes_Rate", "[Rate] >= 0 AND [Rate] <= 1");
                    table.ForeignKey(
                        name: "FK_TaxCodes_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    OutputTaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InputTaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetTaxPayableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmissionReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxReturns", x => x.Id);
                    table.CheckConstraint("CK_TaxReturns_Dates", "[PeriodEnd] >= [PeriodStart]");
                    table.ForeignKey(
                        name: "FK_TaxReturns_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MatchedFinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReconciledBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReconciledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_FinancialDocuments_MatchedFinancialDocumentId",
                        column: x => x.MatchedFinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    FinancialDimensionValueId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetLines_AccountingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetLines_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetLines_FinancialDimensionValues_FinancialDimensionValueId",
                        column: x => x.FinancialDimensionValueId,
                        principalTable: "FinancialDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashAccountId = table.Column<int>(type: "int", nullable: false),
                    ReceivableAccountId = table.Column<int>(type: "int", nullable: false),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceipts", x => x.Id);
                    table.CheckConstraint("CK_CustomerReceipts_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CustomerReceipts_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceipts_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementType = table.Column<int>(type: "int", nullable: false),
                    MovementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FromBin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ToBin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DebitAccountId = table.Column<int>(type: "int", nullable: false),
                    CreditAccountId = table.Column<int>(type: "int", nullable: false),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.CheckConstraint("CK_InventoryMovements_Amounts", "[Quantity] > 0 AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryMovements_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    EmployeePayContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DeductionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunLines", x => x.Id);
                    table.CheckConstraint("CK_PayrollRunLines_Amounts", "[GrossAmount] >= 0 AND [DeductionAmount] >= 0 AND [NetAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollRunLines_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    ReceivableAccountId = table.Column<int>(type: "int", nullable: false),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInvoices", x => x.Id);
                    table.CheckConstraint("CK_CustomerInvoices_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_CustomerInvoices_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerInvoices_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerInvoices_SourceEvidences_SourceEvidenceId",
                        column: x => x.SourceEvidenceId,
                        principalTable: "SourceEvidences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    SourceEvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GrossRevenue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetSettlementAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformClearingAccountId = table.Column<int>(type: "int", nullable: false),
                    CommissionExpenseAccountId = table.Column<int>(type: "int", nullable: false),
                    RevenueAccountId = table.Column<int>(type: "int", nullable: false),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettlements", x => x.Id);
                    table.CheckConstraint("CK_PlatformSettlements_Amounts", "[GrossRevenue] >= 0 AND [CommissionAmount] >= 0 AND [NetSettlementAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_PlatformSettlements_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformSettlements_SourceEvidences_SourceEvidenceId",
                        column: x => x.SourceEvidenceId,
                        principalTable: "SourceEvidences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    SupplierAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    PayableAccountId = table.Column<int>(type: "int", nullable: false),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoices", x => x.Id);
                    table.CheckConstraint("CK_SupplierInvoices_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0 AND [GrossAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_SupplierInvoices_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierInvoices_SourceEvidences_SourceEvidenceId",
                        column: x => x.SourceEvidenceId,
                        principalTable: "SourceEvidences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierInvoices_SupplierAccounts_SupplierAccountId",
                        column: x => x.SupplierAccountId,
                        principalTable: "SupplierAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    SupplierAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashAccountId = table.Column<int>(type: "int", nullable: false),
                    PayableAccountId = table.Column<int>(type: "int", nullable: false),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.CheckConstraint("CK_SupplierPayments_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SupplierPayments_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_SupplierAccounts_SupplierAccountId",
                        column: x => x.SupplierAccountId,
                        principalTable: "SupplierAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    SourceEvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpenseAccountId = table.Column<int>(type: "int", nullable: false),
                    EmployeePayableAccountId = table.Column<int>(type: "int", nullable: false),
                    TaxCodeId = table.Column<int>(type: "int", nullable: true),
                    PostingProfileCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseClaims", x => x.Id);
                    table.CheckConstraint("CK_ExpenseClaims_Amounts", "[NetAmount] >= 0 AND [TaxAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_SourceEvidences_SourceEvidenceId",
                        column: x => x.SourceEvidenceId,
                        principalTable: "SourceEvidences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_TaxCodes_TaxCodeId",
                        column: x => x.TaxCodeId,
                        principalTable: "TaxCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    TaxCodeId = table.Column<int>(type: "int", nullable: false),
                    FinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxTransactions_FinancialDocuments_FinancialDocumentId",
                        column: x => x.FinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxTransactions_TaxCodes_TaxCodeId",
                        column: x => x.TaxCodeId,
                        principalTable: "TaxCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxTransactions_TaxReturns_TaxReturnId",
                        column: x => x.TaxReturnId,
                        principalTable: "TaxReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RevenueAccountId = table.Column<int>(type: "int", nullable: false),
                    TaxCodeId = table.Column<int>(type: "int", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_CustomerInvoiceLines_Quantity", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [NetAmount] >= 0 AND [TaxAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_CustomerInvoiceLines_CustomerInvoices_CustomerInvoiceId",
                        column: x => x.CustomerInvoiceId,
                        principalTable: "CustomerInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerInvoiceLines_TaxCodes_TaxCodeId",
                        column: x => x.TaxCodeId,
                        principalTable: "TaxCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AllocatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceiptAllocations", x => x.Id);
                    table.CheckConstraint("CK_CustomerReceiptAllocations_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_CustomerInvoices_CustomerInvoiceId",
                        column: x => x.CustomerInvoiceId,
                        principalTable: "CustomerInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_CustomerReceipts_CustomerReceiptId",
                        column: x => x.CustomerReceiptId,
                        principalTable: "CustomerReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpenseOrInventoryAccountId = table.Column<int>(type: "int", nullable: false),
                    TaxCodeId = table.Column<int>(type: "int", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_SupplierInvoiceLines_Quantity", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [NetAmount] >= 0 AND [TaxAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceLines_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceLines_TaxCodes_TaxCodeId",
                        column: x => x.TaxCodeId,
                        principalTable: "TaxCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AllocatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaymentAllocations", x => x.Id);
                    table.CheckConstraint("CK_SupplierPaymentAllocations_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_LegalEntityId_Code",
                table: "BankAccounts",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_BankAccountId_ExternalReference",
                table: "BankStatementLines",
                columns: new[] { "BankAccountId", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_MatchedFinancialDocumentId",
                table: "BankStatementLines",
                column: "MatchedFinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_AccountId",
                table: "BudgetLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_BudgetId_AccountId_FinancialDimensionValueId",
                table: "BudgetLines",
                columns: new[] { "BudgetId", "AccountId", "FinancialDimensionValueId" },
                unique: true,
                filter: "[FinancialDimensionValueId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_FinancialDimensionValueId",
                table: "BudgetLines",
                column: "FinancialDimensionValueId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_LegalEntityId_Name",
                table: "Budgets",
                columns: new[] { "LegalEntityId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_LegalEntityId_Code",
                table: "CustomerAccounts",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoiceLines_CustomerInvoiceId_LineNumber",
                table: "CustomerInvoiceLines",
                columns: new[] { "CustomerInvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoiceLines_TaxCodeId",
                table: "CustomerInvoiceLines",
                column: "TaxCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_CustomerAccountId",
                table: "CustomerInvoices",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_FinancialDocumentId",
                table: "CustomerInvoices",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_LegalEntityId_InvoiceNumber",
                table: "CustomerInvoices",
                columns: new[] { "LegalEntityId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_SourceEvidenceId",
                table: "CustomerInvoices",
                column: "SourceEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_CustomerInvoiceId",
                table: "CustomerReceiptAllocations",
                column: "CustomerInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_CustomerReceiptId_CustomerInvoiceId",
                table: "CustomerReceiptAllocations",
                columns: new[] { "CustomerReceiptId", "CustomerInvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_CustomerAccountId",
                table: "CustomerReceipts",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_FinancialDocumentId",
                table: "CustomerReceipts",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_LegalEntityId_ExternalReference",
                table: "CustomerReceipts",
                columns: new[] { "LegalEntityId", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_LegalEntityId_ReceiptNumber",
                table: "CustomerReceipts",
                columns: new[] { "LegalEntityId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayContracts_LegalEntityId_EmployeeIqamaNo_EffectiveFrom",
                table: "EmployeePayContracts",
                columns: new[] { "LegalEntityId", "EmployeeIqamaNo", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_FinancialDocumentId",
                table: "ExpenseClaims",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_LegalEntityId_ClaimNumber",
                table: "ExpenseClaims",
                columns: new[] { "LegalEntityId", "ClaimNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_SourceEvidenceId",
                table: "ExpenseClaims",
                column: "SourceEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_TaxCodeId",
                table: "ExpenseClaims",
                column: "TaxCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_LegalEntityId_AssetNumber",
                table: "FixedAssets",
                columns: new[] { "LegalEntityId", "AssetNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_LegalEntityId_Sku",
                table: "InventoryItems",
                columns: new[] { "LegalEntityId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_FinancialDocumentId",
                table: "InventoryMovements",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_InventoryItemId",
                table: "InventoryMovements",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_LegalEntityId_Reference",
                table: "InventoryMovements",
                columns: new[] { "LegalEntityId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunLines_PayrollRunId_EmployeeIqamaNo",
                table: "PayrollRunLines",
                columns: new[] { "PayrollRunId", "EmployeeIqamaNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_AccrualFinancialDocumentId",
                table: "PayrollRuns",
                column: "AccrualFinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LegalEntityId_PeriodStart_PeriodEnd",
                table: "PayrollRuns",
                columns: new[] { "LegalEntityId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LegalEntityId_RunNumber",
                table: "PayrollRuns",
                columns: new[] { "LegalEntityId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PaymentFinancialDocumentId",
                table: "PayrollRuns",
                column: "PaymentFinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlements_FinancialDocumentId",
                table: "PlatformSettlements",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlements_LegalEntityId_SettlementReference",
                table: "PlatformSettlements",
                columns: new[] { "LegalEntityId", "SettlementReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlements_SourceEvidenceId",
                table: "PlatformSettlements",
                column: "SourceEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidences_LegalEntityId_ContentHash",
                table: "SourceEvidences",
                columns: new[] { "LegalEntityId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidences_PlatformAccountId_ExternalReference",
                table: "SourceEvidences",
                columns: new[] { "PlatformAccountId", "ExternalReference" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAccounts_LegalEntityId_Code",
                table: "SupplierAccounts",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceLines_SupplierInvoiceId_LineNumber",
                table: "SupplierInvoiceLines",
                columns: new[] { "SupplierInvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceLines_TaxCodeId",
                table: "SupplierInvoiceLines",
                column: "TaxCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_FinancialDocumentId",
                table: "SupplierInvoices",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_LegalEntityId_InvoiceNumber",
                table: "SupplierInvoices",
                columns: new[] { "LegalEntityId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_SourceEvidenceId",
                table: "SupplierInvoices",
                column: "SourceEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_SupplierAccountId",
                table: "SupplierInvoices",
                column: "SupplierAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierInvoiceId",
                table: "SupplierPaymentAllocations",
                column: "SupplierInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierPaymentId_SupplierInvoiceId",
                table: "SupplierPaymentAllocations",
                columns: new[] { "SupplierPaymentId", "SupplierInvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_FinancialDocumentId",
                table: "SupplierPayments",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_LegalEntityId_ExternalReference",
                table: "SupplierPayments",
                columns: new[] { "LegalEntityId", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_LegalEntityId_PaymentNumber",
                table: "SupplierPayments",
                columns: new[] { "LegalEntityId", "PaymentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierAccountId",
                table: "SupplierPayments",
                column: "SupplierAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodes_LegalEntityId_Code",
                table: "TaxCodes",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxReturns_LegalEntityId_PeriodStart_PeriodEnd",
                table: "TaxReturns",
                columns: new[] { "LegalEntityId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxTransactions_FinancialDocumentId",
                table: "TaxTransactions",
                column: "FinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxTransactions_LegalEntityId_TransactionDate_TaxReturnId",
                table: "TaxTransactions",
                columns: new[] { "LegalEntityId", "TransactionDate", "TaxReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxTransactions_TaxCodeId",
                table: "TaxTransactions",
                column: "TaxCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxTransactions_TaxReturnId",
                table: "TaxTransactions",
                column: "TaxReturnId");

            migrationBuilder.Sql(@"
CREATE TRIGGER [dbo].[TR_SourceEvidences_ImmutableFields] ON [dbo].[SourceEvidences]
AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE([LegalEntityId]) OR UPDATE([PlatformAccountId]) OR UPDATE([EvidenceType]) OR UPDATE([ExternalReference]) OR UPDATE([StorageLocator]) OR UPDATE([ContentHash]) OR UPDATE([MetadataJson]) OR UPDATE([ReceivedBy]) OR UPDATE([ReceivedAt])
    BEGIN
        RAISERROR ('Source evidence content and provenance are immutable after receipt.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END");

            migrationBuilder.Sql(@"
CREATE TRIGGER [dbo].[TR_SourceEvidences_NoDelete] ON [dbo].[SourceEvidences]
INSTEAD OF DELETE AS
BEGIN
    RAISERROR ('Source evidence cannot be deleted.', 16, 1);
    ROLLBACK TRANSACTION;
END");

            migrationBuilder.Sql(@"
CREATE TRIGGER [dbo].[TR_InventoryMovements_NoUpdate] ON [dbo].[InventoryMovements]
AFTER UPDATE AS
BEGIN
    RAISERROR ('Inventory movements are immutable. Record a correcting movement instead.', 16, 1);
    ROLLBACK TRANSACTION;
END");

            migrationBuilder.Sql(@"
CREATE TRIGGER [dbo].[TR_InventoryMovements_NoDelete] ON [dbo].[InventoryMovements]
INSTEAD OF DELETE AS
BEGIN
    RAISERROR ('Inventory movements cannot be deleted. Record a correcting movement instead.', 16, 1);
    ROLLBACK TRANSACTION;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_SourceEvidences_NoDelete];");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_SourceEvidences_ImmutableFields];");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_InventoryMovements_NoDelete];");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_InventoryMovements_NoUpdate];");

            migrationBuilder.DropTable(
                name: "BankStatementLines");

            migrationBuilder.DropTable(
                name: "BudgetLines");

            migrationBuilder.DropTable(
                name: "CustomerInvoiceLines");

            migrationBuilder.DropTable(
                name: "CustomerReceiptAllocations");

            migrationBuilder.DropTable(
                name: "EmployeePayContracts");

            migrationBuilder.DropTable(
                name: "ExpenseClaims");

            migrationBuilder.DropTable(
                name: "FixedAssets");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "PayrollRunLines");

            migrationBuilder.DropTable(
                name: "PlatformSettlements");

            migrationBuilder.DropTable(
                name: "SupplierInvoiceLines");

            migrationBuilder.DropTable(
                name: "SupplierPaymentAllocations");

            migrationBuilder.DropTable(
                name: "TaxTransactions");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "CustomerInvoices");

            migrationBuilder.DropTable(
                name: "CustomerReceipts");

            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "SupplierInvoices");

            migrationBuilder.DropTable(
                name: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "TaxCodes");

            migrationBuilder.DropTable(
                name: "TaxReturns");

            migrationBuilder.DropTable(
                name: "CustomerAccounts");

            migrationBuilder.DropTable(
                name: "SourceEvidences");

            migrationBuilder.DropTable(
                name: "SupplierAccounts");

            migrationBuilder.DropColumn(
                name: "PostingProfileCode",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "FinancialDocuments");
        }
    }
}
