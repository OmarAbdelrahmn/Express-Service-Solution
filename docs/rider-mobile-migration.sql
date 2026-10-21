BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020105936_AddRiderMobileSessions'
)
BEGIN
    ALTER TABLE [RiderDetails] ADD [PreferredLanguage] nvarchar(2) NOT NULL DEFAULT N'ar';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020105936_AddRiderMobileSessions'
)
BEGIN
    CREATE TABLE [RiderMobileSessions] (
        [Id] uniqueidentifier NOT NULL,
        [RiderId] int NOT NULL,
        [Language] nvarchar(2) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_RiderMobileSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RiderMobileSessions_RiderDetails_RiderId] FOREIGN KEY ([RiderId]) REFERENCES [RiderDetails] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020105936_AddRiderMobileSessions'
)
BEGIN
    CREATE INDEX [IX_RiderMobileSessions_RiderId_ExpiresAtUtc] ON [RiderMobileSessions] ([RiderId], [ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020105936_AddRiderMobileSessions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020105936_AddRiderMobileSessions', N'10.0.6');
END;

COMMIT;
GO

