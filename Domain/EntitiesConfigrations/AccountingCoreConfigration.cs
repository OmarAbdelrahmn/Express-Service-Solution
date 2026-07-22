using Domain.Entities.AccountingCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class CurrencyConfigration : IEntityTypeConfiguration<Currency>
{ public void Configure(EntityTypeBuilder<Currency> e) { e.ToTable("Currencies"); e.HasKey(x => x.Code); e.Property(x => x.Code).HasMaxLength(3).IsFixedLength(); e.Property(x => x.Name).IsRequired().HasMaxLength(100); } }
public class FinancialUserAccessConfigration : IEntityTypeConfiguration<FinancialUserAccess>
{
    public void Configure(EntityTypeBuilder<FinancialUserAccess> e)
    {
        e.ToTable("FinancialUserAccesses");
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.GrantedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.Permissions).HasConversion<int>();
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.UserId, x.LegalEntityId }).IsUnique();
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class ExchangeRateConfigration : IEntityTypeConfiguration<ExchangeRate>
{ public void Configure(EntityTypeBuilder<ExchangeRate> e) { e.ToTable("ExchangeRates"); e.Property(x => x.FromCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength(); e.Property(x => x.ToCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength(); e.Property(x => x.Rate).HasPrecision(19, 8); e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450); e.HasIndex(x => new { x.LegalEntityId, x.FromCurrencyCode, x.ToCurrencyCode, x.EffectiveDate }).IsUnique(); e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict); } }
public class FinancialDimensionConfigration : IEntityTypeConfiguration<FinancialDimension>
{ public void Configure(EntityTypeBuilder<FinancialDimension> e) { e.ToTable("FinancialDimensions"); e.Property(x => x.Code).IsRequired().HasMaxLength(32); e.Property(x => x.Name).IsRequired().HasMaxLength(100); e.HasIndex(x => new { x.LegalEntityId, x.Code }).IsUnique(); e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict); } }
public class FinancialDimensionValueConfigration : IEntityTypeConfiguration<FinancialDimensionValue>
{ public void Configure(EntityTypeBuilder<FinancialDimensionValue> e) { e.ToTable("FinancialDimensionValues"); e.Property(x => x.Code).IsRequired().HasMaxLength(32); e.Property(x => x.Name).IsRequired().HasMaxLength(100); e.HasIndex(x => new { x.FinancialDimensionId, x.Code }).IsUnique(); e.HasOne(x => x.FinancialDimension).WithMany(x => x.Values).HasForeignKey(x => x.FinancialDimensionId).OnDelete(DeleteBehavior.Restrict); } }

public class AccountingAccountConfigration : IEntityTypeConfiguration<AccountingAccount>
{
    public void Configure(EntityTypeBuilder<AccountingAccount> e)
    {
        e.ToTable("AccountingAccounts");
        e.Property(x => x.Code).IsRequired().HasMaxLength(32);
        e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        e.HasIndex(x => new { x.LegalEntityId, x.Code }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ParentAccount).WithMany(x => x.ChildAccounts).HasForeignKey(x => x.ParentAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PostingProfileConfigration : IEntityTypeConfiguration<PostingProfile>
{
    public void Configure(EntityTypeBuilder<PostingProfile> e)
    {
        e.ToTable("PostingProfiles");
        e.Property(x => x.Code).IsRequired().HasMaxLength(64);
        e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        e.HasIndex(x => new { x.LegalEntityId, x.Code, x.Version }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PostingProfileLineConfigration : IEntityTypeConfiguration<PostingProfileLine>
{
    public void Configure(EntityTypeBuilder<PostingProfileLine> e)
    {
        e.ToTable("PostingProfileLines");
        e.Property(x => x.EventCode).IsRequired().HasMaxLength(64);
        e.HasIndex(x => new { x.PostingProfileId, x.EventCode }).IsUnique();
        e.HasOne(x => x.PostingProfile).WithMany(x => x.Lines).HasForeignKey(x => x.PostingProfileId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.DebitAccount).WithMany().HasForeignKey(x => x.DebitAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.CreditAccount).WithMany().HasForeignKey(x => x.CreditAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FiscalYearConfigration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> e)
    {
        e.ToTable("FiscalYears", t => t.HasCheckConstraint("CK_FiscalYears_DateRange", "[EndDate] >= [StartDate]"));
        e.Property(x => x.Name).IsRequired().HasMaxLength(64);
        e.HasIndex(x => new { x.LegalEntityId, x.Name }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FiscalPeriodConfigration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> e)
    {
        e.ToTable("FiscalPeriods", t => t.HasCheckConstraint("CK_FiscalPeriods_DateRange", "[EndDate] >= [StartDate]"));
        e.Property(x => x.Name).IsRequired().HasMaxLength(64);
        e.Property(x => x.ClosedBy).HasMaxLength(450);
        e.Property(x => x.CloseReason).HasMaxLength(1000);
        e.Property(x => x.ReopenReason).HasMaxLength(1000);
        e.Property(x => x.ReopenedBy).HasMaxLength(450);
        e.HasIndex(x => new { x.FiscalYearId, x.PeriodNumber }).IsUnique();
        e.HasOne(x => x.FiscalYear).WithMany(x => x.Periods).HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FinancialDocumentConfigration : IEntityTypeConfiguration<FinancialDocument>
{
    public void Configure(EntityTypeBuilder<FinancialDocument> e)
    {
        e.ToTable("FinancialDocuments");
        e.Property(x => x.DocumentType).IsRequired().HasMaxLength(64);
        e.Property(x => x.DocumentNumber).IsRequired().HasMaxLength(64);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        e.Property(x => x.RequestHash).IsRequired().HasMaxLength(64).IsFixedLength();
        e.Property(x => x.CorrelationId).IsRequired().HasMaxLength(64);
        e.Property(x => x.SourceReference).HasMaxLength(128);
        e.Property(x => x.PostingProfileCode).HasMaxLength(64);
        e.Property(x => x.Description).IsRequired().HasMaxLength(500);
        e.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        e.Property(x => x.BaseCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        e.Property(x => x.ExchangeRate).HasPrecision(19, 8);
        e.Property(x => x.RoundingTraceJson).IsRequired();
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.SubmittedBy).HasMaxLength(450);
        e.Property(x => x.ApprovedBy).HasMaxLength(450);
        e.Property(x => x.PostedBy).HasMaxLength(450);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.LegalEntityId, x.DocumentNumber }).IsUnique();
        e.HasIndex(x => new { x.LegalEntityId, x.DocumentType, x.IdempotencyKey }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ReversalOfDocument).WithMany().HasForeignKey(x => x.ReversalOfDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LegalEntityDocumentSequenceConfigration : IEntityTypeConfiguration<LegalEntityDocumentSequence>
{
    public void Configure(EntityTypeBuilder<LegalEntityDocumentSequence> e)
    {
        e.ToTable("LegalEntityDocumentSequences"); e.Property(x => x.DocumentType).IsRequired().HasMaxLength(64); e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.LegalEntityId, x.DocumentType }).IsUnique();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FinancialDocumentLineConfigration : IEntityTypeConfiguration<FinancialDocumentLine>
{
    public void Configure(EntityTypeBuilder<FinancialDocumentLine> e)
    {
        e.ToTable("FinancialDocumentLines", t => t.HasCheckConstraint("CK_FinancialDocumentLines_OneSide", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)"));
        e.Property(x => x.Debit).HasPrecision(19, 4); e.Property(x => x.Credit).HasPrecision(19, 4); e.Property(x => x.BaseDebit).HasPrecision(19, 4); e.Property(x => x.BaseCredit).HasPrecision(19, 4);
        e.Property(x => x.Description).HasMaxLength(500);
        e.HasIndex(x => new { x.FinancialDocumentId, x.LineNumber }).IsUnique();
        e.HasOne(x => x.FinancialDocument).WithMany(x => x.Lines).HasForeignKey(x => x.FinancialDocumentId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class FinancialDocumentLineDimensionConfigration : IEntityTypeConfiguration<FinancialDocumentLineDimension>
{ public void Configure(EntityTypeBuilder<FinancialDocumentLineDimension> e) { e.ToTable("FinancialDocumentLineDimensions"); e.HasKey(x => new { x.FinancialDocumentLineId, x.FinancialDimensionValueId }); e.HasOne(x => x.FinancialDocumentLine).WithMany(x => x.Dimensions).HasForeignKey(x => x.FinancialDocumentLineId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.FinancialDimensionValue).WithMany().HasForeignKey(x => x.FinancialDimensionValueId).OnDelete(DeleteBehavior.Restrict); } }

public class DocumentApprovalConfigration : IEntityTypeConfiguration<DocumentApproval>
{
    public void Configure(EntityTypeBuilder<DocumentApproval> e)
    {
        e.ToTable("DocumentApprovals"); e.Property(x => x.ApprovedBy).IsRequired().HasMaxLength(450); e.Property(x => x.Comment).HasMaxLength(1000);
        e.HasIndex(x => new { x.FinancialDocumentId, x.StepNumber }).IsUnique();
        e.HasOne(x => x.FinancialDocument).WithMany(x => x.Approvals).HasForeignKey(x => x.FinancialDocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PostingBatchConfigration : IEntityTypeConfiguration<PostingBatch>
{
    public void Configure(EntityTypeBuilder<PostingBatch> e)
    {
        e.ToTable("PostingBatches"); e.Property(x => x.PostingKey).IsRequired().HasMaxLength(128); e.Property(x => x.PostedBy).IsRequired().HasMaxLength(450);
        e.HasIndex(x => new { x.LegalEntityId, x.PostingKey }).IsUnique(); e.HasIndex(x => x.FinancialDocumentId).IsUnique();
        e.HasOne(x => x.FinancialDocument).WithMany().HasForeignKey(x => x.FinancialDocumentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ReversalOfPostingBatch).WithMany().HasForeignKey(x => x.ReversalOfPostingBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class JournalEntryConfigration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> e)
    {
        e.ToTable("JournalEntries"); e.Property(x => x.EntryNumber).IsRequired().HasMaxLength(64); e.Property(x => x.Description).IsRequired().HasMaxLength(500); e.Property(x => x.IsFinalized).HasDefaultValue(false);
        e.HasIndex(x => new { x.LegalEntityId, x.EntryNumber }).IsUnique();
        e.HasOne(x => x.PostingBatch).WithMany(x => x.JournalEntries).HasForeignKey(x => x.PostingBatchId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class JournalLineConfigration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> e)
    {
        e.ToTable("JournalLines", t => t.HasCheckConstraint("CK_JournalLines_OneSide", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)"));
        e.Property(x => x.Debit).HasPrecision(19, 4); e.Property(x => x.Credit).HasPrecision(19, 4); e.Property(x => x.BaseDebit).HasPrecision(19, 4); e.Property(x => x.BaseCredit).HasPrecision(19, 4); e.Property(x => x.Description).HasMaxLength(500);
        e.HasIndex(x => new { x.JournalEntryId, x.LineNumber }).IsUnique();
        e.HasOne(x => x.JournalEntry).WithMany(x => x.Lines).HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
public class JournalLineDimensionConfigration : IEntityTypeConfiguration<JournalLineDimension>
{ public void Configure(EntityTypeBuilder<JournalLineDimension> e) { e.ToTable("JournalLineDimensions"); e.HasKey(x => new { x.JournalLineId, x.FinancialDimensionValueId }); e.HasOne(x => x.JournalLine).WithMany(x => x.Dimensions).HasForeignKey(x => x.JournalLineId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.FinancialDimensionValue).WithMany().HasForeignKey(x => x.FinancialDimensionValueId).OnDelete(DeleteBehavior.Restrict); } }
public class RecurringJournalScheduleConfigration : IEntityTypeConfiguration<RecurringJournalSchedule>
{ public void Configure(EntityTypeBuilder<RecurringJournalSchedule> e) { e.ToTable("RecurringJournalSchedules"); e.Property(x => x.DocumentType).IsRequired().HasMaxLength(64); e.Property(x => x.Description).IsRequired().HasMaxLength(500); e.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength(); e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450); e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict); } }
public class RecurringJournalScheduleLineConfigration : IEntityTypeConfiguration<RecurringJournalScheduleLine>
{ public void Configure(EntityTypeBuilder<RecurringJournalScheduleLine> e) { e.ToTable("RecurringJournalScheduleLines", t => t.HasCheckConstraint("CK_RecurringJournalScheduleLines_OneSide", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)")); e.Property(x => x.Debit).HasPrecision(19, 4); e.Property(x => x.Credit).HasPrecision(19, 4); e.Property(x => x.Description).HasMaxLength(500); e.HasIndex(x => new { x.RecurringJournalScheduleId, x.LineNumber }).IsUnique(); e.HasOne(x => x.RecurringJournalSchedule).WithMany(x => x.Lines).HasForeignKey(x => x.RecurringJournalScheduleId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict); } }

public class AccountingAuditEventConfigration : IEntityTypeConfiguration<AccountingAuditEvent>
{
    public void Configure(EntityTypeBuilder<AccountingAuditEvent> e)
    {
        e.ToTable("AccountingAuditEvents"); e.Property(x => x.EventType).IsRequired().HasMaxLength(128); e.Property(x => x.ActorId).IsRequired().HasMaxLength(450); e.Property(x => x.PayloadJson).IsRequired(); e.Property(x => x.PreviousHash).IsRequired().HasMaxLength(64); e.Property(x => x.Hash).IsRequired().HasMaxLength(64);
        e.HasIndex(x => new { x.LegalEntityId, x.Id }).IsUnique(); e.HasIndex(x => x.Hash).IsUnique();
    }
}

public class AccountingOutboxMessageConfigration : IEntityTypeConfiguration<AccountingOutboxMessage>
{
    public void Configure(EntityTypeBuilder<AccountingOutboxMessage> e)
    {
        e.ToTable("AccountingOutboxMessages"); e.Property(x => x.Type).IsRequired().HasMaxLength(128); e.Property(x => x.PayloadJson).IsRequired(); e.Property(x => x.LastError).HasMaxLength(2000); e.Property(x => x.LockedBy).HasMaxLength(128); e.Property(x => x.CorrelationId).IsRequired().HasMaxLength(64); e.HasIndex(x => new { x.ProcessedAt, x.DeadLetteredAt, x.NextAttemptAt, x.LockedUntil, x.OccurredAt });
    }
}

public class AccountingAuditChainHeadConfigration : IEntityTypeConfiguration<AccountingAuditChainHead>
{
    public void Configure(EntityTypeBuilder<AccountingAuditChainHead> e)
    {
        e.ToTable("AccountingAuditChainHeads");
        e.HasKey(x => x.LegalEntityId);
        e.Property(x => x.LastHash).IsRequired().HasMaxLength(64);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
