using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingSafetyImportsCompensationPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountingOutboxMessages_ProcessedAt_OccurredAt",
                table: "AccountingOutboxMessages");

            migrationBuilder.AddColumn<decimal>(
                name: "BaseCredit",
                table: "JournalLines",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseDebit",
                table: "JournalLines",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                table: "FiscalPeriods",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PayrollLocked",
                table: "FiscalPeriods",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReopenReason",
                table: "FiscalPeriods",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReopenedAt",
                table: "FiscalPeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenedBy",
                table: "FiscalPeriods",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TaxLocked",
                table: "FiscalPeriods",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BaseCurrencyCode",
                table: "FinancialDocuments",
                type: "nchar(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "FinancialDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "ExchangeRateId",
                table: "FinancialDocuments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "FinancialDocuments",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RoundingTraceJson",
                table: "FinancialDocuments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "BaseCredit",
                table: "FinancialDocumentLines",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseDebit",
                table: "FinancialDocumentLines",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "AccountingOutboxMessages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadLetteredAt",
                table: "AccountingOutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "AccountingOutboxMessages",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "AccountingOutboxMessages",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "AccountingOutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "AccountingOutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "AccountingOutboxMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsCashEquivalent",
                table: "AccountingAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE jl
                SET [BaseDebit] = jl.[Debit], [BaseCredit] = jl.[Credit]
                FROM [JournalLines] jl;

                UPDATE fdl
                SET [BaseDebit] = fdl.[Debit], [BaseCredit] = fdl.[Credit]
                FROM [FinancialDocumentLines] fdl;

                UPDATE fd
                SET [BaseCurrencyCode] = le.[BaseCurrencyCode],
                    [CorrelationId] = REPLACE(CONVERT(varchar(36), fd.[Id]), '-', ''),
                    [RequestHash] = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT(CONVERT(varchar(36), fd.[Id]), '|', fd.[IdempotencyKey])), 2),
                    [RoundingTraceJson] = '{}'
                FROM [FinancialDocuments] fd
                JOIN [LegalEntities] le ON le.[Id] = fd.[LegalEntityId];

                UPDATE [AccountingOutboxMessages]
                SET [CorrelationId] = REPLACE(CONVERT(varchar(36), NEWID()), '-', '')
                WHERE [CorrelationId] = '';
                """);

            migrationBuilder.CreateTable(
                name: "AccountingAuditChainHeads",
                columns: table => new
                {
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    LastHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastEventId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingAuditChainHeads", x => x.LegalEntityId);
                    table.ForeignKey(
                        name: "FK_AccountingAuditChainHeads_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountingStoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PlaintextLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StorageLocator = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RetainUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingStoredFiles", x => x.Id);
                    table.CheckConstraint("CK_AccountingStoredFiles_Length", "[PlaintextLength] >= 0");
                    table.ForeignKey(
                        name: "FK_AccountingStoredFiles_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompensationPolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "int", nullable: false),
                    WorkerCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationPolicyVersions", x => x.Id);
                    table.CheckConstraint("CK_CompensationPolicyVersions_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_CompensationPolicyVersions_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompensationPolicyVersions_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformImportTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdapterKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SchemaFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportTemplates", x => x.Id);
                    table.CheckConstraint("CK_PlatformImportTemplates_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_PlatformImportTemplates_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformImportTemplates_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformWorkerIdentities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "int", nullable: false),
                    ExternalWorkerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsSubstitution = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformWorkerIdentities", x => x.Id);
                    table.CheckConstraint("CK_PlatformWorkerIdentities_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_PlatformWorkerIdentities_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformWorkerIdentities_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderFinancialItemTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    LedgerAccountId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderFinancialItemTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderFinancialItemTypes_AccountingAccounts_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalTable: "AccountingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderFinancialItemTypes_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    AppliedDeductions = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    CarriedDeductions = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    AccrualFinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPayrollRuns", x => x.Id);
                    table.CheckConstraint("CK_RiderPayrollRuns_Dates", "[PeriodEnd] >= [PeriodStart]");
                    table.ForeignKey(
                        name: "FK_RiderPayrollRuns_FinancialDocuments_AccrualFinancialDocumentId",
                        column: x => x.AccrualFinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollRuns_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompensationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompensationPolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Template = table.Column<int>(type: "int", nullable: false),
                    ComponentType = table.Column<int>(type: "int", nullable: false),
                    MetricCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConditionMetricCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ConditionOperator = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    ConditionValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    LowerBound = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    UpperBound = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    BelowRate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    AboveRate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    FixedAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    BaseAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    TargetComponentCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ExclusiveGroup = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StackingMode = table.Column<int>(type: "int", nullable: false),
                    RoundingScale = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationRules", x => x.Id);
                    table.CheckConstraint("CK_CompensationRules_Rounding", "[RoundingScale] >= 0 AND [RoundingScale] <= 4");
                    table.ForeignKey(
                        name: "FK_CompensationRules_CompensationPolicyVersions_CompensationPolicyVersionId",
                        column: x => x.CompensationPolicyVersionId,
                        principalTable: "CompensationPolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "int", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ParserVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceControlTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    NormalizedControlTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SupersedesBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededByBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportBatches", x => x.Id);
                    table.CheckConstraint("CK_PlatformImportBatches_Dates", "[PeriodEnd] >= [PeriodStart]");
                    table.ForeignKey(
                        name: "FK_PlatformImportBatches_AccountingStoredFiles_StoredFileId",
                        column: x => x.StoredFileId,
                        principalTable: "AccountingStoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformImportBatches_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformImportBatches_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformImportBatches_PlatformImportBatches_SupersedesBatchId",
                        column: x => x.SupersedesBatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformImportBatches_PlatformImportTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "PlatformImportTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderFinancialItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    RiderFinancialItemTypeId = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeductionStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OriginalAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    InstallmentCount = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EvidenceFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderFinancialItems", x => x.Id);
                    table.CheckConstraint("CK_RiderFinancialItems_Amounts", "[OriginalAmount] > 0 AND [OutstandingAmount] >= 0 AND [OutstandingAmount] <= [OriginalAmount]");
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_AccountingStoredFiles_EvidenceFileId",
                        column: x => x.EvidenceFileId,
                        principalTable: "AccountingStoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderFinancialItems_RiderFinancialItemTypes_RiderFinancialItemTypeId",
                        column: x => x.RiderFinancialItemTypeId,
                        principalTable: "RiderFinancialItemTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPaymentBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    RiderPayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExportFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentFinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPaymentBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderPaymentBatches_AccountingStoredFiles_ExportFileId",
                        column: x => x.ExportFileId,
                        principalTable: "AccountingStoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPaymentBatches_FinancialDocuments_PaymentFinancialDocumentId",
                        column: x => x.PaymentFinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPaymentBatches_RiderPayrollRuns_RiderPayrollRunId",
                        column: x => x.RiderPayrollRunId,
                        principalTable: "RiderPayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPayrollCarryForwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    CreatedFromPayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPayrollCarryForwards", x => x.Id);
                    table.CheckConstraint("CK_RiderPayrollCarryForwards_Amounts", "[OriginalAmount] > 0 AND [OutstandingAmount] >= 0 AND [OutstandingAmount] <= [OriginalAmount]");
                    table.ForeignKey(
                        name: "FK_RiderPayrollCarryForwards_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollCarryForwards_RiderPayrollRuns_CreatedFromPayrollRunId",
                        column: x => x.CreatedFromPayrollRunId,
                        principalTable: "RiderPayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPayrollLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderPayrollRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    AppliedDeductions = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    CarriedDeductions = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    IsHeld = table.Column<bool>(type: "bit", nullable: false),
                    HoldReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPayrollLines", x => x.Id);
                    table.CheckConstraint("CK_RiderPayrollLines_Amounts", "[GrossEarnings] >= 0 AND [AppliedDeductions] >= 0 AND [CarriedDeductions] >= 0 AND [NetPay] >= 0");
                    table.ForeignKey(
                        name: "FK_RiderPayrollLines_RiderPayrollRuns_RiderPayrollRunId",
                        column: x => x.RiderPayrollRunId,
                        principalTable: "RiderPayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformImportSheets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetIndex = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    MaxRowNumber = table.Column<int>(type: "int", nullable: false),
                    MaxColumnNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformImportSheets_PlatformImportBatches_PlatformImportBatchId",
                        column: x => x.PlatformImportBatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderFinancialInstallments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderFinancialItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    IsSettled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderFinancialInstallments", x => x.Id);
                    table.CheckConstraint("CK_RiderFinancialInstallments_Amounts", "[ScheduledAmount] > 0 AND [AppliedAmount] >= 0 AND [AppliedAmount] <= [ScheduledAmount]");
                    table.ForeignKey(
                        name: "FK_RiderFinancialInstallments_RiderFinancialItems_RiderFinancialItemId",
                        column: x => x.RiderFinancialItemId,
                        principalTable: "RiderFinancialItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPaymentBatchLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderPaymentBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderPayrollLineId = table.Column<long>(type: "bigint", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    IbanSnapshot = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    HousingId = table.Column<int>(type: "int", nullable: true),
                    IsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PaymentFinancialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPaymentBatchLines", x => x.Id);
                    table.CheckConstraint("CK_RiderPaymentBatchLines_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_RiderPaymentBatchLines_FinancialDocuments_PaymentFinancialDocumentId",
                        column: x => x.PaymentFinancialDocumentId,
                        principalTable: "FinancialDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPaymentBatchLines_RiderPaymentBatches_RiderPaymentBatchId",
                        column: x => x.RiderPaymentBatchId,
                        principalTable: "RiderPaymentBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPaymentBatchLines_RiderPayrollLines_RiderPayrollLineId",
                        column: x => x.RiderPayrollLineId,
                        principalTable: "RiderPayrollLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPayrollAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderPayrollLineId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EvidenceFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPayrollAdjustments", x => x.Id);
                    table.CheckConstraint("CK_RiderPayrollAdjustments_NonZero", "[Amount] <> 0");
                    table.ForeignKey(
                        name: "FK_RiderPayrollAdjustments_AccountingStoredFiles_EvidenceFileId",
                        column: x => x.EvidenceFileId,
                        principalTable: "AccountingStoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollAdjustments_RiderPayrollLines_RiderPayrollLineId",
                        column: x => x.RiderPayrollLineId,
                        principalTable: "RiderPayrollLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderPayrollComponents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderPayrollLineId = table.Column<long>(type: "bigint", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "int", nullable: true),
                    CompensationPolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompensationRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RiderFinancialItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RiderPayrollCarryForwardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    ComponentType = table.Column<int>(type: "int", nullable: false),
                    ComponentCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    CalculationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPayrollComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderPayrollComponents_CompensationPolicyVersions_CompensationPolicyVersionId",
                        column: x => x.CompensationPolicyVersionId,
                        principalTable: "CompensationPolicyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollComponents_CompensationRules_CompensationRuleId",
                        column: x => x.CompensationRuleId,
                        principalTable: "CompensationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollComponents_PlatformAccounts_PlatformAccountId",
                        column: x => x.PlatformAccountId,
                        principalTable: "PlatformAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollComponents_PlatformImportBatches_SourceImportBatchId",
                        column: x => x.SourceImportBatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollComponents_RiderFinancialItems_RiderFinancialItemId",
                        column: x => x.RiderFinancialItemId,
                        principalTable: "RiderFinancialItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollComponents_RiderPayrollCarryForwards_RiderPayrollCarryForwardId",
                        column: x => x.RiderPayrollCarryForwardId,
                        principalTable: "RiderPayrollCarryForwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderPayrollComponents_RiderPayrollLines_RiderPayrollLineId",
                        column: x => x.RiderPayrollLineId,
                        principalTable: "RiderPayrollLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformImportRawRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformImportSheetId = table.Column<long>(type: "bigint", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    RowHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportRawRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformImportRawRows_PlatformImportSheets_PlatformImportSheetId",
                        column: x => x.PlatformImportSheetId,
                        principalTable: "PlatformImportSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformImportIssues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRawRowId = table.Column<long>(type: "bigint", nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformImportIssues_PlatformImportBatches_PlatformImportBatchId",
                        column: x => x.PlatformImportBatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformImportIssues_PlatformImportRawRows_SourceRawRowId",
                        column: x => x.SourceRawRowId,
                        principalTable: "PlatformImportRawRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformImportRawCells",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformImportRawRowId = table.Column<long>(type: "bigint", nullable: false),
                    ColumnNumber = table.Column<int>(type: "int", nullable: false),
                    CellReference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DisplayValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Formula = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DataType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformImportRawCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformImportRawCells_PlatformImportRawRows_PlatformImportRawRowId",
                        column: x => x.PlatformImportRawRowId,
                        principalTable: "PlatformImportRawRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformNormalizedFacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PlatformAccountId = table.Column<int>(type: "int", nullable: false),
                    WorkerCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceRawRowId = table.Column<long>(type: "bigint", nullable: true),
                    RiderIqamaNo = table.Column<long>(type: "bigint", nullable: true),
                    ExternalWorkerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FactDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    MetricCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NumericValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    LineageJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformNormalizedFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformNormalizedFacts_PlatformImportBatches_PlatformImportBatchId",
                        column: x => x.PlatformImportBatchId,
                        principalTable: "PlatformImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformNormalizedFacts_PlatformImportRawRows_SourceRawRowId",
                        column: x => x.SourceRawRowId,
                        principalTable: "PlatformImportRawRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformFactOverrides",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlatformNormalizedFactId = table.Column<long>(type: "bigint", nullable: false),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformFactOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformFactOverrides_PlatformNormalizedFacts_PlatformNormalizedFactId",
                        column: x => x.PlatformNormalizedFactId,
                        principalTable: "PlatformNormalizedFacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutboxMessages_ProcessedAt_DeadLetteredAt_NextAttemptAt_LockedUntil_OccurredAt",
                table: "AccountingOutboxMessages",
                columns: new[] { "ProcessedAt", "DeadLetteredAt", "NextAttemptAt", "LockedUntil", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingStoredFiles_LegalEntityId_Sha256",
                table: "AccountingStoredFiles",
                columns: new[] { "LegalEntityId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPolicyVersions_LegalEntityId_PlatformAccountId_WorkerCategory_Code_Version",
                table: "CompensationPolicyVersions",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "WorkerCategory", "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationPolicyVersions_PlatformAccountId",
                table: "CompensationPolicyVersions",
                column: "PlatformAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationRules_CompensationPolicyVersionId_Code",
                table: "CompensationRules",
                columns: new[] { "CompensationPolicyVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformFactOverrides_PlatformNormalizedFactId",
                table: "PlatformFactOverrides",
                column: "PlatformNormalizedFactId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_ExternalReference",
                table: "PlatformImportBatches",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_StoredFileId",
                table: "PlatformImportBatches",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "StoredFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_PlatformAccountId",
                table: "PlatformImportBatches",
                column: "PlatformAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_StoredFileId",
                table: "PlatformImportBatches",
                column: "StoredFileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_SupersedesBatchId",
                table: "PlatformImportBatches",
                column: "SupersedesBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_TemplateId",
                table: "PlatformImportBatches",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportIssues_PlatformImportBatchId_Status_Severity",
                table: "PlatformImportIssues",
                columns: new[] { "PlatformImportBatchId", "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportIssues_SourceRawRowId",
                table: "PlatformImportIssues",
                column: "SourceRawRowId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportRawCells_PlatformImportRawRowId_ColumnNumber",
                table: "PlatformImportRawCells",
                columns: new[] { "PlatformImportRawRowId", "ColumnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportRawRows_PlatformImportSheetId_RowNumber",
                table: "PlatformImportRawRows",
                columns: new[] { "PlatformImportSheetId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportSheets_PlatformImportBatchId_SheetIndex",
                table: "PlatformImportSheets",
                columns: new[] { "PlatformImportBatchId", "SheetIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportTemplates_LegalEntityId_PlatformAccountId_Code_Version",
                table: "PlatformImportTemplates",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportTemplates_PlatformAccountId",
                table: "PlatformImportTemplates",
                column: "PlatformAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNormalizedFacts_PlatformImportBatchId_RiderIqamaNo_FactDate_MetricCode",
                table: "PlatformNormalizedFacts",
                columns: new[] { "PlatformImportBatchId", "RiderIqamaNo", "FactDate", "MetricCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformNormalizedFacts_SourceRawRowId",
                table: "PlatformNormalizedFacts",
                column: "SourceRawRowId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformWorkerIdentities_LegalEntityId_PlatformAccountId_ExternalWorkerId_EffectiveFrom",
                table: "PlatformWorkerIdentities",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "ExternalWorkerId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformWorkerIdentities_PlatformAccountId",
                table: "PlatformWorkerIdentities",
                column: "PlatformAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialInstallments_RiderFinancialItemId_Sequence",
                table: "RiderFinancialInstallments",
                columns: new[] { "RiderFinancialItemId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_EvidenceFileId",
                table: "RiderFinancialItems",
                column: "EvidenceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_LegalEntityId_Reference",
                table: "RiderFinancialItems",
                columns: new[] { "LegalEntityId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_LegalEntityId_RiderIqamaNo_Status",
                table: "RiderFinancialItems",
                columns: new[] { "LegalEntityId", "RiderIqamaNo", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItems_RiderFinancialItemTypeId",
                table: "RiderFinancialItems",
                column: "RiderFinancialItemTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItemTypes_LedgerAccountId",
                table: "RiderFinancialItemTypes",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderFinancialItemTypes_LegalEntityId_Code",
                table: "RiderFinancialItemTypes",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderPaymentBatches_ExportFileId",
                table: "RiderPaymentBatches",
                column: "ExportFileId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPaymentBatches_LegalEntityId_BatchNumber",
                table: "RiderPaymentBatches",
                columns: new[] { "LegalEntityId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderPaymentBatches_PaymentFinancialDocumentId",
                table: "RiderPaymentBatches",
                column: "PaymentFinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPaymentBatches_RiderPayrollRunId",
                table: "RiderPaymentBatches",
                column: "RiderPayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPaymentBatchLines_PaymentFinancialDocumentId",
                table: "RiderPaymentBatchLines",
                column: "PaymentFinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPaymentBatchLines_RiderPaymentBatchId_RiderPayrollLineId",
                table: "RiderPaymentBatchLines",
                columns: new[] { "RiderPaymentBatchId", "RiderPayrollLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderPaymentBatchLines_RiderPayrollLineId",
                table: "RiderPaymentBatchLines",
                column: "RiderPayrollLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollAdjustments_EvidenceFileId",
                table: "RiderPayrollAdjustments",
                column: "EvidenceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollAdjustments_RiderPayrollLineId",
                table: "RiderPayrollAdjustments",
                column: "RiderPayrollLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollCarryForwards_CreatedFromPayrollRunId",
                table: "RiderPayrollCarryForwards",
                column: "CreatedFromPayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollCarryForwards_LegalEntityId_RiderIqamaNo_Status",
                table: "RiderPayrollCarryForwards",
                columns: new[] { "LegalEntityId", "RiderIqamaNo", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollComponents_CompensationPolicyVersionId",
                table: "RiderPayrollComponents",
                column: "CompensationPolicyVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollComponents_CompensationRuleId",
                table: "RiderPayrollComponents",
                column: "CompensationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollComponents_PlatformAccountId",
                table: "RiderPayrollComponents",
                column: "PlatformAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollComponents_RiderFinancialItemId",
                table: "RiderPayrollComponents",
                column: "RiderFinancialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollComponents_RiderPayrollCarryForwardId",
                table: "RiderPayrollComponents",
                column: "RiderPayrollCarryForwardId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollComponents_RiderPayrollLineId_ComponentCode_PlatformAccountId",
                table: "RiderPayrollComponents",
                columns: new[] { "RiderPayrollLineId", "ComponentCode", "PlatformAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollComponents_SourceImportBatchId",
                table: "RiderPayrollComponents",
                column: "SourceImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollLines_RiderPayrollRunId_RiderIqamaNo",
                table: "RiderPayrollLines",
                columns: new[] { "RiderPayrollRunId", "RiderIqamaNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollRuns_AccrualFinancialDocumentId",
                table: "RiderPayrollRuns",
                column: "AccrualFinancialDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollRuns_LegalEntityId_PeriodStart_PeriodEnd",
                table: "RiderPayrollRuns",
                columns: new[] { "LegalEntityId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderPayrollRuns_LegalEntityId_RunNumber",
                table: "RiderPayrollRuns",
                columns: new[] { "LegalEntityId", "RunNumber" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO [AccountingAuditChainHeads] ([LegalEntityId], [LastHash], [LastEventId])
                SELECT le.[Id], ISNULL(lastEvent.[Hash], ''), ISNULL(lastEvent.[Id], 0)
                FROM [LegalEntities] le
                OUTER APPLY (
                    SELECT TOP (1) ae.[Id], ae.[Hash]
                    FROM [AccountingAuditEvents] ae
                    WHERE ae.[LegalEntityId] = le.[Id]
                    ORDER BY ae.[Id] DESC
                ) lastEvent;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_FinancialDocuments_ImmutablePosted] ON [FinancialDocuments] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.[Id] = d.[Id] WHERE d.[Status] IN (4, 5) AND i.[Id] IS NULL)
                        THROW 51100, 'Posted financial documents cannot be deleted.', 1;

                    IF EXISTS (SELECT 1 FROM deleted WHERE [Status] IN (4, 5)) AND
                       (UPDATE([LegalEntityId]) OR UPDATE([BranchId]) OR UPDATE([DocumentType]) OR UPDATE([DocumentNumber]) OR
                        UPDATE([IdempotencyKey]) OR UPDATE([RequestHash]) OR UPDATE([CorrelationId]) OR UPDATE([SourceReference]) OR
                        UPDATE([PostingProfileCode]) OR UPDATE([Description]) OR UPDATE([TransactionDate]) OR UPDATE([CurrencyCode]) OR
                        UPDATE([BaseCurrencyCode]) OR UPDATE([ExchangeRate]) OR UPDATE([ExchangeRateId]) OR UPDATE([RoundingTraceJson]) OR
                        UPDATE([CreatedBy]) OR UPDATE([CreatedAt]) OR UPDATE([SubmittedBy]) OR UPDATE([SubmittedAt]) OR
                        UPDATE([ApprovedBy]) OR UPDATE([ApprovedAt]) OR UPDATE([PostedBy]) OR UPDATE([PostedAt]) OR UPDATE([ReversalOfDocumentId]))
                        THROW 51101, 'Posted financial document content is immutable.', 1;

                    IF EXISTS (
                        SELECT 1 FROM deleted d JOIN inserted i ON i.[Id] = d.[Id]
                        WHERE d.[Status] = 5 OR (d.[Status] = 4 AND (i.[Status] <> 5 OR i.[ReversedByDocumentId] IS NULL))
                    )
                        THROW 51102, 'Only a posted-to-reversed transition with a reversal document is permitted.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_FinancialDocumentLines_ImmutablePosted] ON [FinancialDocumentLines] AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM (SELECT [FinancialDocumentId] FROM inserted UNION SELECT [FinancialDocumentId] FROM deleted) changed
                        JOIN [FinancialDocuments] fd ON fd.[Id] = changed.[FinancialDocumentId]
                        WHERE fd.[Status] IN (4, 5)
                    ) THROW 51103, 'Lines of posted financial documents are immutable.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_FinancialDocumentLineDimensions_ImmutablePosted] ON [FinancialDocumentLineDimensions] AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM (SELECT [FinancialDocumentLineId] FROM inserted UNION SELECT [FinancialDocumentLineId] FROM deleted) changed
                        JOIN [FinancialDocumentLines] line ON line.[Id] = changed.[FinancialDocumentLineId]
                        JOIN [FinancialDocuments] fd ON fd.[Id] = line.[FinancialDocumentId]
                        WHERE fd.[Status] IN (4, 5)
                    ) THROW 51104, 'Dimensions of posted financial documents are immutable.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_JournalLineDimensions_ImmutableWhenFinalized] ON [JournalLineDimensions] AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM (SELECT [JournalLineId] FROM inserted UNION SELECT [JournalLineId] FROM deleted) changed
                        JOIN [JournalLines] line ON line.[Id] = changed.[JournalLineId]
                        JOIN [JournalEntries] entry ON entry.[Id] = line.[JournalEntryId]
                        WHERE entry.[IsFinalized] = 1
                    ) THROW 51105, 'Dimensions of finalized journal entries are immutable.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_FiscalYears_NoOverlap] ON [FiscalYears] AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN [FiscalYears] y ON y.[LegalEntityId] = i.[LegalEntityId] AND y.[Id] <> i.[Id]
                        WHERE i.[StartDate] <= y.[EndDate] AND i.[EndDate] >= y.[StartDate]
                    ) THROW 51106, 'Fiscal years may not overlap for a legal entity.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_FiscalPeriods_NoOverlap] ON [FiscalPeriods] AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN [FiscalYears] iy ON iy.[Id] = i.[FiscalYearId]
                        JOIN [FiscalPeriods] p ON p.[Id] <> i.[Id]
                        JOIN [FiscalYears] py ON py.[Id] = p.[FiscalYearId] AND py.[LegalEntityId] = iy.[LegalEntityId]
                        WHERE i.[StartDate] <= p.[EndDate] AND i.[EndDate] >= p.[StartDate]
                    ) THROW 51107, 'Fiscal periods may not overlap for a legal entity.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_PlatformWorkerIdentities_NoOverlap] ON [PlatformWorkerIdentities] AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN [PlatformWorkerIdentities] w
                          ON w.[Id] <> i.[Id] AND w.[LegalEntityId] = i.[LegalEntityId]
                         AND w.[PlatformAccountId] = i.[PlatformAccountId] AND w.[ExternalWorkerId] = i.[ExternalWorkerId]
                        WHERE i.[EffectiveFrom] <= ISNULL(w.[EffectiveTo], '9999-12-31')
                          AND ISNULL(i.[EffectiveTo], '9999-12-31') >= w.[EffectiveFrom]
                    ) THROW 51108, 'Effective platform worker mappings may not overlap.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_CompensationPolicyVersions_NoActiveOverlap] ON [CompensationPolicyVersions] AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN [CompensationPolicyVersions] p
                          ON p.[Id] <> i.[Id] AND p.[LegalEntityId] = i.[LegalEntityId] AND p.[PlatformAccountId] = i.[PlatformAccountId]
                         AND p.[WorkerCategory] = i.[WorkerCategory] AND p.[Status] = 2
                        WHERE i.[Status] = 2 AND i.[EffectiveFrom] <= ISNULL(p.[EffectiveTo], '9999-12-31')
                          AND ISNULL(i.[EffectiveTo], '9999-12-31') >= p.[EffectiveFrom]
                    ) THROW 51109, 'Active compensation policy versions may not overlap.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_CompensationPolicyVersions_NoActiveOverlap]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_PlatformWorkerIdentities_NoOverlap]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_FiscalPeriods_NoOverlap]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_FiscalYears_NoOverlap]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_JournalLineDimensions_ImmutableWhenFinalized]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_FinancialDocumentLineDimensions_ImmutablePosted]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_FinancialDocumentLines_ImmutablePosted]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_FinancialDocuments_ImmutablePosted]");

            migrationBuilder.DropTable(
                name: "AccountingAuditChainHeads");

            migrationBuilder.DropTable(
                name: "PlatformFactOverrides");

            migrationBuilder.DropTable(
                name: "PlatformImportIssues");

            migrationBuilder.DropTable(
                name: "PlatformImportRawCells");

            migrationBuilder.DropTable(
                name: "PlatformWorkerIdentities");

            migrationBuilder.DropTable(
                name: "RiderFinancialInstallments");

            migrationBuilder.DropTable(
                name: "RiderPaymentBatchLines");

            migrationBuilder.DropTable(
                name: "RiderPayrollAdjustments");

            migrationBuilder.DropTable(
                name: "RiderPayrollComponents");

            migrationBuilder.DropTable(
                name: "PlatformNormalizedFacts");

            migrationBuilder.DropTable(
                name: "RiderPaymentBatches");

            migrationBuilder.DropTable(
                name: "CompensationRules");

            migrationBuilder.DropTable(
                name: "RiderFinancialItems");

            migrationBuilder.DropTable(
                name: "RiderPayrollCarryForwards");

            migrationBuilder.DropTable(
                name: "RiderPayrollLines");

            migrationBuilder.DropTable(
                name: "PlatformImportRawRows");

            migrationBuilder.DropTable(
                name: "CompensationPolicyVersions");

            migrationBuilder.DropTable(
                name: "RiderFinancialItemTypes");

            migrationBuilder.DropTable(
                name: "RiderPayrollRuns");

            migrationBuilder.DropTable(
                name: "PlatformImportSheets");

            migrationBuilder.DropTable(
                name: "PlatformImportBatches");

            migrationBuilder.DropTable(
                name: "AccountingStoredFiles");

            migrationBuilder.DropTable(
                name: "PlatformImportTemplates");

            migrationBuilder.DropIndex(
                name: "IX_AccountingOutboxMessages_ProcessedAt_DeadLetteredAt_NextAttemptAt_LockedUntil_OccurredAt",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "BaseCredit",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "BaseDebit",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                table: "FiscalPeriods");

            migrationBuilder.DropColumn(
                name: "PayrollLocked",
                table: "FiscalPeriods");

            migrationBuilder.DropColumn(
                name: "ReopenReason",
                table: "FiscalPeriods");

            migrationBuilder.DropColumn(
                name: "ReopenedAt",
                table: "FiscalPeriods");

            migrationBuilder.DropColumn(
                name: "ReopenedBy",
                table: "FiscalPeriods");

            migrationBuilder.DropColumn(
                name: "TaxLocked",
                table: "FiscalPeriods");

            migrationBuilder.DropColumn(
                name: "BaseCurrencyCode",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateId",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "RoundingTraceJson",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "BaseCredit",
                table: "FinancialDocumentLines");

            migrationBuilder.DropColumn(
                name: "BaseDebit",
                table: "FinancialDocumentLines");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "AccountingOutboxMessages");

            migrationBuilder.DropColumn(
                name: "IsCashEquivalent",
                table: "AccountingAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingOutboxMessages_ProcessedAt_OccurredAt",
                table: "AccountingOutboxMessages",
                columns: new[] { "ProcessedAt", "OccurredAt" });
        }
    }
}
