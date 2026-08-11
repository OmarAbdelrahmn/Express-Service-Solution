using Domain.Entities;
using Domain.Entities.Keeta;
using Domain.Entities.Petrol;
using Domain.Entities.Spare;
using Domain.Entities.Organization;
using Domain.Entities.AccountingCore;
using Domain.Entities.FinancialOperations;
using Domain.Entities.AccountingPlatform;
using Domain.Entities.Vacation;
using Domain.Auditing;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Domain;

public class ApplicationDbcontext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAuditContextAccessor auditContextAccessor;

    [SetsRequiredMembers]
    public ApplicationDbcontext(DbContextOptions<ApplicationDbcontext> options)
        : this(options, new AuditContextAccessor())
    {
    }

    [SetsRequiredMembers]
    public ApplicationDbcontext(
        DbContextOptions<ApplicationDbcontext> options,
        IAuditContextAccessor auditContextAccessor) : base(options)
    {
        this.auditContextAccessor = auditContextAccessor;
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
    public DbSet<VacationUserRoleAssignment> VacationUserRoleAssignments { get; set; }
    public DbSet<VacationRequest> VacationRequests { get; set; }
    public DbSet<VacationApprovalDecision> VacationApprovalDecisions { get; set; }
    public DbSet<VacationDateChangeRequest> VacationDateChangeRequests { get; set; }
    public DbSet<VacationCancellationRequest> VacationCancellationRequests { get; set; }
    public DbSet<VacationHrDocument> VacationHrDocuments { get; set; }
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
    public DbSet<SystemAuditEvent> SystemAuditEvents { get; set; }

    public DbSet<TransporterShift> TransporterShifts { get; set; }
    public DbSet<OutRiderInfo> OutRiderInfos { get; set; }
    public DbSet<OutageShiftPerformance> OutageShiftPerformances { get; set; }
    public DbSet<MaintenanceInterval> MaintenanceIntervals { get; set; }
    public DbSet<KeetaDriverShift> KeetaDriverShifts { get; set; }
    public DbSet<KeetaShiftSlot> KeetaShiftSlots{ get; set; }
    public DbSet<KeetaBreakConfiguration> KeetaBreakConfigurations { get; set; }
    public DbSet<KeetaBreakShiftDefinition> KeetaBreakShiftDefinitions { get; set; }
    public DbSet<KeetaBreakShiftPattern> KeetaBreakShiftPatterns { get; set; }
    public DbSet<KeetaBreakBatch> KeetaBreakBatches { get; set; }
    public DbSet<KeetaBreakImportedRider> KeetaBreakImportedRiders { get; set; }
    public DbSet<KeetaBreakAssignment> KeetaBreakAssignments { get; set; }
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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var candidates = CaptureAuditCandidates();
        if (candidates.Count == 0)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        if (!acceptAllChangesOnSuccess)
            throw new InvalidOperationException("Audited saves must accept changes on success.");

        IDbContextTransaction? transaction = null;
        try
        {
            if (Database.IsRelational() && Database.CurrentTransaction is null)
                transaction = Database.BeginTransaction();

            var result = base.SaveChanges(true);
            AddAuditEvents(candidates);
            base.SaveChanges(true);
            transaction?.Commit();
            return result;
        }
        catch
        {
            transaction?.Rollback();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var candidates = CaptureAuditCandidates();
        if (candidates.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (!acceptAllChangesOnSuccess)
            throw new InvalidOperationException("Audited saves must accept changes on success.");

        IDbContextTransaction? transaction = null;
        try
        {
            if (Database.IsRelational() && Database.CurrentTransaction is null)
                transaction = await Database.BeginTransactionAsync(cancellationToken);

            var result = await base.SaveChangesAsync(true, cancellationToken);
            AddAuditEvents(candidates);
            await base.SaveChangesAsync(true, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private List<AuditCandidate> CaptureAuditCandidates()
    {
        ChangeTracker.DetectChanges();

        var candidates = new List<AuditCandidate>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || !ShouldAudit(entry))
                continue;

            var action = entry.State switch
            {
                EntityState.Added => SystemAuditAction.Create,
                EntityState.Modified => SystemAuditAction.Update,
                EntityState.Deleted => SystemAuditAction.Delete,
                _ => throw new InvalidOperationException("Unexpected audited entity state.")
            };

            var allProperties = GetAuditedProperties(entry).ToList();
            var properties = allProperties;
            if (action == SystemAuditAction.Update)
            {
                properties = properties
                    .Where(property => property.IsModified && !ValuesEqual(property.OriginalValue, property.CurrentValue))
                    .ToList();

                if (properties.Count == 0)
                    continue;
            }

            var oldValues = action is SystemAuditAction.Update or SystemAuditAction.Delete
                ? ReadValues(properties, useOriginalValues: true)
                : new Dictionary<string, object?>();
            var newValues = action == SystemAuditAction.Create
                ? new Dictionary<string, object?>()
                : action == SystemAuditAction.Update
                    ? ReadValues(properties, useOriginalValues: false)
                    : new Dictionary<string, object?>();
            var (scopeTypeBefore, scopeBefore) = action is SystemAuditAction.Update or SystemAuditAction.Delete
                ? GetScope(ReadValues(allProperties, useOriginalValues: true))
                : (null, null);
            var (scopeTypeAfter, scopeAfter) = action is SystemAuditAction.Create or SystemAuditAction.Update
                ? GetScope(ReadValues(allProperties, useOriginalValues: false))
                : (null, null);

            candidates.Add(new AuditCandidate(
                entry.Entity,
                entry.Metadata,
                action,
                properties.Select(x => x.Metadata.Name).ToArray(),
                oldValues,
                newValues,
                action == SystemAuditAction.Delete ? BuildEntityKey(entry, useOriginalValues: true) : null,
                GetDisplayName(
                    ReadValues(allProperties, useOriginalValues: false),
                    ReadValues(allProperties, useOriginalValues: true)),
                scopeTypeBefore ?? scopeTypeAfter,
                scopeBefore,
                scopeAfter));
        }

        return candidates;
    }

    private void AddAuditEvents(IEnumerable<AuditCandidate> candidates)
    {
        var context = auditContextAccessor.Current;
        var timestamp = DateTimeOffset.UtcNow;
        var events = new List<SystemAuditEvent>();

        foreach (var candidate in candidates)
        {
            var entry = Entry(candidate.Entity);
            var newValues = candidate.Action == SystemAuditAction.Create
                ? ReadValues(GetAuditedProperties(entry)
                    .Where(property => candidate.PropertyNames.Contains(property.Metadata.Name)), useOriginalValues: false)
                : candidate.NewValues;
            var entityKey = candidate.EntityKey ?? BuildEntityKey(entry, useOriginalValues: false);

            events.Add(new SystemAuditEvent
            {
                OperationId = context.OperationId,
                OccurredAtUtc = timestamp,
                ActorType = context.ActorType,
                ActorUserId = context.ActorUserId,
                ActorName = context.ActorName,
                Source = context.Source,
                OperationName = context.OperationName,
                CorrelationId = context.CorrelationId,
                HttpMethod = context.HttpMethod,
                RequestPath = context.RequestPath,
                IpAddress = context.IpAddress,
                EntityType = candidate.Metadata.ClrType.FullName ?? candidate.Metadata.Name,
                EntityKey = entityKey,
                EntityDisplayName = candidate.EntityDisplayName,
                Action = candidate.Action,
                ChangedFieldsJson = JsonSerializer.Serialize(candidate.PropertyNames.Order(StringComparer.Ordinal), AuditJsonOptions),
                OldValuesJson = candidate.OldValues.Count == 0 ? null : JsonSerializer.Serialize(candidate.OldValues, AuditJsonOptions),
                NewValuesJson = newValues.Count == 0 ? null : JsonSerializer.Serialize(newValues, AuditJsonOptions),
                ScopeType = candidate.ScopeType,
                ScopeBefore = candidate.ScopeBefore,
                ScopeAfter = candidate.ScopeAfter
            });
        }

        SystemAuditEvents.AddRange(events);
    }

    private static bool ShouldAudit(EntityEntry entry)
    {
        var type = entry.Metadata.ClrType;
        var typeName = type.Name;
        var nameSpace = type.Namespace ?? string.Empty;

        if (type == typeof(SystemAuditEvent) ||
            type.GetCustomAttribute<AuditIgnoreAttribute>() is not null)
            return false;

        if (nameSpace.StartsWith("Domain.Entities.AccountingCore", StringComparison.Ordinal) ||
            nameSpace.StartsWith("Domain.Entities.AccountingPlatform", StringComparison.Ordinal) ||
            nameSpace.StartsWith("Domain.Entities.FinancialOperations", StringComparison.Ordinal))
            return false;

        if (typeName.StartsWith("Temp", StringComparison.Ordinal) ||
            typeName is "DailyReportLog" or "RefreshToken" or "InventoryAuditLog" ||
            typeName.StartsWith("IdentityUserToken", StringComparison.Ordinal) ||
            typeName.StartsWith("IdentityUserLogin", StringComparison.Ordinal) ||
            typeName.StartsWith("IdentityUserClaim", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static IEnumerable<PropertyEntry> GetAuditedProperties(EntityEntry entry) => entry.Properties
        .Where(property => property.Metadata.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() is null)
        .Where(property => property.Metadata.ClrType != typeof(byte[]) && property.Metadata.ClrType != typeof(Stream));

    private static Dictionary<string, object?> ReadValues(
        IEnumerable<PropertyEntry> properties,
        bool useOriginalValues)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var value = useOriginalValues ? property.OriginalValue : property.CurrentValue;
            result[property.Metadata.Name] = IsSensitive(property.Metadata.Name) ? "[REDACTED]" : value;
        }

        return result;
    }

    private static bool IsSensitive(string propertyName) =>
        propertyName.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("securitystamp", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("concurrencystamp", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("authenticator", StringComparison.OrdinalIgnoreCase);

    private static bool ValuesEqual(object? left, object? right) => Equals(left, right);

    private static string BuildEntityKey(EntityEntry entry, bool useOriginalValues)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return "(keyless)";

        return string.Join("|", key.Properties.Select(property =>
        {
            var value = useOriginalValues
                ? entry.Property(property.Name).OriginalValue
                : entry.Property(property.Name).CurrentValue;
            return $"{property.Name}={JsonSerializer.Serialize(value, AuditJsonOptions)}";
        }));
    }

    private static string? GetDisplayName(
        IReadOnlyDictionary<string, object?> newValues,
        IReadOnlyDictionary<string, object?> oldValues)
    {
        foreach (var name in new[] { "Name", "FullName", "WorkingId", "EmployeeIqamaNo", "IqamaNo", "VehicleNumber" })
        {
            if (TryGetString(newValues, name, out var value) || TryGetString(oldValues, name, out value))
                return value;
        }

        return null;
    }

    private static (string? Type, string? Value) GetScope(IReadOnlyDictionary<string, object?> values)
    {
        if (TryGetString(values, "Location", out var location))
            return ("Location", location);
        if (TryGetString(values, "HousingId", out var housingId))
            return ("Housing", housingId);
        if (TryGetString(values, "HousingName", out var housingName))
            return ("Housing", housingName);

        return (null, null);
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object?> values, string name, out string? value)
    {
        if (values.TryGetValue(name, out var raw) && raw is not null)
        {
            value = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
            return !string.IsNullOrWhiteSpace(value) && value != "[REDACTED]";
        }

        value = null;
        return false;
    }

    private sealed record AuditCandidate(
        object Entity,
        IEntityType Metadata,
        SystemAuditAction Action,
        IReadOnlyList<string> PropertyNames,
        Dictionary<string, object?> OldValues,
        Dictionary<string, object?> NewValues,
        string? EntityKey,
        string? EntityDisplayName,
        string? ScopeType,
        string? ScopeBefore,
        string? ScopeAfter);

}
