using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    TargetRiderId = table.Column<int>(type: "int", nullable: true),
                    IdempotencyScope = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpperRiderId = table.Column<int>(type: "int", nullable: false),
                    LastRiderId = table.Column<int>(type: "int", nullable: false),
                    ExpansionComplete = table.Column<bool>(type: "bit", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiderPushDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProtectedDestination = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DestinationHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderPushDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderPushDevices_RiderMobileSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "RiderMobileSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    NoDevice = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationRecipients_NotificationRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "NotificationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceGeneration = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_NotificationRecipients_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "NotificationRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_RiderPushDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "RiderPushDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_DeviceId",
                table: "NotificationDeliveries",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_RecipientId_DeviceId_DeviceGeneration",
                table: "NotificationDeliveries",
                columns: new[] { "RecipientId", "DeviceId", "DeviceGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptAtUtc_LeaseUntilUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecipients_RequestId_RiderId",
                table: "NotificationRecipients",
                columns: new[] { "RequestId", "RiderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRequests_EventKey",
                table: "NotificationRequests",
                column: "EventKey",
                unique: true,
                filter: "[EventKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRequests_ExpansionComplete_LeaseUntilUtc_CreatedAtUtc",
                table: "NotificationRequests",
                columns: new[] { "ExpansionComplete", "LeaseUntilUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRequests_IdempotencyScope",
                table: "NotificationRequests",
                column: "IdempotencyScope",
                unique: true,
                filter: "[IdempotencyScope] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPushDevices_DestinationHash",
                table: "RiderPushDevices",
                column: "DestinationHash",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPushDevices_InstallationId",
                table: "RiderPushDevices",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderPushDevices_RiderId_IsActive",
                table: "RiderPushDevices",
                columns: new[] { "RiderId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderPushDevices_SessionId",
                table: "RiderPushDevices",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationRecipients");

            migrationBuilder.DropTable(
                name: "RiderPushDevices");

            migrationBuilder.DropTable(
                name: "NotificationRequests");
        }
    }
}
