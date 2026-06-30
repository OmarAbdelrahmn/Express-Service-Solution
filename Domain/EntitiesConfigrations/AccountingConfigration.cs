using Domain.Entities.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class AccountingAccountConfigration : IEntityTypeConfiguration<AccountingAccount>
{
    public void Configure(EntityTypeBuilder<AccountingAccount> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Code).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(a => a.Code).IsUnique();
        builder.HasOne(a => a.ParentAccount)
            .WithMany(a => a.Children)
            .HasForeignKey(a => a.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new AccountingAccount { Id = 1, Code = "1000", Name = "Cash and Bank", Type = AccountType.Asset, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 2, Code = "1100", Name = "Company Receivables", Type = AccountType.Asset, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 3, Code = "1200", Name = "Rider Receivables", Type = AccountType.Asset, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 4, Code = "1210", Name = "Loan Receivables", Type = AccountType.Asset, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 5, Code = "1220", Name = "Traffic Violation Receivables", Type = AccountType.Asset, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 6, Code = "1230", Name = "Iqama and Government Fee Receivables", Type = AccountType.Asset, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 7, Code = "2000", Name = "Supplier Payables", Type = AccountType.Liability, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 8, Code = "2100", Name = "Rider Payables", Type = AccountType.Liability, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 9, Code = "4000", Name = "Company Revenue", Type = AccountType.Revenue, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 10, Code = "5000", Name = "Rider Salary Expense", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 11, Code = "5100", Name = "Petrol Expense", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 12, Code = "5200", Name = "Spare Parts Expense", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 13, Code = "5300", Name = "Accessory Expense", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 14, Code = "5400", Name = "Housing Expense", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 15, Code = "5500", Name = "Vehicle Expense", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 16, Code = "5600", Name = "Government Fees Expense", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 17, Code = "5700", Name = "Manual Adjustments", Type = AccountType.Expense, IsSystem = true, IsActive = true },
            new AccountingAccount { Id = 18, Code = "2200", Name = "VAT Payable", Type = AccountType.Liability, IsSystem = true, IsActive = true }
        );
    }
}

public class AccountingPeriodConfigration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.Year, p.Month }).IsUnique();
        builder.Property(p => p.ClosedBy).HasMaxLength(200);
    }
}

public class CostCenterConfigration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => new { c.Type, c.CompanyId, c.RiderId, c.HousingId });
        builder.HasOne(c => c.Vehicle)
            .WithMany()
            .HasForeignKey(c => c.VehicleNumber)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class JournalEntryConfigration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.HasKey(j => j.Id);
        builder.Property(j => j.EntryNumber).IsRequired().HasMaxLength(80);
        builder.Property(j => j.Description).IsRequired().HasMaxLength(500);
        builder.Property(j => j.SourceType).HasMaxLength(100);
        builder.Property(j => j.CreatedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(j => j.EntryNumber).IsUnique();
        builder.HasIndex(j => j.EntryDate);
        builder.HasIndex(j => new { j.SourceType, j.SourceId });
        builder.HasIndex(j => j.ReversedEntryId);
    }
}

public class JournalEntryLineConfigration : IEntityTypeConfiguration<JournalEntryLine>
{
    public void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Debit).HasColumnType("decimal(18,2)");
        builder.Property(l => l.Credit).HasColumnType("decimal(18,2)");
        builder.HasIndex(l => l.AccountId);
        builder.HasIndex(l => l.CostCenterId);
        builder.HasIndex(l => new { l.CompanyId, l.RiderId, l.HousingId });
        builder.HasIndex(l => l.BankAccountId);
        builder.HasOne(l => l.BankAccount)
            .WithMany()
            .HasForeignKey(l => l.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompanyBillImportConfigration : IEntityTypeConfiguration<CompanyBillImport>
{
    public void Configure(EntityTypeBuilder<CompanyBillImport> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.CompanyNameSnapshot).HasMaxLength(250);
        builder.Property(i => i.SourceFileName).IsRequired().HasMaxLength(500);
        builder.Property(i => i.UploadedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(i => new { i.Year, i.Month, i.CompanyId, i.TemplateType });
        builder.HasIndex(i => i.Status);
    }
}

public class CompanyBillSheetConfigration : IEntityTypeConfiguration<CompanyBillSheet>
{
    public void Configure(EntityTypeBuilder<CompanyBillSheet> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SheetName).IsRequired().HasMaxLength(200);
        builder.HasIndex(s => new { s.CompanyBillImportId, s.Role });
    }
}

public class CompanyBillRawRowConfigration : IEntityTypeConfiguration<CompanyBillRawRow>
{
    public void Configure(EntityTypeBuilder<CompanyBillRawRow> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.CompanyBillSheetId, r.RowNumber }).IsUnique();
    }
}

public class CompanyBillRawCellConfigration : IEntityTypeConfiguration<CompanyBillRawCell>
{
    public void Configure(EntityTypeBuilder<CompanyBillRawCell> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Header).HasMaxLength(500);
        builder.Property(c => c.NormalizedField).HasMaxLength(100);
        builder.HasIndex(c => new { c.CompanyBillRawRowId, c.ColumnNumber }).IsUnique();
    }
}

public class CompanyBillRiderSummaryConfigration : IEntityTypeConfiguration<CompanyBillRiderSummary>
{
    public void Configure(EntityTypeBuilder<CompanyBillRiderSummary> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SourceRiderId).IsRequired().HasMaxLength(100);
        builder.Property(s => s.SourceRiderName).HasMaxLength(250);
        builder.HasIndex(s => new { s.CompanyBillImportId, s.SourceRiderId });
        builder.HasIndex(s => s.PaidRiderId);
        builder.HasIndex(s => s.ResolutionStatus);
        builder.HasOne(s => s.OriginalRider)
            .WithMany()
            .HasForeignKey(s => s.OriginalRiderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.PaidRider)
            .WithMany()
            .HasForeignKey(s => s.PaidRiderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompanyBillTransactionLineConfigration : IEntityTypeConfiguration<CompanyBillTransactionLine>
{
    public void Configure(EntityTypeBuilder<CompanyBillTransactionLine> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.SourceRiderId).HasMaxLength(100);
        builder.Property(l => l.TransactionType).HasMaxLength(150);
        builder.Property(l => l.WorkId).HasMaxLength(150);
        builder.Property(l => l.FeeType).HasMaxLength(250);
        builder.Property(l => l.TicketId).HasMaxLength(150);
        builder.Property(l => l.ViolationId).HasMaxLength(150);
        builder.HasIndex(l => new { l.CompanyBillImportId, l.SourceRiderId });
        builder.HasIndex(l => l.PaidRiderId);
        builder.HasIndex(l => l.ServiceDate);
        builder.HasOne(l => l.OriginalRider)
            .WithMany()
            .HasForeignKey(l => l.OriginalRiderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.PaidRider)
            .WithMany()
            .HasForeignKey(l => l.PaidRiderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompanyBillDailyMetricConfigration : IEntityTypeConfiguration<CompanyBillDailyMetric>
{
    public void Configure(EntityTypeBuilder<CompanyBillDailyMetric> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.SourceRiderId).IsRequired().HasMaxLength(100);
        builder.HasIndex(m => new { m.CompanyBillImportId, m.SourceRiderId, m.MetricDate });
    }
}

public class RiderEarningConfigration : IEntityTypeConfiguration<RiderEarning>
{
    public void Configure(EntityTypeBuilder<RiderEarning> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SourceType).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => new { e.Year, e.Month, e.PaidRiderId });
        builder.HasIndex(e => new { e.CompanyId, e.Year, e.Month });
        builder.HasOne(e => e.OriginalRider)
            .WithMany()
            .HasForeignKey(e => e.OriginalRiderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.PaidRider)
            .WithMany()
            .HasForeignKey(e => e.PaidRiderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderBonusRuleConfigration : IEntityTypeConfiguration<RiderBonusRule>
{
    public void Configure(EntityTypeBuilder<RiderBonusRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.CompanyId, r.MinimumAcceptedOrders, r.IsActive });
    }
}

public class RiderSalaryRuleConfigration : IEntityTypeConfiguration<RiderSalaryRule>
{
    public void Configure(EntityTypeBuilder<RiderSalaryRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.BaseAmount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ExtraOrderAmount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.BelowThresholdOrderAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(r => new { r.CompanyId, r.TemplateType, r.IsActive, r.EffectiveFrom });
        builder.HasData(new RiderSalaryRule
        {
            Id = 1,
            TemplateType = CompanyBillTemplateType.FtrHunger,
            Name = "Default FTR Hunger salary",
            MinimumAcceptedOrders = 500,
            BaseAmount = 2000,
            ExtraOrderAmount = 6,
            BelowThresholdOrderAmount = 3,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Priority = 0,
            IsActive = true,
            Notes = "Default rule matching the previous hardcoded Hunger/FTR salary formula."
        });
    }
}

public class RiderFinancialItemTypeConfigration : IEntityTypeConfiguration<RiderFinancialItemType>
{
    public void Configure(EntityTypeBuilder<RiderFinancialItemType> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).IsRequired().HasMaxLength(80);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(t => t.Code).IsUnique();
        builder.HasData(
            new RiderFinancialItemType { Id = 1, Code = "WALLET_ADVANCE", Name = "Wallet Advance", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 2, Code = "LOAN", Name = "Loan Installment", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 3, Code = "TRAFFIC_VIOLATION", Name = "Traffic Violation", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 4, Code = "IQAMA_FEE", Name = "Iqama Fee", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 5, Code = "LABOR_OFFICE_FEE", Name = "Labor Office Fee", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 6, Code = "PLANE_TICKET", Name = "Plane Ticket", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 7, Code = "HOUSING_ALLOWANCE", Name = "Housing Allowance", Category = FinancialItemCategory.Allowance, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 8, Code = "ACCESSORY_CHARGE", Name = "Accessory Charge", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 9, Code = "SPARE_PART_CHARGE", Name = "Spare Part Charge", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 10, Code = "PETROL_CHARGE", Name = "Petrol Charge", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 11, Code = "MANUAL_BONUS", Name = "Manual Bonus", Category = FinancialItemCategory.Earning, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 12, Code = "MANUAL_DEDUCTION", Name = "Manual Deduction", Category = FinancialItemCategory.Deduction, IsSystem = true, IsActive = true },
            new RiderFinancialItemType { Id = 13, Code = "OTHER", Name = "Other", Category = FinancialItemCategory.InformationOnly, IsSystem = true, IsActive = true }
        );
    }
}

public class RiderFinancialItemConfigration : IEntityTypeConfiguration<RiderFinancialItem>
{
    public void Configure(EntityTypeBuilder<RiderFinancialItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.CreatedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(i => new { i.Year, i.Month, i.RiderId });
        builder.HasIndex(i => i.RiderFinancialItemTypeId);
        builder.HasOne(i => i.Vehicle)
            .WithMany()
            .HasForeignKey(i => i.VehicleNumber)
            .HasPrincipalKey(v => v.VehicleNumber)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}

public class RiderFinalSettlementConfigration : IEntityTypeConfiguration<RiderFinalSettlement>
{
    public void Configure(EntityTypeBuilder<RiderFinalSettlement> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CreatedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(s => new { s.RiderId, s.Year, s.Month });
    }
}

public class RiderMonthlySalaryConfigration : IEntityTypeConfiguration<RiderMonthlySalary>
{
    public void Configure(EntityTypeBuilder<RiderMonthlySalary> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.GeneratedBy).IsRequired().HasMaxLength(200);
        builder.Property(s => s.IbanSnapshot).HasMaxLength(60);
        builder.HasIndex(s => new { s.RiderId, s.Year, s.Month }).IsUnique();
        builder.HasIndex(s => new { s.Year, s.Month, s.PaymentMethod, s.Status });
    }
}

public class RiderMonthlySalaryLineConfigration : IEntityTypeConfiguration<RiderMonthlySalaryLine>
{
    public void Configure(EntityTypeBuilder<RiderMonthlySalaryLine> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Description).IsRequired().HasMaxLength(300);
        builder.Property(l => l.SourceType).HasMaxLength(100);
        builder.HasIndex(l => new { l.SourceType, l.SourceId });
    }
}

public class RiderSalaryPaymentBatchConfigration : IEntityTypeConfiguration<RiderSalaryPaymentBatch>
{
    public void Configure(EntityTypeBuilder<RiderSalaryPaymentBatch> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.CreatedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(b => new { b.Year, b.Month, b.PaymentMethod, b.Status });
    }
}

public class CashSalaryHandoverBatchConfigration : IEntityTypeConfiguration<CashSalaryHandoverBatch>
{
    public void Configure(EntityTypeBuilder<CashSalaryHandoverBatch> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.CreatedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(b => new { b.Year, b.Month, b.HousingId, b.Status });
    }
}

public class CompanyReceivableConfigration : IEntityTypeConfiguration<CompanyReceivable>
{
    public void Configure(EntityTypeBuilder<CompanyReceivable> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.Year, r.Month, r.CompanyId, r.Status });
        builder.HasIndex(r => r.CompanyBillImportId)
            .IsUnique()
            .HasFilter("[CompanyBillImportId] IS NOT NULL");
    }
}

public class CompanyPaymentReceiptConfigration : IEntityTypeConfiguration<CompanyPaymentReceipt>
{
    public void Configure(EntityTypeBuilder<CompanyPaymentReceipt> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReferenceNumber).HasMaxLength(150);
        builder.Property(r => r.BankAccount).HasMaxLength(200);
        builder.HasIndex(r => new { r.CompanyId, r.ReceiptDate, r.ReferenceNumber, r.BankAccount })
            .IsUnique()
            .HasFilter("[ReferenceNumber] IS NOT NULL");
        builder.HasIndex(r => r.BankAccountId);
        builder.HasOne(r => r.LinkedBankAccount)
            .WithMany()
            .HasForeignKey(r => r.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompanyExpenseCategoryConfigration : IEntityTypeConfiguration<CompanyExpenseCategory>
{
    public void Configure(EntityTypeBuilder<CompanyExpenseCategory> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).IsRequired().HasMaxLength(80);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasData(
            new CompanyExpenseCategory { Id = 1, Code = "HOUSING", Name = "Housing", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 2, Code = "VEHICLE", Name = "Vehicle", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 3, Code = "PETROL", Name = "Petrol", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 4, Code = "SPARE_PARTS", Name = "Spare Parts", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 5, Code = "ACCESSORIES", Name = "Accessories", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 6, Code = "GOVERNMENT_FEES", Name = "Government Fees", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 7, Code = "TICKETS", Name = "Plane Tickets", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 8, Code = "TRAFFIC_VIOLATIONS", Name = "Traffic Violations", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 9, Code = "SUPPLIER_BILLS", Name = "Supplier Bills", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 10, Code = "BANK_FEES", Name = "Bank Fees", IsSystem = true, IsActive = true },
            new CompanyExpenseCategory { Id = 11, Code = "MANUAL", Name = "Manual Expense", IsSystem = true, IsActive = true }
        );
    }
}

public class CompanyExpenseConfigration : IEntityTypeConfiguration<CompanyExpense>
{
    public void Configure(EntityTypeBuilder<CompanyExpense> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => new { e.ExpenseDate, e.CompanyId, e.CompanyExpenseCategoryId });
        builder.HasIndex(e => e.CostCenterId);
        builder.HasIndex(e => e.BankAccountId);
        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleNumber)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.BankAccount)
            .WithMany()
            .HasForeignKey(e => e.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompanyProfitSnapshotConfigration : IEntityTypeConfiguration<CompanyProfitSnapshot>
{
    public void Configure(EntityTypeBuilder<CompanyProfitSnapshot> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.Year, s.Month, s.CompanyId }).IsUnique();
    }
}

public class FixedAssetConfigration : IEntityTypeConfiguration<FixedAsset>
{
    public void Configure(EntityTypeBuilder<FixedAsset> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AssetCode).IsRequired().HasMaxLength(80);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(a => a.AssetCode).IsUnique();
        builder.HasOne(a => a.Vehicle)
            .WithMany()
            .HasForeignKey(a => a.VehicleNumber)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BankAccountConfigration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AccountName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Iban).HasMaxLength(60);
        builder.Property(a => a.BankName).HasMaxLength(200);
    }
}
