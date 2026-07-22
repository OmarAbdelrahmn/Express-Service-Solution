using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations;

[DbContext(typeof(ApplicationDbcontext))]
[Migration("20260713102000_ArchiveLegacyAccountingSchema")]
public partial class ArchiveLegacyAccountingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS
            (
                SELECT 1
                FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = N'20260624092332_AddAccountingModule'
            )
            OR OBJECT_ID(N'[dbo].[CompanyBillImports]', N'U') IS NOT NULL
            BEGIN
                IF SCHEMA_ID(N'legacy_accounting') IS NULL
                    EXEC(N'CREATE SCHEMA [legacy_accounting] AUTHORIZATION [dbo];');

                DECLARE @LegacyTables TABLE
                (
                    [SortOrder] int NOT NULL PRIMARY KEY,
                    [TableName] sysname NOT NULL UNIQUE
                );

                INSERT INTO @LegacyTables ([SortOrder], [TableName])
                VALUES
                    (1, N'AccountingAccounts'),
                    (2, N'AccountingAttachments'),
                    (3, N'AccountingAuditLogs'),
                    (4, N'AccountingNotes'),
                    (5, N'AccountingPeriods'),
                    (6, N'AssetDepreciationEntries'),
                    (7, N'BankAccounts'),
                    (8, N'BankReconciliations'),
                    (9, N'BankTransactions'),
                    (10, N'CashSalaryHandoverBatches'),
                    (11, N'CashSalaryHandoverLines'),
                    (12, N'CheckCycles'),
                    (13, N'CompanyBillDailyMetrics'),
                    (14, N'CompanyBillImports'),
                    (15, N'CompanyBillRawCells'),
                    (16, N'CompanyBillRawRows'),
                    (17, N'CompanyBillResolutionIssues'),
                    (18, N'CompanyBillRiderSummaries'),
                    (19, N'CompanyBillSheets'),
                    (20, N'CompanyBillTransactionLines'),
                    (21, N'CompanyExpenseCategories'),
                    (22, N'CompanyExpenses'),
                    (23, N'CompanyPaymentReceipts'),
                    (24, N'CompanyProfitSnapshots'),
                    (25, N'CompanyReceivables'),
                    (26, N'CostCenters'),
                    (27, N'FixedAssets'),
                    (28, N'JournalEntries'),
                    (29, N'JournalEntryLines'),
                    (30, N'PurchaseInvoices'),
                    (31, N'RiderBonusAwards'),
                    (32, N'RiderBonusRules'),
                    (33, N'RiderEarnings'),
                    (34, N'RiderFinalSettlements'),
                    (35, N'RiderFinancialItems'),
                    (36, N'RiderFinancialItemTypes'),
                    (37, N'RiderLoanInstallments'),
                    (38, N'RiderLoans'),
                    (39, N'RiderMonthlySalaries'),
                    (40, N'RiderMonthlySalaryLines'),
                    (41, N'RiderSalaryPaymentBatches'),
                    (42, N'RiderSalaryPayments'),
                    (43, N'RiderSalaryRules'),
                    (44, N'SupplierPayables'),
                    (45, N'SupplierPayments'),
                    (46, N'TreasuryAccounts');

                DECLARE @TableName sysname;
                DECLARE @QualifiedDboName nvarchar(517);
                DECLARE @QualifiedLegacyName nvarchar(517);
                DECLARE @ConflictMessage nvarchar(2048);
                DECLARE @Sql nvarchar(max);

                DECLARE LegacyTableCursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT [TableName]
                    FROM @LegacyTables
                    ORDER BY [SortOrder];

                OPEN LegacyTableCursor;
                FETCH NEXT FROM LegacyTableCursor INTO @TableName;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @QualifiedDboName = N'[dbo].' + QUOTENAME(@TableName);
                    SET @QualifiedLegacyName = N'[legacy_accounting].' + QUOTENAME(@TableName);

                    IF OBJECT_ID(@QualifiedDboName, N'U') IS NOT NULL
                    BEGIN
                        IF OBJECT_ID(@QualifiedLegacyName, N'U') IS NOT NULL
                        BEGIN
                            SET @ConflictMessage = N'Cannot archive legacy accounting table ' + @TableName
                                + N' because both dbo and legacy_accounting copies already exist.';
                            CLOSE LegacyTableCursor;
                            DEALLOCATE LegacyTableCursor;
                            THROW 51001, @ConflictMessage, 1;
                        END;

                        SET @Sql = N'ALTER SCHEMA [legacy_accounting] TRANSFER [dbo].'
                            + QUOTENAME(@TableName)
                            + N';';
                        EXEC sys.sp_executesql @Sql;
                    END;

                    FETCH NEXT FROM LegacyTableCursor INTO @TableName;
                END;

                CLOSE LegacyTableCursor;
                DEALLOCATE LegacyTableCursor;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF SCHEMA_ID(N'legacy_accounting') IS NOT NULL
            BEGIN
                DECLARE @LegacyTables TABLE
                (
                    [SortOrder] int NOT NULL PRIMARY KEY,
                    [TableName] sysname NOT NULL UNIQUE
                );

                INSERT INTO @LegacyTables ([SortOrder], [TableName])
                VALUES
                    (1, N'AccountingAccounts'),
                    (2, N'AccountingAttachments'),
                    (3, N'AccountingAuditLogs'),
                    (4, N'AccountingNotes'),
                    (5, N'AccountingPeriods'),
                    (6, N'AssetDepreciationEntries'),
                    (7, N'BankAccounts'),
                    (8, N'BankReconciliations'),
                    (9, N'BankTransactions'),
                    (10, N'CashSalaryHandoverBatches'),
                    (11, N'CashSalaryHandoverLines'),
                    (12, N'CheckCycles'),
                    (13, N'CompanyBillDailyMetrics'),
                    (14, N'CompanyBillImports'),
                    (15, N'CompanyBillRawCells'),
                    (16, N'CompanyBillRawRows'),
                    (17, N'CompanyBillResolutionIssues'),
                    (18, N'CompanyBillRiderSummaries'),
                    (19, N'CompanyBillSheets'),
                    (20, N'CompanyBillTransactionLines'),
                    (21, N'CompanyExpenseCategories'),
                    (22, N'CompanyExpenses'),
                    (23, N'CompanyPaymentReceipts'),
                    (24, N'CompanyProfitSnapshots'),
                    (25, N'CompanyReceivables'),
                    (26, N'CostCenters'),
                    (27, N'FixedAssets'),
                    (28, N'JournalEntries'),
                    (29, N'JournalEntryLines'),
                    (30, N'PurchaseInvoices'),
                    (31, N'RiderBonusAwards'),
                    (32, N'RiderBonusRules'),
                    (33, N'RiderEarnings'),
                    (34, N'RiderFinalSettlements'),
                    (35, N'RiderFinancialItems'),
                    (36, N'RiderFinancialItemTypes'),
                    (37, N'RiderLoanInstallments'),
                    (38, N'RiderLoans'),
                    (39, N'RiderMonthlySalaries'),
                    (40, N'RiderMonthlySalaryLines'),
                    (41, N'RiderSalaryPaymentBatches'),
                    (42, N'RiderSalaryPayments'),
                    (43, N'RiderSalaryRules'),
                    (44, N'SupplierPayables'),
                    (45, N'SupplierPayments'),
                    (46, N'TreasuryAccounts');

                DECLARE @TableName sysname;
                DECLARE @QualifiedDboName nvarchar(517);
                DECLARE @QualifiedLegacyName nvarchar(517);
                DECLARE @ConflictMessage nvarchar(2048);
                DECLARE @Sql nvarchar(max);

                DECLARE LegacyTableCursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT [TableName]
                    FROM @LegacyTables
                    ORDER BY [SortOrder] DESC;

                OPEN LegacyTableCursor;
                FETCH NEXT FROM LegacyTableCursor INTO @TableName;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @QualifiedDboName = N'[dbo].' + QUOTENAME(@TableName);
                    SET @QualifiedLegacyName = N'[legacy_accounting].' + QUOTENAME(@TableName);

                    IF OBJECT_ID(@QualifiedLegacyName, N'U') IS NOT NULL
                    BEGIN
                        IF OBJECT_ID(@QualifiedDboName, N'U') IS NOT NULL
                        BEGIN
                            SET @ConflictMessage = N'Cannot restore legacy accounting table ' + @TableName
                                + N' because a dbo table with the same name exists.';
                            CLOSE LegacyTableCursor;
                            DEALLOCATE LegacyTableCursor;
                            THROW 51002, @ConflictMessage, 1;
                        END;

                        SET @Sql = N'ALTER SCHEMA [dbo] TRANSFER [legacy_accounting].'
                            + QUOTENAME(@TableName)
                            + N';';
                        EXEC sys.sp_executesql @Sql;
                    END;

                    FETCH NEXT FROM LegacyTableCursor INTO @TableName;
                END;

                CLOSE LegacyTableCursor;
                DEALLOCATE LegacyTableCursor;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.objects
                    WHERE [schema_id] = SCHEMA_ID(N'legacy_accounting')
                )
                    EXEC(N'DROP SCHEMA [legacy_accounting];');
            END;
            """);
    }
}
