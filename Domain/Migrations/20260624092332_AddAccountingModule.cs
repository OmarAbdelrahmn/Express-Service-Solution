using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ParentAccountId = table.Column<int>(type: "int", nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingAccounts_AccountingAccounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "AccountingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountingAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountingAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountingNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashSalaryHandoverBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSalaryHandoverBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashSalaryHandoverBatches_Housings_HousingId",
                        column: x => x.HousingId,
                        principalTable: "Housings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CheckCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheckNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayeeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    CompanyNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    TemplateType = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillImports_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyProfitSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    GrossIncome = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetIncome = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RiderSalaryExpense = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CompanyExpenses = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DeductionsRecovered = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Profit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfitSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyProfitSnapshots_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    RiderId = table.Column<int>(type: "int", nullable: true),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: true),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostCenters_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CostCenters_Employees_EmployeeIqamaNo",
                        column: x => x.EmployeeIqamaNo,
                        principalTable: "Employees",
                        principalColumn: "IqamaNo");
                    table.ForeignKey(
                        name: "FK_CostCenters_Housings_HousingId",
                        column: x => x.HousingId,
                        principalTable: "Housings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CostCenters_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CostCenters_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CostCenters_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AssetType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PurchaseCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SalvageValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssets_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FixedAssets_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    ReversedEntryId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RiderBonusRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    MinimumAcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    BonusAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderBonusRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderBonusRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RiderFinancialItemTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderFinancialItemTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiderLoans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FirstDeductionYear = table.Column<int>(type: "int", nullable: false),
                    FirstDeductionMonth = table.Column<int>(type: "int", nullable: false),
                    InstallmentCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderLoans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderLoans_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderMonthlySalaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalBonuses = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAllowances = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IbanSnapshot = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderMonthlySalaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderMonthlySalaries_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderSalaryPaymentBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentCount = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderSalaryPaymentBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    PurchaseInvoiceId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPayables_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TreasuryAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StatementBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SystemBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliations_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsReconciled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillDailyMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillImportId = table.Column<int>(type: "int", nullable: false),
                    SourceRiderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: true),
                    MetricDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    RejectedOrders = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillDailyMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillDailyMetrics_CompanyBillImports_CompanyBillImportId",
                        column: x => x.CompanyBillImportId,
                        principalTable: "CompanyBillImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBillDailyMetrics_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillResolutionIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillImportId = table.Column<int>(type: "int", nullable: false),
                    IssueType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: true),
                    SourceRiderId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillResolutionIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillResolutionIssues_CompanyBillImports_CompanyBillImportId",
                        column: x => x.CompanyBillImportId,
                        principalTable: "CompanyBillImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillSheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillImportId = table.Column<int>(type: "int", nullable: false),
                    SheetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ColumnCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillSheets_CompanyBillImports_CompanyBillImportId",
                        column: x => x.CompanyBillImportId,
                        principalTable: "CompanyBillImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyReceivables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    CompanyBillImportId = table.Column<int>(type: "int", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CollectedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PendingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyReceivables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyReceivables_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyReceivables_CompanyBillImports_CompanyBillImportId",
                        column: x => x.CompanyBillImportId,
                        principalTable: "CompanyBillImports",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RiderEarnings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillImportId = table.Column<int>(type: "int", nullable: true),
                    CompanyBillRiderSummaryId = table.Column<int>(type: "int", nullable: true),
                    CompanyBillTransactionLineId = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    OriginalRiderId = table.Column<int>(type: "int", nullable: true),
                    PaidRiderId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    RejectedOrders = table.Column<int>(type: "int", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DistanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SalaryAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderEarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderEarnings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RiderEarnings_CompanyBillImports_CompanyBillImportId",
                        column: x => x.CompanyBillImportId,
                        principalTable: "CompanyBillImports",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RiderEarnings_RiderDetails_OriginalRiderId",
                        column: x => x.OriginalRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderEarnings_RiderDetails_PaidRiderId",
                        column: x => x.PaidRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyExpenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyExpenseCategoryId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    RiderId = table.Column<int>(type: "int", nullable: true),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ExpenseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyExpenses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyExpenses_CompanyExpenseCategories_CompanyExpenseCategoryId",
                        column: x => x.CompanyExpenseCategoryId,
                        principalTable: "CompanyExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyExpenses_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyExpenses_Housings_HousingId",
                        column: x => x.HousingId,
                        principalTable: "Housings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyExpenses_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyExpenses_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetDepreciationEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FixedAssetId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetDepreciationEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetDepreciationEntries_FixedAssets_FixedAssetId",
                        column: x => x.FixedAssetId,
                        principalTable: "FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JournalEntryId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    RiderId = table.Column<int>(type: "int", nullable: true),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: true),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_AccountingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderBonusAwards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderBonusRuleId = table.Column<int>(type: "int", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    AcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsManualOverride = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderBonusAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderBonusAwards_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RiderBonusAwards_RiderBonusRules_RiderBonusRuleId",
                        column: x => x.RiderBonusRuleId,
                        principalTable: "RiderBonusRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderBonusAwards_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderFinancialItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderFinancialItemTypeId = table.Column<int>(type: "int", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeIqamaNo = table.Column<long>(type: "bigint", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsWaived = table.Column<bool>(type: "bit", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderFinancialItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_Housings_HousingId",
                        column: x => x.HousingId,
                        principalTable: "Housings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_RiderFinancialItemTypes_RiderFinancialItemTypeId",
                        column: x => x.RiderFinancialItemTypeId,
                        principalTable: "RiderFinancialItemTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_Vehicles_VehicleNumber",
                        column: x => x.VehicleNumber,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderLoanInstallments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderLoanId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderLoanInstallments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderLoanInstallments_RiderLoans_RiderLoanId",
                        column: x => x.RiderLoanId,
                        principalTable: "RiderLoans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashSalaryHandoverLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CashSalaryHandoverBatchId = table.Column<int>(type: "int", nullable: false),
                    RiderMonthlySalaryId = table.Column<int>(type: "int", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MemberNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSalaryHandoverLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashSalaryHandoverLines_CashSalaryHandoverBatches_CashSalaryHandoverBatchId",
                        column: x => x.CashSalaryHandoverBatchId,
                        principalTable: "CashSalaryHandoverBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSalaryHandoverLines_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSalaryHandoverLines_RiderMonthlySalaries_RiderMonthlySalaryId",
                        column: x => x.RiderMonthlySalaryId,
                        principalTable: "RiderMonthlySalaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderMonthlySalaryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderMonthlySalaryId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    IsEditable = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderMonthlySalaryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderMonthlySalaryLines_RiderMonthlySalaries_RiderMonthlySalaryId",
                        column: x => x.RiderMonthlySalaryId,
                        principalTable: "RiderMonthlySalaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderSalaryPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderSalaryPaymentBatchId = table.Column<int>(type: "int", nullable: false),
                    RiderMonthlySalaryId = table.Column<int>(type: "int", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IbanSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BankNameSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderSalaryPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderSalaryPayments_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderSalaryPayments_RiderMonthlySalaries_RiderMonthlySalaryId",
                        column: x => x.RiderMonthlySalaryId,
                        principalTable: "RiderMonthlySalaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderSalaryPayments_RiderSalaryPaymentBatches_RiderSalaryPaymentBatchId",
                        column: x => x.RiderSalaryPaymentBatchId,
                        principalTable: "RiderSalaryPaymentBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierPayableId = table.Column<int>(type: "int", nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaidBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_SupplierPayables_SupplierPayableId",
                        column: x => x.SupplierPayableId,
                        principalTable: "SupplierPayables",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillRawRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillSheetId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    IsHeader = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillRawRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillRawRows_CompanyBillSheets_CompanyBillSheetId",
                        column: x => x.CompanyBillSheetId,
                        principalTable: "CompanyBillSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillRiderSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillImportId = table.Column<int>(type: "int", nullable: false),
                    CompanyBillSheetId = table.Column<int>(type: "int", nullable: true),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceRiderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceRiderName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    OriginalRiderId = table.Column<int>(type: "int", nullable: true),
                    PaidRiderId = table.Column<int>(type: "int", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "int", nullable: false),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcceptedOrders = table.Column<int>(type: "int", nullable: false),
                    RejectedOrders = table.Column<int>(type: "int", nullable: false),
                    DistanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BasicPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BonusAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PenaltyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RiderBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WorkingHours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WorkingDays = table.Column<int>(type: "int", nullable: false),
                    ValidityStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidityReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillRiderSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillRiderSummaries_CompanyBillImports_CompanyBillImportId",
                        column: x => x.CompanyBillImportId,
                        principalTable: "CompanyBillImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBillRiderSummaries_CompanyBillSheets_CompanyBillSheetId",
                        column: x => x.CompanyBillSheetId,
                        principalTable: "CompanyBillSheets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyBillRiderSummaries_RiderDetails_OriginalRiderId",
                        column: x => x.OriginalRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBillRiderSummaries_RiderDetails_PaidRiderId",
                        column: x => x.PaidRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillTransactionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillImportId = table.Column<int>(type: "int", nullable: false),
                    CompanyBillSheetId = table.Column<int>(type: "int", nullable: true),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceRiderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceRiderName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OriginalRiderId = table.Column<int>(type: "int", nullable: true),
                    PaidRiderId = table.Column<int>(type: "int", nullable: true),
                    ResolutionStatus = table.Column<int>(type: "int", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    WorkId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FeeType = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    AmountDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DistanceKm = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TicketId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ViolationId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ViolationType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PunishmentMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FaceVerificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FaceVerificationResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillTransactionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillTransactionLines_CompanyBillImports_CompanyBillImportId",
                        column: x => x.CompanyBillImportId,
                        principalTable: "CompanyBillImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBillTransactionLines_CompanyBillSheets_CompanyBillSheetId",
                        column: x => x.CompanyBillSheetId,
                        principalTable: "CompanyBillSheets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyBillTransactionLines_RiderDetails_OriginalRiderId",
                        column: x => x.OriginalRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBillTransactionLines_RiderDetails_PaidRiderId",
                        column: x => x.PaidRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyPaymentReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyReceivableId = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BankAccount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceivedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyPaymentReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyPaymentReceipts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompanyPaymentReceipts_CompanyReceivables_CompanyReceivableId",
                        column: x => x.CompanyReceivableId,
                        principalTable: "CompanyReceivables",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyBillRawCells",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyBillRawRowId = table.Column<int>(type: "int", nullable: false),
                    ColumnNumber = table.Column<int>(type: "int", nullable: false),
                    Header = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OriginalValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBillRawCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyBillRawCells_CompanyBillRawRows_CompanyBillRawRowId",
                        column: x => x.CompanyBillRawRowId,
                        principalTable: "CompanyBillRawRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AccountingAccounts",
                columns: new[] { "Id", "Code", "IsActive", "IsSystem", "Name", "ParentAccountId", "Type" },
                values: new object[,]
                {
                    { 1, "1000", true, true, "Cash and Bank", null, 1 },
                    { 2, "1100", true, true, "Company Receivables", null, 1 },
                    { 3, "1200", true, true, "Rider Receivables", null, 1 },
                    { 4, "1210", true, true, "Loan Receivables", null, 1 },
                    { 5, "1220", true, true, "Traffic Violation Receivables", null, 1 },
                    { 6, "1230", true, true, "Iqama and Government Fee Receivables", null, 1 },
                    { 7, "2000", true, true, "Supplier Payables", null, 2 },
                    { 8, "2100", true, true, "Rider Payables", null, 2 },
                    { 9, "4000", true, true, "Company Revenue", null, 4 },
                    { 10, "5000", true, true, "Rider Salary Expense", null, 5 },
                    { 11, "5100", true, true, "Petrol Expense", null, 5 },
                    { 12, "5200", true, true, "Spare Parts Expense", null, 5 },
                    { 13, "5300", true, true, "Accessory Expense", null, 5 },
                    { 14, "5400", true, true, "Housing Expense", null, 5 },
                    { 15, "5500", true, true, "Vehicle Expense", null, 5 },
                    { 16, "5600", true, true, "Government Fees Expense", null, 5 },
                    { 17, "5700", true, true, "Manual Adjustments", null, 5 }
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "IsDefault", "IsDeleted", "Name", "NormalizedName" },
                values: new object[] { "A2C96C5D-F502-47TF-EE95-ABVN14A3CA22", "A2C75EE9-DB35-480D-9F9F-18D2E499B004", false, false, "Accountant", "ACCOUNTANT" });

            migrationBuilder.InsertData(
                table: "CompanyExpenseCategories",
                columns: new[] { "Id", "Code", "IsActive", "IsSystem", "Name" },
                values: new object[,]
                {
                    { 1, "HOUSING", true, true, "Housing" },
                    { 2, "VEHICLE", true, true, "Vehicle" },
                    { 3, "PETROL", true, true, "Petrol" },
                    { 4, "SPARE_PARTS", true, true, "Spare Parts" },
                    { 5, "ACCESSORIES", true, true, "Accessories" },
                    { 6, "GOVERNMENT_FEES", true, true, "Government Fees" },
                    { 7, "TICKETS", true, true, "Plane Tickets" },
                    { 8, "TRAFFIC_VIOLATIONS", true, true, "Traffic Violations" },
                    { 9, "SUPPLIER_BILLS", true, true, "Supplier Bills" },
                    { 10, "BANK_FEES", true, true, "Bank Fees" },
                    { 11, "MANUAL", true, true, "Manual Expense" }
                });

            migrationBuilder.InsertData(
                table: "RiderFinancialItemTypes",
                columns: new[] { "Id", "Category", "Code", "IsActive", "IsSystem", "Name" },
                values: new object[,]
                {
                    { 1, 2, "WALLET_ADVANCE", true, true, "Wallet Advance" },
                    { 2, 2, "LOAN", true, true, "Loan Installment" },
                    { 3, 2, "TRAFFIC_VIOLATION", true, true, "Traffic Violation" },
                    { 4, 2, "IQAMA_FEE", true, true, "Iqama Fee" },
                    { 5, 2, "LABOR_OFFICE_FEE", true, true, "Labor Office Fee" },
                    { 6, 2, "PLANE_TICKET", true, true, "Plane Ticket" },
                    { 7, 3, "HOUSING_ALLOWANCE", true, true, "Housing Allowance" },
                    { 8, 2, "ACCESSORY_CHARGE", true, true, "Accessory Charge" },
                    { 9, 2, "SPARE_PART_CHARGE", true, true, "Spare Part Charge" },
                    { 10, 2, "PETROL_CHARGE", true, true, "Petrol Charge" },
                    { 11, 1, "MANUAL_BONUS", true, true, "Manual Bonus" },
                    { 12, 2, "MANUAL_DEDUCTION", true, true, "Manual Deduction" },
                    { 13, 6, "OTHER", true, true, "Other" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingAccounts_Code",
                table: "AccountingAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingAccounts_ParentAccountId",
                table: "AccountingAccounts",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_Year_Month",
                table: "AccountingPeriods",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciationEntries_FixedAssetId",
                table: "AssetDepreciationEntries",
                column: "FixedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_BankAccountId",
                table: "BankReconciliations",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId",
                table: "BankTransactions",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSalaryHandoverBatches_HousingId",
                table: "CashSalaryHandoverBatches",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSalaryHandoverBatches_Year_Month_HousingId_Status",
                table: "CashSalaryHandoverBatches",
                columns: new[] { "Year", "Month", "HousingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSalaryHandoverLines_CashSalaryHandoverBatchId",
                table: "CashSalaryHandoverLines",
                column: "CashSalaryHandoverBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSalaryHandoverLines_RiderId",
                table: "CashSalaryHandoverLines",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSalaryHandoverLines_RiderMonthlySalaryId",
                table: "CashSalaryHandoverLines",
                column: "RiderMonthlySalaryId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillDailyMetrics_CompanyBillImportId_SourceRiderId_MetricDate",
                table: "CompanyBillDailyMetrics",
                columns: new[] { "CompanyBillImportId", "SourceRiderId", "MetricDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillDailyMetrics_RiderId",
                table: "CompanyBillDailyMetrics",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillImports_CompanyId",
                table: "CompanyBillImports",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillImports_Status",
                table: "CompanyBillImports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillImports_Year_Month_CompanyId_TemplateType",
                table: "CompanyBillImports",
                columns: new[] { "Year", "Month", "CompanyId", "TemplateType" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillRawCells_CompanyBillRawRowId_ColumnNumber",
                table: "CompanyBillRawCells",
                columns: new[] { "CompanyBillRawRowId", "ColumnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillRawRows_CompanyBillSheetId_RowNumber",
                table: "CompanyBillRawRows",
                columns: new[] { "CompanyBillSheetId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillResolutionIssues_CompanyBillImportId",
                table: "CompanyBillResolutionIssues",
                column: "CompanyBillImportId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillRiderSummaries_CompanyBillImportId_SourceRiderId",
                table: "CompanyBillRiderSummaries",
                columns: new[] { "CompanyBillImportId", "SourceRiderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillRiderSummaries_CompanyBillSheetId",
                table: "CompanyBillRiderSummaries",
                column: "CompanyBillSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillRiderSummaries_OriginalRiderId",
                table: "CompanyBillRiderSummaries",
                column: "OriginalRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillRiderSummaries_PaidRiderId",
                table: "CompanyBillRiderSummaries",
                column: "PaidRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillRiderSummaries_ResolutionStatus",
                table: "CompanyBillRiderSummaries",
                column: "ResolutionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillSheets_CompanyBillImportId_Role",
                table: "CompanyBillSheets",
                columns: new[] { "CompanyBillImportId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillTransactionLines_CompanyBillImportId_SourceRiderId",
                table: "CompanyBillTransactionLines",
                columns: new[] { "CompanyBillImportId", "SourceRiderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillTransactionLines_CompanyBillSheetId",
                table: "CompanyBillTransactionLines",
                column: "CompanyBillSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillTransactionLines_OriginalRiderId",
                table: "CompanyBillTransactionLines",
                column: "OriginalRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillTransactionLines_PaidRiderId",
                table: "CompanyBillTransactionLines",
                column: "PaidRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBillTransactionLines_ServiceDate",
                table: "CompanyBillTransactionLines",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenseCategories_Code",
                table: "CompanyExpenseCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_CompanyExpenseCategoryId",
                table: "CompanyExpenses",
                column: "CompanyExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_CompanyId",
                table: "CompanyExpenses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_CostCenterId",
                table: "CompanyExpenses",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_ExpenseDate_CompanyId_CompanyExpenseCategoryId",
                table: "CompanyExpenses",
                columns: new[] { "ExpenseDate", "CompanyId", "CompanyExpenseCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_HousingId",
                table: "CompanyExpenses",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_RiderId",
                table: "CompanyExpenses",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyExpenses_VehicleNumber",
                table: "CompanyExpenses",
                column: "VehicleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPaymentReceipts_CompanyId",
                table: "CompanyPaymentReceipts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPaymentReceipts_CompanyReceivableId",
                table: "CompanyPaymentReceipts",
                column: "CompanyReceivableId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfitSnapshots_CompanyId",
                table: "CompanyProfitSnapshots",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfitSnapshots_Year_Month_CompanyId",
                table: "CompanyProfitSnapshots",
                columns: new[] { "Year", "Month", "CompanyId" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyReceivables_CompanyBillImportId",
                table: "CompanyReceivables",
                column: "CompanyBillImportId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyReceivables_CompanyId",
                table: "CompanyReceivables",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyReceivables_Year_Month_CompanyId_Status",
                table: "CompanyReceivables",
                columns: new[] { "Year", "Month", "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_Code",
                table: "CostCenters",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_CompanyId",
                table: "CostCenters",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_EmployeeIqamaNo",
                table: "CostCenters",
                column: "EmployeeIqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_HousingId",
                table: "CostCenters",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_RiderId",
                table: "CostCenters",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_SupplierId",
                table: "CostCenters",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_Type_CompanyId_RiderId_HousingId",
                table: "CostCenters",
                columns: new[] { "Type", "CompanyId", "RiderId", "HousingId" });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_VehicleNumber",
                table: "CostCenters",
                column: "VehicleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_AssetCode",
                table: "FixedAssets",
                column: "AssetCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CompanyId",
                table: "FixedAssets",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_VehicleNumber",
                table: "FixedAssets",
                column: "VehicleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryDate",
                table: "JournalEntries",
                column: "EntryDate");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryNumber",
                table: "JournalEntries",
                column: "EntryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_SourceType_SourceId",
                table: "JournalEntries",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_AccountId",
                table: "JournalEntryLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CompanyId_RiderId_HousingId",
                table: "JournalEntryLines",
                columns: new[] { "CompanyId", "RiderId", "HousingId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CostCenterId",
                table: "JournalEntryLines",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_JournalEntryId",
                table: "JournalEntryLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierId",
                table: "PurchaseInvoices",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderBonusAwards_CompanyId",
                table: "RiderBonusAwards",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderBonusAwards_RiderBonusRuleId",
                table: "RiderBonusAwards",
                column: "RiderBonusRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderBonusAwards_RiderId",
                table: "RiderBonusAwards",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderBonusRules_CompanyId_MinimumAcceptedOrders_IsActive",
                table: "RiderBonusRules",
                columns: new[] { "CompanyId", "MinimumAcceptedOrders", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderEarnings_CompanyBillImportId",
                table: "RiderEarnings",
                column: "CompanyBillImportId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderEarnings_CompanyId_Year_Month",
                table: "RiderEarnings",
                columns: new[] { "CompanyId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderEarnings_OriginalRiderId",
                table: "RiderEarnings",
                column: "OriginalRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderEarnings_PaidRiderId",
                table: "RiderEarnings",
                column: "PaidRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderEarnings_Year_Month_PaidRiderId",
                table: "RiderEarnings",
                columns: new[] { "Year", "Month", "PaidRiderId" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_CompanyId",
                table: "RiderFinancialItems",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_HousingId",
                table: "RiderFinancialItems",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_RiderFinancialItemTypeId",
                table: "RiderFinancialItems",
                column: "RiderFinancialItemTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_RiderId",
                table: "RiderFinancialItems",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_VehicleNumber",
                table: "RiderFinancialItems",
                column: "VehicleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_Year_Month_RiderId",
                table: "RiderFinancialItems",
                columns: new[] { "Year", "Month", "RiderId" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItemTypes_Code",
                table: "RiderFinancialItemTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderLoanInstallments_RiderLoanId",
                table: "RiderLoanInstallments",
                column: "RiderLoanId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderLoans_RiderId",
                table: "RiderLoans",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderMonthlySalaries_RiderId_Year_Month",
                table: "RiderMonthlySalaries",
                columns: new[] { "RiderId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderMonthlySalaries_Year_Month_PaymentMethod_Status",
                table: "RiderMonthlySalaries",
                columns: new[] { "Year", "Month", "PaymentMethod", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderMonthlySalaryLines_RiderMonthlySalaryId",
                table: "RiderMonthlySalaryLines",
                column: "RiderMonthlySalaryId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderMonthlySalaryLines_SourceType_SourceId",
                table: "RiderMonthlySalaryLines",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderSalaryPaymentBatches_Year_Month_PaymentMethod_Status",
                table: "RiderSalaryPaymentBatches",
                columns: new[] { "Year", "Month", "PaymentMethod", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderSalaryPayments_RiderId",
                table: "RiderSalaryPayments",
                column: "RiderId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderSalaryPayments_RiderMonthlySalaryId",
                table: "RiderSalaryPayments",
                column: "RiderMonthlySalaryId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderSalaryPayments_RiderSalaryPaymentBatchId",
                table: "RiderSalaryPayments",
                column: "RiderSalaryPaymentBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayables_SupplierId",
                table: "SupplierPayables",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierPayableId",
                table: "SupplierPayments",
                column: "SupplierPayableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingAttachments");

            migrationBuilder.DropTable(
                name: "AccountingAuditLogs");

            migrationBuilder.DropTable(
                name: "AccountingNotes");

            migrationBuilder.DropTable(
                name: "AccountingPeriods");

            migrationBuilder.DropTable(
                name: "AssetDepreciationEntries");

            migrationBuilder.DropTable(
                name: "BankReconciliations");

            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropTable(
                name: "CashSalaryHandoverLines");

            migrationBuilder.DropTable(
                name: "CheckCycles");

            migrationBuilder.DropTable(
                name: "CompanyBillDailyMetrics");

            migrationBuilder.DropTable(
                name: "CompanyBillRawCells");

            migrationBuilder.DropTable(
                name: "CompanyBillResolutionIssues");

            migrationBuilder.DropTable(
                name: "CompanyBillRiderSummaries");

            migrationBuilder.DropTable(
                name: "CompanyBillTransactionLines");

            migrationBuilder.DropTable(
                name: "CompanyExpenses");

            migrationBuilder.DropTable(
                name: "CompanyPaymentReceipts");

            migrationBuilder.DropTable(
                name: "CompanyProfitSnapshots");

            migrationBuilder.DropTable(
                name: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");

            migrationBuilder.DropTable(
                name: "RiderBonusAwards");

            migrationBuilder.DropTable(
                name: "RiderEarnings");

            migrationBuilder.DropTable(
                name: "RiderFinancialItems");

            migrationBuilder.DropTable(
                name: "RiderLoanInstallments");

            migrationBuilder.DropTable(
                name: "RiderMonthlySalaryLines");

            migrationBuilder.DropTable(
                name: "RiderSalaryPayments");

            migrationBuilder.DropTable(
                name: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "TreasuryAccounts");

            migrationBuilder.DropTable(
                name: "FixedAssets");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "CashSalaryHandoverBatches");

            migrationBuilder.DropTable(
                name: "CompanyBillRawRows");

            migrationBuilder.DropTable(
                name: "CompanyExpenseCategories");

            migrationBuilder.DropTable(
                name: "CompanyReceivables");

            migrationBuilder.DropTable(
                name: "AccountingAccounts");

            migrationBuilder.DropTable(
                name: "CostCenters");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "RiderBonusRules");

            migrationBuilder.DropTable(
                name: "RiderFinancialItemTypes");

            migrationBuilder.DropTable(
                name: "RiderLoans");

            migrationBuilder.DropTable(
                name: "RiderMonthlySalaries");

            migrationBuilder.DropTable(
                name: "RiderSalaryPaymentBatches");

            migrationBuilder.DropTable(
                name: "SupplierPayables");

            migrationBuilder.DropTable(
                name: "CompanyBillSheets");

            migrationBuilder.DropTable(
                name: "CompanyBillImports");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "A2C96C5D-F502-47TF-EE95-ABVN14A3CA22");

        }
    }
}
