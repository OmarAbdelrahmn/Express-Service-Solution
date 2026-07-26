using Domain.Entities.AccountingPlatform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class AccountingStoredFileConfigration : IEntityTypeConfiguration<AccountingStoredFile>
{
    public void Configure(EntityTypeBuilder<AccountingStoredFile> e)
    {
        e.ToTable("AccountingStoredFiles", t => t.HasCheckConstraint("CK_AccountingStoredFiles_Length", "[PlaintextLength] >= 0"));
        e.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(260);
        e.Property(x => x.ContentType).IsRequired().HasMaxLength(128);
        e.Property(x => x.Sha256).IsRequired().HasMaxLength(64).IsFixedLength();
        e.Property(x => x.StorageLocator).IsRequired().HasMaxLength(512);
        e.Property(x => x.EncryptionKeyId).IsRequired().HasMaxLength(64);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.HasIndex(x => new { x.LegalEntityId, x.Sha256 }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformImportTemplateConfigration : IEntityTypeConfiguration<PlatformImportTemplate>
{
    public void Configure(EntityTypeBuilder<PlatformImportTemplate> e)
    {
        e.ToTable("PlatformImportTemplates", t => t.HasCheckConstraint("CK_PlatformImportTemplates_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        e.Property(x => x.Code).IsRequired().HasMaxLength(64);
        e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        e.Property(x => x.AdapterKey).IsRequired().HasMaxLength(128);
        e.Property(x => x.SchemaFingerprint).IsRequired().HasMaxLength(64);
        e.Property(x => x.ConfigurationJson).IsRequired();
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.ActivatedBy).HasMaxLength(450);
        e.HasIndex(x => new { x.LegalEntityId, x.PlatformAccountId, x.Code, x.Version }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.PlatformAccount).WithMany().HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformImportBatchConfigration : IEntityTypeConfiguration<PlatformImportBatch>
{
    public void Configure(EntityTypeBuilder<PlatformImportBatch> e)
    {
        e.ToTable("PlatformImportBatches", t => t.HasCheckConstraint("CK_PlatformImportBatches_Dates", "[PeriodEnd] >= [PeriodStart]"));
        e.Property(x => x.ExternalReference).IsRequired().HasMaxLength(128);
        e.Property(x => x.ParserVersion).IsRequired().HasMaxLength(64);
        e.Property(x => x.SchemaFingerprint).IsRequired().HasMaxLength(64);
        e.Property(x => x.SourceControlTotal).HasPrecision(19, 4);
        e.Property(x => x.NormalizedControlTotal).HasPrecision(19, 4);
        e.Property(x => x.FailureReason).HasMaxLength(2000);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.ReviewedBy).HasMaxLength(450);
        e.Property(x => x.RowVersion).IsRowVersion();
        // A rejected import is a historical attempt and must not block a corrected re-upload.
        e.HasIndex(x => new { x.LegalEntityId, x.PlatformAccountId, x.ExternalReference }).IsUnique().HasFilter("[Status] <> 6");
        e.HasIndex(x => new { x.LegalEntityId, x.PlatformAccountId, x.StoredFileId }).IsUnique().HasFilter("[Status] <> 6");
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.PlatformAccount).WithMany().HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.SupersedesBatch).WithMany().HasForeignKey(x => x.SupersedesBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformImportSheetConfigration : IEntityTypeConfiguration<PlatformImportSheet>
{
    public void Configure(EntityTypeBuilder<PlatformImportSheet> e)
    {
        e.ToTable("PlatformImportSheets");
        e.Property(x => x.Name).IsRequired().HasMaxLength(128);
        e.HasIndex(x => new { x.PlatformImportBatchId, x.SheetIndex }).IsUnique();
        e.HasOne(x => x.PlatformImportBatch).WithMany(x => x.Sheets).HasForeignKey(x => x.PlatformImportBatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PlatformImportRawRowConfigration : IEntityTypeConfiguration<PlatformImportRawRow>
{
    public void Configure(EntityTypeBuilder<PlatformImportRawRow> e)
    {
        e.ToTable("PlatformImportRawRows");
        e.Property(x => x.RowHash).IsRequired().HasMaxLength(64).IsFixedLength();
        e.HasIndex(x => new { x.PlatformImportSheetId, x.RowNumber }).IsUnique();
        e.HasOne(x => x.PlatformImportSheet).WithMany(x => x.Rows).HasForeignKey(x => x.PlatformImportSheetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PlatformImportRawCellConfigration : IEntityTypeConfiguration<PlatformImportRawCell>
{
    public void Configure(EntityTypeBuilder<PlatformImportRawCell> e)
    {
        e.ToTable("PlatformImportRawCells");
        e.Property(x => x.CellReference).IsRequired().HasMaxLength(32);
        e.Property(x => x.RawValue).HasMaxLength(4000);
        e.Property(x => x.DisplayValue).HasMaxLength(4000);
        e.Property(x => x.Formula).HasMaxLength(4000);
        e.Property(x => x.DataType).IsRequired().HasMaxLength(32);
        e.HasIndex(x => new { x.PlatformImportRawRowId, x.ColumnNumber }).IsUnique();
        e.HasOne(x => x.PlatformImportRawRow).WithMany(x => x.Cells).HasForeignKey(x => x.PlatformImportRawRowId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PlatformNormalizedFactConfigration : IEntityTypeConfiguration<PlatformNormalizedFact>
{
    public void Configure(EntityTypeBuilder<PlatformNormalizedFact> e)
    {
        e.ToTable("PlatformNormalizedFacts");
        e.Property(x => x.ExternalWorkerId).IsRequired().HasMaxLength(128);
        e.Property(x => x.WorkerCategory).IsRequired().HasMaxLength(64);
        e.Property(x => x.MetricCode).IsRequired().HasMaxLength(64);
        e.Property(x => x.NumericValue).HasPrecision(19, 4);
        e.Property(x => x.TextValue).HasMaxLength(2000);
        e.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        e.Property(x => x.LineageJson).IsRequired();
        e.HasIndex(x => new { x.PlatformImportBatchId, x.RiderIqamaNo, x.FactDate, x.MetricCode });
        e.HasOne(x => x.PlatformImportBatch).WithMany(x => x.Facts).HasForeignKey(x => x.PlatformImportBatchId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.SourceRawRow).WithMany().HasForeignKey(x => x.SourceRawRowId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformImportIssueConfigration : IEntityTypeConfiguration<PlatformImportIssue>
{
    public void Configure(EntityTypeBuilder<PlatformImportIssue> e)
    {
        e.ToTable("PlatformImportIssues");
        e.Property(x => x.Code).IsRequired().HasMaxLength(64);
        e.Property(x => x.Message).IsRequired().HasMaxLength(2000);
        e.Property(x => x.Resolution).HasMaxLength(2000);
        e.Property(x => x.ResolvedBy).HasMaxLength(450);
        e.HasIndex(x => new { x.PlatformImportBatchId, x.Status, x.Severity });
        e.HasOne(x => x.PlatformImportBatch).WithMany(x => x.Issues).HasForeignKey(x => x.PlatformImportBatchId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.SourceRawRow).WithMany().HasForeignKey(x => x.SourceRawRowId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformWorkerIdentityConfigration : IEntityTypeConfiguration<PlatformWorkerIdentity>
{
    public void Configure(EntityTypeBuilder<PlatformWorkerIdentity> e)
    {
        e.ToTable("PlatformWorkerIdentities", t =>
        {
            t.HasCheckConstraint("CK_PlatformWorkerIdentities_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            // SQL Server rejects OUTPUT without INTO when this table's overlap trigger is enabled.
            t.UseSqlOutputClause(false);
        });
        e.Property(x => x.ExternalWorkerId).IsRequired().HasMaxLength(128);
        e.Property(x => x.Reason).HasMaxLength(1000);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.HasIndex(x => new { x.LegalEntityId, x.PlatformAccountId, x.ExternalWorkerId, x.EffectiveFrom }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.PlatformAccount).WithMany().HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformFactOverrideConfigration : IEntityTypeConfiguration<PlatformFactOverride>
{
    public void Configure(EntityTypeBuilder<PlatformFactOverride> e)
    {
        e.ToTable("PlatformFactOverrides");
        e.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.HasIndex(x => x.PlatformNormalizedFactId).IsUnique();
        e.HasOne(x => x.PlatformNormalizedFact).WithOne(x => x.Override).HasForeignKey<PlatformFactOverride>(x => x.PlatformNormalizedFactId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompensationPolicyVersionConfigration : IEntityTypeConfiguration<CompensationPolicyVersion>
{
    public void Configure(EntityTypeBuilder<CompensationPolicyVersion> e)
    {
        // SQL Server has TR_CompensationPolicyVersions_NoActiveOverlap on this table.
        // Disable EF Core's OUTPUT clause so inserts/updates work with that trigger.
        e.ToTable("CompensationPolicyVersions", t =>
        {
            t.HasCheckConstraint("CK_CompensationPolicyVersions_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            t.UseSqlOutputClause(false);
        });
        e.Property(x => x.WorkerCategory).IsRequired().HasMaxLength(64);
        e.Property(x => x.Code).IsRequired().HasMaxLength(64);
        e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.ActivatedBy).HasMaxLength(450);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.LegalEntityId, x.PlatformAccountId, x.WorkerCategory, x.Code, x.Version }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.PlatformAccount).WithMany().HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CompensationRuleConfigration : IEntityTypeConfiguration<CompensationRule>
{
    public void Configure(EntityTypeBuilder<CompensationRule> e)
    {
        e.ToTable("CompensationRules", t => t.HasCheckConstraint("CK_CompensationRules_Rounding", "[RoundingScale] >= 0 AND [RoundingScale] <= 4"));
        e.Property(x => x.Code).IsRequired().HasMaxLength(64);
        e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        e.Property(x => x.MetricCode).IsRequired().HasMaxLength(64);
        e.Property(x => x.ConditionMetricCode).HasMaxLength(64);
        e.Property(x => x.ConditionOperator).HasMaxLength(8);
        e.Property(x => x.TargetComponentCode).HasMaxLength(64);
        e.Property(x => x.ExclusiveGroup).HasMaxLength(64);
        foreach (var property in new[] { nameof(CompensationRule.ConditionValue), nameof(CompensationRule.LowerBound), nameof(CompensationRule.UpperBound), nameof(CompensationRule.Rate), nameof(CompensationRule.BelowRate), nameof(CompensationRule.AboveRate), nameof(CompensationRule.FixedAmount), nameof(CompensationRule.BaseAmount) })
            e.Property<decimal?>(property).HasPrecision(19, 4);
        e.HasIndex(x => new { x.CompensationPolicyVersionId, x.Code }).IsUnique();
        e.HasOne(x => x.CompensationPolicyVersion).WithMany(x => x.Rules).HasForeignKey(x => x.CompensationPolicyVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RiderPayrollRunConfigration : IEntityTypeConfiguration<RiderPayrollRun>
{
    public void Configure(EntityTypeBuilder<RiderPayrollRun> e)
    {
        e.ToTable("RiderPayrollRuns", t => t.HasCheckConstraint("CK_RiderPayrollRuns_Dates", "[PeriodEnd] >= [PeriodStart]"));
        e.Property(x => x.RunNumber).IsRequired().HasMaxLength(64);
        e.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        e.Property(x => x.GrossEarnings).HasPrecision(19, 2);
        e.Property(x => x.AppliedDeductions).HasPrecision(19, 2);
        e.Property(x => x.CarriedDeductions).HasPrecision(19, 2);
        e.Property(x => x.NetPay).HasPrecision(19, 2);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.ApprovedBy).HasMaxLength(450);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.LegalEntityId, x.RunNumber }).IsUnique();
        e.HasIndex(x => new { x.LegalEntityId, x.PeriodStart, x.PeriodEnd }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.AccrualFinancialDocument).WithMany().HasForeignKey(x => x.AccrualFinancialDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderPayrollLineConfigration : IEntityTypeConfiguration<RiderPayrollLine>
{
    public void Configure(EntityTypeBuilder<RiderPayrollLine> e)
    {
        e.ToTable("RiderPayrollLines", t => t.HasCheckConstraint("CK_RiderPayrollLines_Amounts", "[GrossEarnings] >= 0 AND [AppliedDeductions] >= 0 AND [CarriedDeductions] >= 0 AND [NetPay] >= 0"));
        e.Property(x => x.GrossEarnings).HasPrecision(19, 2);
        e.Property(x => x.AppliedDeductions).HasPrecision(19, 2);
        e.Property(x => x.CarriedDeductions).HasPrecision(19, 2);
        e.Property(x => x.NetPay).HasPrecision(19, 2);
        e.Property(x => x.HoldReason).HasMaxLength(1000);
        e.HasIndex(x => new { x.RiderPayrollRunId, x.RiderIqamaNo }).IsUnique();
        e.HasOne(x => x.RiderPayrollRun).WithMany(x => x.Lines).HasForeignKey(x => x.RiderPayrollRunId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RiderPayrollComponentConfigration : IEntityTypeConfiguration<RiderPayrollComponent>
{
    public void Configure(EntityTypeBuilder<RiderPayrollComponent> e)
    {
        e.ToTable("RiderPayrollComponents");
        e.Property(x => x.ComponentCode).IsRequired().HasMaxLength(64);
        e.Property(x => x.Description).IsRequired().HasMaxLength(500);
        e.Property(x => x.Quantity).HasPrecision(19, 4);
        e.Property(x => x.Rate).HasPrecision(19, 4);
        e.Property(x => x.Amount).HasPrecision(19, 2);
        e.Property(x => x.CalculationJson).IsRequired();
        e.HasIndex(x => new { x.RiderPayrollLineId, x.ComponentCode, x.PlatformAccountId });
        e.HasOne(x => x.RiderPayrollLine).WithMany(x => x.Components).HasForeignKey(x => x.RiderPayrollLineId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.PlatformAccount).WithMany().HasForeignKey(x => x.PlatformAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.CompensationPolicyVersion).WithMany().HasForeignKey(x => x.CompensationPolicyVersionId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.CompensationRule).WithMany().HasForeignKey(x => x.CompensationRuleId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.SourceImportBatch).WithMany().HasForeignKey(x => x.SourceImportBatchId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.RiderFinancialItem).WithMany().HasForeignKey(x => x.RiderFinancialItemId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.RiderPayrollCarryForward).WithMany().HasForeignKey(x => x.RiderPayrollCarryForwardId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderPayrollCarryForwardConfigration : IEntityTypeConfiguration<RiderPayrollCarryForward>
{
    public void Configure(EntityTypeBuilder<RiderPayrollCarryForward> e)
    {
        e.ToTable("RiderPayrollCarryForwards", t => t.HasCheckConstraint("CK_RiderPayrollCarryForwards_Amounts", "[OriginalAmount] > 0 AND [OutstandingAmount] >= 0 AND [OutstandingAmount] <= [OriginalAmount]"));
        e.Property(x => x.SourceCode).IsRequired().HasMaxLength(64);
        e.Property(x => x.Description).IsRequired().HasMaxLength(500);
        e.Property(x => x.OriginalAmount).HasPrecision(19, 2);
        e.Property(x => x.OutstandingAmount).HasPrecision(19, 2);
        e.HasIndex(x => new { x.LegalEntityId, x.RiderIqamaNo, x.Status });
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.CreatedFromPayrollRun).WithMany().HasForeignKey(x => x.CreatedFromPayrollRunId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderPayrollAdjustmentConfigration : IEntityTypeConfiguration<RiderPayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<RiderPayrollAdjustment> e)
    {
        e.ToTable("RiderPayrollAdjustments", t => t.HasCheckConstraint("CK_RiderPayrollAdjustments_NonZero", "[Amount] <> 0"));
        e.Property(x => x.Amount).HasPrecision(19, 2);
        e.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        e.Property(x => x.Notes).HasMaxLength(2000);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.HasOne(x => x.RiderPayrollLine).WithMany().HasForeignKey(x => x.RiderPayrollLineId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.EvidenceFile).WithMany().HasForeignKey(x => x.EvidenceFileId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderFinancialItemTypeConfigration : IEntityTypeConfiguration<RiderFinancialItemType>
{
    public void Configure(EntityTypeBuilder<RiderFinancialItemType> e)
    {
        e.ToTable("RiderFinancialItemTypes");
        e.Property(x => x.Code).IsRequired().HasMaxLength(64);
        e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        e.HasIndex(x => new { x.LegalEntityId, x.Code }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.LedgerAccount).WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderFinancialItemConfigration : IEntityTypeConfiguration<RiderFinancialItem>
{
    public void Configure(EntityTypeBuilder<RiderFinancialItem> e)
    {
        e.ToTable("RiderFinancialItems", t => t.HasCheckConstraint("CK_RiderFinancialItems_Amounts", "[OriginalAmount] > 0 AND [OutstandingAmount] >= 0 AND [OutstandingAmount] <= [OriginalAmount]"));
        e.Property(x => x.Reference).IsRequired().HasMaxLength(128);
        e.Property(x => x.Description).IsRequired().HasMaxLength(500);
        e.Property(x => x.OriginalAmount).HasPrecision(19, 2);
        e.Property(x => x.OutstandingAmount).HasPrecision(19, 2);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.LegalEntityId, x.Reference }).IsUnique();
        e.HasIndex(x => new { x.LegalEntityId, x.RiderIqamaNo, x.Status });
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ItemType).WithMany().HasForeignKey(x => x.RiderFinancialItemTypeId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.EvidenceFile).WithMany().HasForeignKey(x => x.EvidenceFileId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderFinancialInstallmentConfigration : IEntityTypeConfiguration<RiderFinancialInstallment>
{
    public void Configure(EntityTypeBuilder<RiderFinancialInstallment> e)
    {
        e.ToTable("RiderFinancialInstallments", t => t.HasCheckConstraint("CK_RiderFinancialInstallments_Amounts", "[ScheduledAmount] > 0 AND [AppliedAmount] >= 0 AND [AppliedAmount] <= [ScheduledAmount]"));
        e.Property(x => x.ScheduledAmount).HasPrecision(19, 2);
        e.Property(x => x.AppliedAmount).HasPrecision(19, 2);
        e.HasIndex(x => new { x.RiderFinancialItemId, x.Sequence }).IsUnique();
        e.HasOne(x => x.RiderFinancialItem).WithMany(x => x.Installments).HasForeignKey(x => x.RiderFinancialItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RiderPaymentBatchConfigration : IEntityTypeConfiguration<RiderPaymentBatch>
{
    public void Configure(EntityTypeBuilder<RiderPaymentBatch> e)
    {
        e.ToTable("RiderPaymentBatches");
        e.Property(x => x.BatchNumber).IsRequired().HasMaxLength(64);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.HasIndex(x => new { x.LegalEntityId, x.BatchNumber }).IsUnique();
        e.HasOne<RiderPayrollRun>().WithMany().HasForeignKey(x => x.RiderPayrollRunId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ExportFile).WithMany().HasForeignKey(x => x.ExportFileId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.PaymentFinancialDocument).WithMany().HasForeignKey(x => x.PaymentFinancialDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RiderPaymentBatchLineConfigration : IEntityTypeConfiguration<RiderPaymentBatchLine>
{
    public void Configure(EntityTypeBuilder<RiderPaymentBatchLine> e)
    {
        e.ToTable("RiderPaymentBatchLines", t => t.HasCheckConstraint("CK_RiderPaymentBatchLines_Amount", "[Amount] > 0"));
        e.Property(x => x.Amount).HasPrecision(19, 2);
        e.Property(x => x.IbanSnapshot).HasMaxLength(34);
        e.Property(x => x.RejectionReason).HasMaxLength(1000);
        e.Property(x => x.ConfirmedBy).HasMaxLength(450);
        e.HasIndex(x => new { x.RiderPaymentBatchId, x.RiderPayrollLineId }).IsUnique();
        e.HasOne(x => x.RiderPaymentBatch).WithMany(x => x.Lines).HasForeignKey(x => x.RiderPaymentBatchId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.RiderPayrollLine).WithMany().HasForeignKey(x => x.RiderPayrollLineId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.PaymentFinancialDocument).WithMany().HasForeignKey(x => x.PaymentFinancialDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class HousingCashUserAccessConfigration : IEntityTypeConfiguration<HousingCashUserAccess>
{
    public void Configure(EntityTypeBuilder<HousingCashUserAccess> e)
    {
        e.ToTable("HousingCashUserAccesses");
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.GrantedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.UserId, x.LegalEntityId, x.HousingId }).IsUnique();
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Housing).WithMany().HasForeignKey(x => x.HousingId).OnDelete(DeleteBehavior.Restrict);
    }
}
