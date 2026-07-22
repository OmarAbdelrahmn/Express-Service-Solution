using Domain.Entities;
using Domain.Entities.Accounting;
using Domain.Entities.Keeta;
using Domain.Entities.Petrol;
using Domain.Entities.Spare;
using Domain.Entities.Organization;
using Domain.Entities.AccountingCore;
using Domain.Entities.FinancialOperations;
using Domain.Entities.AccountingPlatform;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Domain;

public class ApplicationDbcontext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    [SetsRequiredMembers]
    public ApplicationDbcontext(DbContextOptions<ApplicationDbcontext> options) : base(options)
    {
    }

    public required DbSet<ApplicationUser> ApplicationUsers { get; set; }
    public required DbSet<ApplicationRole> ApplicationRoles { get; set; }
    public required DbSet<Company> Companies { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<LegalEntity> LegalEntities { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<PlatformAccount> PlatformAccounts { get; set; }
    public DbSet<LegacyCompanyPlatformMapping> LegacyCompanyPlatformMappings { get; set; }
    public DbSet<AccountingAccount> AccountingAccounts { get; set; }
    public DbSet<Currency> Currencies { get; set; }
    public DbSet<FinancialUserAccess> FinancialUserAccesses { get; set; }
    public DbSet<ExchangeRate> ExchangeRates { get; set; }
    public DbSet<FinancialDimension> FinancialDimensions { get; set; }
    public DbSet<FinancialDimensionValue> FinancialDimensionValues { get; set; }
    public DbSet<PostingProfile> PostingProfiles { get; set; }
    public DbSet<PostingProfileLine> PostingProfileLines { get; set; }
    public DbSet<FiscalYear> FiscalYears { get; set; }
    public DbSet<FiscalPeriod> FiscalPeriods { get; set; }
    public DbSet<FinancialDocument> FinancialDocuments { get; set; }
    public DbSet<LegalEntityDocumentSequence> LegalEntityDocumentSequences { get; set; }
    public DbSet<FinancialDocumentLine> FinancialDocumentLines { get; set; }
    public DbSet<FinancialDocumentLineDimension> FinancialDocumentLineDimensions { get; set; }
    public DbSet<DocumentApproval> DocumentApprovals { get; set; }
    public DbSet<PostingBatch> PostingBatches { get; set; }
    public DbSet<JournalEntry> JournalEntries { get; set; }
    public DbSet<JournalLine> JournalLines { get; set; }
    public DbSet<JournalLineDimension> JournalLineDimensions { get; set; }
    public DbSet<RecurringJournalSchedule> RecurringJournalSchedules { get; set; }
    public DbSet<RecurringJournalScheduleLine> RecurringJournalScheduleLines { get; set; }
    public DbSet<AccountingAuditEvent> AccountingAuditEvents { get; set; }
    public DbSet<AccountingOutboxMessage> AccountingOutboxMessages { get; set; }
    public DbSet<AccountingAuditChainHead> AccountingAuditChainHeads { get; set; }
    public DbSet<SourceEvidence> SourceEvidences { get; set; }
    public DbSet<PlatformSettlement> PlatformSettlements { get; set; }
    public DbSet<CustomerAccount> CustomerAccounts { get; set; }
    public DbSet<CustomerInvoice> CustomerInvoices { get; set; }
    public DbSet<CustomerInvoiceLine> CustomerInvoiceLines { get; set; }
    public DbSet<CustomerReceipt> CustomerReceipts { get; set; }
    public DbSet<CustomerReceiptAllocation> CustomerReceiptAllocations { get; set; }
    public DbSet<EmployeePayContract> EmployeePayContracts { get; set; }
    public DbSet<PayrollRun> PayrollRuns { get; set; }
    public DbSet<PayrollRunLine> PayrollRunLines { get; set; }
    public DbSet<SupplierAccount> SupplierAccounts { get; set; }
    public DbSet<SupplierInvoice> SupplierInvoices { get; set; }
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines { get; set; }
    public DbSet<SupplierPayment> SupplierPayments { get; set; }
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations { get; set; }
    public DbSet<InventoryItem> InventoryItems { get; set; }
    public DbSet<InventoryMovement> InventoryMovements { get; set; }
    public DbSet<ExpenseClaim> ExpenseClaims { get; set; }
    public DbSet<BankAccount> BankAccounts { get; set; }
    public DbSet<BankStatementLine> BankStatementLines { get; set; }
    public DbSet<TaxCode> TaxCodes { get; set; }
    public DbSet<TaxTransaction> TaxTransactions { get; set; }
    public DbSet<TaxReturn> TaxReturns { get; set; }
    public DbSet<FixedAsset> FixedAssets { get; set; }
    public DbSet<Budget> Budgets { get; set; }
    public DbSet<BudgetLine> BudgetLines { get; set; }
    public DbSet<AccountingStoredFile> AccountingStoredFiles { get; set; }
    public DbSet<PlatformImportTemplate> PlatformImportTemplates { get; set; }
    public DbSet<PlatformImportBatch> PlatformImportBatches { get; set; }
    public DbSet<PlatformImportSheet> PlatformImportSheets { get; set; }
    public DbSet<PlatformImportRawRow> PlatformImportRawRows { get; set; }
    public DbSet<PlatformImportRawCell> PlatformImportRawCells { get; set; }
    public DbSet<PlatformNormalizedFact> PlatformNormalizedFacts { get; set; }
    public DbSet<PlatformFactOverride> PlatformFactOverrides { get; set; }
    public DbSet<PlatformImportIssue> PlatformImportIssues { get; set; }
    public DbSet<PlatformWorkerIdentity> PlatformWorkerIdentities { get; set; }
    public DbSet<CompensationPolicyVersion> CompensationPolicyVersions { get; set; }
    public DbSet<CompensationRule> CompensationRules { get; set; }
    public DbSet<RiderPayrollRun> RiderPayrollRuns { get; set; }
    public DbSet<RiderPayrollLine> RiderPayrollLines { get; set; }
    public DbSet<RiderPayrollComponent> RiderPayrollComponents { get; set; }
    public DbSet<RiderPayrollAdjustment> RiderPayrollAdjustments { get; set; }
    public DbSet<RiderPayrollCarryForward> RiderPayrollCarryForwards { get; set; }
    public DbSet<RiderFinancialItemType> RiderFinancialItemTypes { get; set; }
    public DbSet<RiderFinancialItem> RiderFinancialItems { get; set; }
    public DbSet<RiderFinancialInstallment> RiderFinancialInstallments { get; set; }
    public DbSet<RiderPaymentBatch> RiderPaymentBatches { get; set; }
    public DbSet<RiderPaymentBatchLine> RiderPaymentBatchLines { get; set; }
    public DbSet<HousingCashUserAccess> HousingCashUserAccesses { get; set; }
    public required DbSet<Employees> Employees { get; set; }
    public required DbSet<EmployeeDocuments> EmployeeDocuments { get; set; }
    public required DbSet<Housing> Housings { get; set; }
    public required DbSet<RiderDetails> RiderDetails { get; set; }
    public required DbSet<RiderShift> RiderShifts { get; set; }
    public required DbSet<RiderShiftSubstitution> RiderShiftSubstitutions { get; set; }
    public required DbSet<Vehicle> Vehicles { get; set; }
    public required DbSet<DeletedEmployees> DeletedEmployees { get; set; }
    public required DbSet<RiderCompanyHistory> RiderCompanyHistory { get; set; }
    public required DbSet<RiderVehicleStatus> RiderVehicleStatus { get; set; }
    public required DbSet<TempRiderShiftComparison> TempRiderShiftComparisons { get; set; }
    public required DbSet<TempEmployeeUpdate> TempEmployeeUpdates { get; set; }
    public required DbSet<TempEmployeeStatusChange> TempEmployeeStatusChanges { get; set; }
    public required DbSet<TempVehicleOperation> TempVehicleOperations { get; set; }
    public required DbSet<RiderWorkingIdHistory> RiderWorkingIdHistories { get; set; }
    public required DbSet<SparePart> SpareParts { get; set; }
    public required DbSet<RiderAccessory> RiderAccessories { get; set; }
    public required DbSet<RiderAccessoryUsage> RiderAccessoryUsages { get; set; }
    public required DbSet<SparePartUsage> SparePartUsages { get; set; }
    public required DbSet<InventoryAuditLog> InventoryAuditLogs { get; set; }
    public required DbSet<Supplier> Suppliers { get; set; }
    public required DbSet<Bill> Bills { get; set; }
    public required DbSet<BillItem> BillItems { get; set; }
    public required DbSet<Transfer> Transfers { get; set; }
    public required DbSet<TransferItem> TransferItems { get; set; }
    public required DbSet<Return> Returns { get; set; }
    public required DbSet<ReturnItem> ReturnItems { get; set; }
    public required DbSet<KetaFreeLancer> KetaFreeLancers { get; set; }
    public required DbSet<RiderMonthlyValidity> RiderMonthlyValidities { get; set; }
    public required DbSet<Company2ValidationConfig> Company2ValidationConfigs{ get; set; }
    public DbSet<DailyReportLog> DailyReportLogs { get; set; }
    public DbSet<Wallet> Wallets { get; set; }
    public DbSet<EscapedEmployeeDetails> EscapedEmployeeDetails{ get; set; }
    public DbSet<EmployeeStatusLog> EmployeeStatusLogs { get; set; }

    public DbSet<VehiclePetrolCost>  VehiclePetrolCosts  { get; set; }
    public DbSet<RiderPetrolCost> RiderPetrolCosts { get; set; }
    public DbSet<EmployeeOrder> EmployeeOrders{ get; set; }

    public DbSet<TransporterShift> TransporterShifts { get; set; }
    public DbSet<OutRiderInfo> OutRiderInfos { get; set; }
    public DbSet<OutageShiftPerformance> OutageShiftPerformances { get; set; }
    public DbSet<MaintenanceInterval> MaintenanceIntervals { get; set; }
    public DbSet<KeetaDriverShift> KeetaDriverShifts { get; set; }
    public DbSet<KeetaShiftSlot> KeetaShiftSlots{ get; set; }
    public DbSet<AccountingPeriod> AccountingPeriods { get; set; }
    public DbSet<CostCenter> CostCenters { get; set; }
    public DbSet<AccountingAccount> AccountingAccounts { get; set; }
    public DbSet<JournalEntry> JournalEntries { get; set; }
    public DbSet<JournalEntryLine> JournalEntryLines { get; set; }
    public DbSet<AccountingAuditLog> AccountingAuditLogs { get; set; }
    public DbSet<AccountingNote> AccountingNotes { get; set; }
    public DbSet<AccountingAttachment> AccountingAttachments { get; set; }
    public DbSet<CompanyBillImport> CompanyBillImports { get; set; }
    public DbSet<CompanyBillSheet> CompanyBillSheets { get; set; }
    public DbSet<CompanyBillRawRow> CompanyBillRawRows { get; set; }
    public DbSet<CompanyBillRawCell> CompanyBillRawCells { get; set; }
    public DbSet<CompanyBillRiderSummary> CompanyBillRiderSummaries { get; set; }
    public DbSet<CompanyBillTransactionLine> CompanyBillTransactionLines { get; set; }
    public DbSet<CompanyBillDailyMetric> CompanyBillDailyMetrics { get; set; }
    public DbSet<CompanyBillResolutionIssue> CompanyBillResolutionIssues { get; set; }
    public DbSet<RiderEarning> RiderEarnings { get; set; }
    public DbSet<RiderBonusRule> RiderBonusRules { get; set; }
    public DbSet<RiderSalaryRule> RiderSalaryRules { get; set; }
    public DbSet<RiderBonusAward> RiderBonusAwards { get; set; }
    public DbSet<RiderFinancialItemType> RiderFinancialItemTypes { get; set; }
    public DbSet<RiderFinancialItem> RiderFinancialItems { get; set; }
    public DbSet<RiderLoan> RiderLoans { get; set; }
    public DbSet<RiderLoanInstallment> RiderLoanInstallments { get; set; }
    public DbSet<RiderFinalSettlement> RiderFinalSettlements { get; set; }
    public DbSet<RiderMonthlySalary> RiderMonthlySalaries { get; set; }
    public DbSet<RiderMonthlySalaryLine> RiderMonthlySalaryLines { get; set; }
    public DbSet<RiderSalaryPaymentBatch> RiderSalaryPaymentBatches { get; set; }
    public DbSet<RiderSalaryPayment> RiderSalaryPayments { get; set; }
    public DbSet<CashSalaryHandoverBatch> CashSalaryHandoverBatches { get; set; }
    public DbSet<CashSalaryHandoverLine> CashSalaryHandoverLines { get; set; }
    public DbSet<CompanyReceivable> CompanyReceivables { get; set; }
    public DbSet<CompanyPaymentReceipt> CompanyPaymentReceipts { get; set; }
    public DbSet<CompanyExpenseCategory> CompanyExpenseCategories { get; set; }
    public DbSet<CompanyExpense> CompanyExpenses { get; set; }
    public DbSet<SupplierPayable> SupplierPayables { get; set; }
    public DbSet<SupplierPayment> SupplierPayments { get; set; }
    public DbSet<CompanyProfitSnapshot> CompanyProfitSnapshots { get; set; }
    public DbSet<FixedAsset> FixedAssets { get; set; }
    public DbSet<AssetDepreciationEntry> AssetDepreciationEntries { get; set; }
    public DbSet<BankAccount> BankAccounts { get; set; }
    public DbSet<TreasuryAccount> TreasuryAccounts { get; set; }
    public DbSet<BankTransaction> BankTransactions { get; set; }
    public DbSet<BankReconciliation> BankReconciliations { get; set; }
    public DbSet<CheckCycle> CheckCycles { get; set; }
    public DbSet<PurchaseInvoice> PurchaseInvoices { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        var cascadeFKs = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade);

        foreach (var fk in cascadeFKs)
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        modelBuilder.Entity<RiderDetails>()
        .HasOne(r => r.Vehicle)
        .WithOne(v => v.RiderDetails)
        .HasForeignKey<RiderDetails>(r => r.VehicleNumber);

        modelBuilder.Entity<Employees>()
        .HasOne(e => e.RiderDetails)
        .WithOne(r => r.Employee)
        .HasForeignKey<RiderDetails>(r => r.EmployeeIqamaNo);

        modelBuilder.Entity<RiderVehicleStatus>()
        .HasOne(r => r.Vehicle)
        .WithMany(v => v.RiderVehicleStatuses)
        .HasForeignKey(r => r.VehicleNumber)
        .HasPrincipalKey(v => v.VehicleNumber);

        modelBuilder.Entity<RiderVehicleStatus>(entity =>
        {


            entity.Property(rvs => rvs.Permission)
            .IsRequired(false)
                .HasMaxLength(500);

            entity.Property(rvs => rvs.PermissionStartDate).IsRequired(false);
            entity.Property(rvs => rvs.PermissionEndDate).IsRequired(false);

            entity.Property(rvs => rvs.Timestamp)
                .HasDefaultValueSql("GETDATE()")
                .HasColumnType("datetime2");

            entity.Property(rvs => rvs.IsActive)
                .HasDefaultValue(false);

            entity.Property(rvs => rvs.StatusType)
                .IsRequired()
                .HasConversion<int>();

            entity.HasIndex(rvs => new { rvs.VehicleNumber, rvs.IsActive, rvs.StatusType });
            entity.HasIndex(rvs => new { rvs.EmployeeIqamaNo, rvs.IsActive });
            entity.HasIndex(rvs => rvs.Timestamp);
            entity.HasIndex(rvs => new { rvs.VehicleNumber, rvs.IsActive, rvs.PermissionEndDate })
                .HasFilter("[PermissionEndDate] IS NOT NULL");
        });


        modelBuilder.Entity<TempEmployeeUpdate>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.IqamaNo)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(e => e.IqamaNo);
            entity.HasIndex(e => e.IsResolved);
            entity.HasIndex(e => e.UploadedAt);
        });

        modelBuilder.Entity<TempEmployeeStatusChange>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeIqamaNo)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Reason)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.RequestedBy)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.EmployeeIqamaNo);
            entity.HasIndex(e => e.IsResolved);
            entity.HasIndex(e => e.RequestedAt);
        });

        modelBuilder.Entity<TempVehicleOperation>(entity =>
        {


            entity.Property(t => t.VehicleStatusType)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(t => t.Reason)
                .HasMaxLength(500);

            entity.Property(t => t.Permission)
                .HasMaxLength(500)
                            .IsRequired(false);

            entity.Property(t => t.PermissionEndDate)
            .IsRequired(false);

            entity.Property(t => t.RequestedAt)
                .HasDefaultValueSql("GETDATE()")
                .HasColumnType("datetime2");

            entity.Property(t => t.RequestedBy)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(t => t.IsResolved)
                .HasDefaultValue(false);

            entity.Property(t => t.Resolution)
                .HasMaxLength(50);

            entity.Property(t => t.ResolvedBy)
                .HasMaxLength(200);

            entity.Property(t => t.ResolvedAt)
                .HasColumnType("datetime2");

            entity.Property(t => t.AdminNotes)
                .HasMaxLength(1000);

            entity.HasOne(t => t.Rider)
                .WithMany()
                .HasForeignKey(t => t.RiderIqamaNo)
                .HasPrincipalKey(r => r.EmployeeIqamaNo)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.Vehicle)
                .WithMany()
                .HasForeignKey(t => t.VehicleNumber)
                .HasPrincipalKey(v => v.VehicleNumber)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(t => new { t.RiderIqamaNo, t.IsResolved });
            entity.HasIndex(t => new { t.IsResolved, t.VehicleStatusType })
                .HasFilter("[IsResolved] = 0");
            entity.HasIndex(e => e.RiderIqamaNo);
            entity.HasIndex(e => e.VehicleNumber);
            entity.HasIndex(e => e.IsResolved);
            entity.HasIndex(e => e.RequestedAt);
        });


        modelBuilder.Entity<RiderShiftSubstitution>(entity =>
            {
                entity.HasKey(s => s.Id);

                entity.HasIndex(s => new { s.ActualRiderWorkingId, s.IsActive });
                entity.HasIndex(s => new { s.SubstituteWorkingId, s.IsActive });

                entity.HasOne(s => s.ActualRider)
                    .WithMany()
                    .HasForeignKey(s => s.ActualRiderId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false);

                entity.Navigation(s => s.ActualRider)
                    .IsRequired(false);

                entity.Property(s => s.ActualRiderId)
                    .IsRequired(false);

                entity.Property(s => s.EndDate)
                    .IsRequired(false);

                entity.HasOne(s => s.SubstituteRider)
                    .WithMany()
                    .HasForeignKey(s => s.SubstituteRiderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        modelBuilder.Entity<RiderWorkingIdHistory>(entity =>
        {
            entity.HasKey(h => h.Id);

            entity.HasIndex(h => h.WorkingId);
            entity.HasIndex(h => h.RiderIqamaNo);
            entity.HasIndex(h => new { h.WorkingId, h.IsActive });
            entity.HasIndex(h => new { h.RiderIqamaNo, h.IsActive });

            entity.HasIndex(h => h.CompanyId);

            entity.Property(h => h.WorkingId)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasOne(h => h.Employee)
                .WithMany()
                .HasForeignKey(h => h.RiderIqamaNo)
                .HasPrincipalKey(e => e.IqamaNo)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(h => h.Company)
                .WithMany()
                .HasForeignKey(h => h.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        foreach (var property in modelBuilder.Model.GetEntityTypes()
       .SelectMany(t => t.GetProperties())
       .Where(p => (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)) && p.GetPrecision() is null))
        {
            property.SetColumnType("decimal(18, 2)");
        }

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HungerDisability>(entity =>
        {
            entity.HasKey(h => h.Id);

            entity.Property(h => h.ActualWorkingId)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(h => h.SubstituteWorkingId)
                .HasMaxLength(50);

            entity.Property(h => h.ShiftDate)
                .IsRequired();

            entity.Property(h => h.Days)
                .IsRequired();

            entity.Property(h => h.AcceptedDailyOrders)
                .IsRequired();

            entity.Property(h => h.CreatedAt)
                .IsRequired();

            // Relationship with ActualRider (the disabled rider)
            entity.HasOne(h => h.Rider)
                .WithMany()
                .HasForeignKey(h => h.ActualRiderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relationship with Company
            entity.HasOne(h => h.Company)
                .WithMany()
                .HasForeignKey(h => h.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Composite unique index to prevent duplicate records for same rider on same date
            entity.HasIndex(h => new { h.ActualRiderId, h.ShiftDate })
                .IsUnique()
                .HasDatabaseName("IX_HungerDisability_ActualRider_ShiftDate");

            // Index for performance on common queries
            entity.HasIndex(h => h.ActualWorkingId)
                .HasDatabaseName("IX_HungerDisability_ActualWorkingId");

            entity.HasIndex(h => h.ShiftDate)
                .HasDatabaseName("IX_HungerDisability_ShiftDate");

            entity.HasIndex(h => h.CompanyId)
                .HasDatabaseName("IX_HungerDisability_CompanyId");

            entity.HasIndex(h => h.SubstituteRiderId)
                .HasDatabaseName("IX_HungerDisability_SubstituteRiderId");

            entity.ToTable("HungerDisabilities");
        });


    }

}
