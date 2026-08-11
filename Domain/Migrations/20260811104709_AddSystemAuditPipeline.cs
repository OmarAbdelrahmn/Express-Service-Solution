using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemAuditPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RequestPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(900)", maxLength: 900, nullable: false),
                    EntityDisplayName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ChangedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScopeType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ScopeBefore = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ScopeAfter = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditEvents_ActorUserId_OccurredAtUtc",
                table: "SystemAuditEvents",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditEvents_EntityType_EntityKey_OccurredAtUtc",
                table: "SystemAuditEvents",
                columns: new[] { "EntityType", "EntityKey", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditEvents_OccurredAtUtc_Id",
                table: "SystemAuditEvents",
                columns: new[] { "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditEvents_OperationId_OccurredAtUtc",
                table: "SystemAuditEvents",
                columns: new[] { "OperationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditEvents_ScopeType_ScopeAfter_OccurredAtUtc",
                table: "SystemAuditEvents",
                columns: new[] { "ScopeType", "ScopeAfter", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditEvents_ScopeType_ScopeBefore_OccurredAtUtc",
                table: "SystemAuditEvents",
                columns: new[] { "ScopeType", "ScopeBefore", "OccurredAtUtc" });

            migrationBuilder.Sql("""
                INSERT INTO [SystemAuditEvents]
                ([OperationId], [OccurredAtUtc], [ActorType], [ActorUserId], [ActorName], [Source], [OperationName],
                 [EntityType], [EntityKey], [EntityDisplayName], [Action], [ChangedFieldsJson], [OldValuesJson], [NewValuesJson],
                 [ScopeType], [ScopeBefore], [ScopeAfter])
                SELECT
                    NEWID(),
                    ([PerformedAt] AT TIME ZONE 'Arab Standard Time') AT TIME ZONE 'UTC',
                    1,
                    NULL,
                    [PerformedBy],
                    N'LegacyInventory',
                    [Notes],
                    CASE [ItemType]
                        WHEN 1 THEN N'Domain.Entities.Spare.SparePart'
                        ELSE N'Domain.Entities.Spare.RiderAccessory'
                    END,
                    CONCAT(N'Id=', [ItemId]),
                    [ItemName],
                    [Action],
                    N'["Name","Location","Quantity","Price"]',
                    CASE WHEN [Action] IN (2, 3) THEN
                        (SELECT [ItemName] AS [Name], [LocationBefore] AS [Location], [QuantityBefore] AS [Quantity], [PriceBefore] AS [Price]
                         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
                    END,
                    CASE WHEN [Action] IN (1, 2) THEN
                        (SELECT [ItemName] AS [Name], [LocationAfter] AS [Location], [QuantityAfter] AS [Quantity], [PriceAfter] AS [Price]
                         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
                    END,
                    CASE WHEN [LocationBefore] IS NOT NULL OR [LocationAfter] IS NOT NULL THEN N'Location' END,
                    [LocationBefore],
                    [LocationAfter]
                FROM [InventoryAuditLogs];
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_SystemAuditEvents_AppendOnly]
                ON [dbo].[SystemAuditEvents]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'SystemAuditEvents is append-only.', 1;
                END
                """);

            migrationBuilder.DropTable(
                name: "InventoryAuditLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_SystemAuditEvents_AppendOnly];");

            migrationBuilder.DropTable(
                name: "SystemAuditEvents");

            migrationBuilder.CreateTable(
                name: "InventoryAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemType = table.Column<int>(type: "int", nullable: false),
                    LocationAfter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LocationBefore = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PriceAfter = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PriceBefore = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    QuantityAfter = table.Column<int>(type: "int", nullable: true),
                    QuantityBefore = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_ItemType_ItemId",
                table: "InventoryAuditLogs",
                columns: new[] { "ItemType", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_LocationAfter",
                table: "InventoryAuditLogs",
                column: "LocationAfter");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_LocationBefore",
                table: "InventoryAuditLogs",
                column: "LocationBefore");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLogs_PerformedAt",
                table: "InventoryAuditLogs",
                column: "PerformedAt");
        }
    }
}
