BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE TABLE [NotificationRequests] (
        [Id] uniqueidentifier NOT NULL,
        [ActorId] nvarchar(450) NULL,
        [CompanyId] int NULL,
        [TargetRiderId] int NULL,
        [IdempotencyScope] varchar(64) NULL,
        [EventKey] nvarchar(200) NULL,
        [PayloadHash] varchar(64) NOT NULL,
        [ContentJson] nvarchar(max) NOT NULL,
        [DataJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [UpperRiderId] int NOT NULL,
        [LastRiderId] int NOT NULL,
        [ExpansionComplete] bit NOT NULL,
        [LeaseOwner] uniqueidentifier NULL,
        [LeaseUntilUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_NotificationRequests] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE TABLE [RiderPushDevices] (
        [Id] uniqueidentifier NOT NULL,
        [InstallationId] uniqueidentifier NOT NULL,
        [RiderId] int NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [ProtectedDestination] nvarchar(2048) NOT NULL,
        [DestinationHash] varchar(64) NOT NULL,
        [Platform] nvarchar(16) NOT NULL,
        [Permission] nvarchar(16) NOT NULL,
        [AppVersion] nvarchar(64) NULL,
        [IsActive] bit NOT NULL,
        [Generation] bigint NOT NULL,
        [LastSeenAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_RiderPushDevices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RiderPushDevices_RiderMobileSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [RiderMobileSessions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE TABLE [NotificationRecipients] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [RiderId] int NOT NULL,
        [NoDevice] bit NOT NULL,
        CONSTRAINT [PK_NotificationRecipients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NotificationRecipients_NotificationRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [NotificationRequests] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE TABLE [NotificationDeliveries] (
        [Id] uniqueidentifier NOT NULL,
        [RecipientId] uniqueidentifier NOT NULL,
        [DeviceId] uniqueidentifier NOT NULL,
        [DeviceGeneration] bigint NOT NULL,
        [Status] nvarchar(16) NOT NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAtUtc] datetime2 NOT NULL,
        [LeaseOwner] uniqueidentifier NULL,
        [LeaseUntilUtc] datetime2 NULL,
        [ProviderMessageId] nvarchar(512) NULL,
        [LastErrorCode] nvarchar(64) NULL,
        [AcceptedAtUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_NotificationDeliveries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NotificationDeliveries_NotificationRecipients_RecipientId] FOREIGN KEY ([RecipientId]) REFERENCES [NotificationRecipients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NotificationDeliveries_RiderPushDevices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [RiderPushDevices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE INDEX [IX_NotificationDeliveries_DeviceId] ON [NotificationDeliveries] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationDeliveries_RecipientId_DeviceId_DeviceGeneration] ON [NotificationDeliveries] ([RecipientId], [DeviceId], [DeviceGeneration]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE INDEX [IX_NotificationDeliveries_Status_NextAttemptAtUtc_LeaseUntilUtc] ON [NotificationDeliveries] ([Status], [NextAttemptAtUtc], [LeaseUntilUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationRecipients_RequestId_RiderId] ON [NotificationRecipients] ([RequestId], [RiderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_NotificationRequests_EventKey] ON [NotificationRequests] ([EventKey]) WHERE [EventKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE INDEX [IX_NotificationRequests_ExpansionComplete_LeaseUntilUtc_CreatedAtUtc] ON [NotificationRequests] ([ExpansionComplete], [LeaseUntilUtc], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_NotificationRequests_IdempotencyScope] ON [NotificationRequests] ([IdempotencyScope]) WHERE [IdempotencyScope] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RiderPushDevices_DestinationHash] ON [RiderPushDevices] ([DestinationHash]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RiderPushDevices_InstallationId] ON [RiderPushDevices] ([InstallationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE INDEX [IX_RiderPushDevices_RiderId_IsActive] ON [RiderPushDevices] ([RiderId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    CREATE INDEX [IX_RiderPushDevices_SessionId] ON [RiderPushDevices] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022105437_AddRiderNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022105437_AddRiderNotifications', N'10.0.6');
END;

COMMIT;
GO

