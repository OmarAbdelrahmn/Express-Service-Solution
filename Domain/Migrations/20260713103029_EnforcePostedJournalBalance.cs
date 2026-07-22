using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePostedJournalBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFinalized",
                table: "JournalEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_JournalEntries_Finalization] ON [JournalEntries] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [IsFinalized] = 1)
                        THROW 51000, 'Finalized journal entries are immutable.', 1;

                    IF EXISTS (
                        SELECT 1
                        FROM inserted i
                        JOIN deleted d ON d.[Id] = i.[Id]
                        OUTER APPLY (
                            SELECT SUM([Debit]) AS [Debit], SUM([Credit]) AS [Credit]
                            FROM [JournalLines] l WHERE l.[JournalEntryId] = i.[Id]
                        ) totals
                        WHERE d.[IsFinalized] = 0 AND i.[IsFinalized] = 1
                          AND (ISNULL(totals.[Debit], 0) <> ISNULL(totals.[Credit], 0) OR ISNULL(totals.[Debit], 0) = 0)
                    )
                        THROW 51001, 'A journal entry must be balanced before finalization.', 1;
                END
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_JournalLines_ImmutableWhenFinalized] ON [JournalLines] AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM (SELECT [JournalEntryId] FROM inserted UNION SELECT [JournalEntryId] FROM deleted) changed
                        JOIN [JournalEntries] entry ON entry.[Id] = changed.[JournalEntryId]
                        WHERE entry.[IsFinalized] = 1
                    )
                        THROW 51002, 'Lines of finalized journal entries are immutable.', 1;
                END
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AccountingAuditEvents_AppendOnly] ON [AccountingAuditEvents] AFTER UPDATE, DELETE AS
                BEGIN
                    THROW 51003, 'Accounting audit events are append-only.', 1;
                END
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_PostingBatches_AppendOnly] ON [PostingBatches] AFTER UPDATE, DELETE AS
                BEGIN
                    THROW 51004, 'Posting batches are immutable.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_PostingBatches_AppendOnly]");
            migrationBuilder.Sql("DROP TRIGGER [TR_AccountingAuditEvents_AppendOnly]");
            migrationBuilder.Sql("DROP TRIGGER [TR_JournalLines_ImmutableWhenFinalized]");
            migrationBuilder.Sql("DROP TRIGGER [TR_JournalEntries_Finalization]");

            migrationBuilder.DropColumn(
                name: "IsFinalized",
                table: "JournalEntries");
        }
    }
}
