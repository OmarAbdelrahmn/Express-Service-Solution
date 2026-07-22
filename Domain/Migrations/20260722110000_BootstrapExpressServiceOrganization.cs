using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations;

[DbContext(typeof(ApplicationDbcontext))]
[Migration("20260722110000_BootstrapExpressServiceOrganization")]
public partial class BootstrapExpressServiceOrganization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @TenantId int;
            DECLARE @LegalEntityId int;

            SELECT @TenantId = [Id]
            FROM [dbo].[Tenants]
            WHERE [Code] = N'EXPRESSSERVICE';

            IF @TenantId IS NULL
            BEGIN
                INSERT INTO [dbo].[Tenants] ([Code], [Name], [IsActive], [CreatedAt])
                VALUES (N'EXPRESSSERVICE', N'ExpressService', 1, SYSUTCDATETIME());

                SET @TenantId = CONVERT(int, SCOPE_IDENTITY());
            END;

            SELECT @LegalEntityId = [Id]
            FROM [dbo].[LegalEntities]
            WHERE [TenantId] = @TenantId
              AND [Code] = N'EXPRESSSERVICE';

            IF @LegalEntityId IS NULL
            BEGIN
                INSERT INTO [dbo].[LegalEntities]
                (
                    [TenantId],
                    [Code],
                    [LegalName],
                    [BaseCurrencyCode],
                    [TaxRegistrationNumber],
                    [IsActive],
                    [CreatedAt]
                )
                VALUES
                (
                    @TenantId,
                    N'EXPRESSSERVICE',
                    N'ExpressService',
                    N'SAR',
                    NULL,
                    1,
                    SYSUTCDATETIME()
                );

                SET @LegalEntityId = CONVERT(int, SCOPE_IDENTITY());
            END;

            IF NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[Branches]
                WHERE [LegalEntityId] = @LegalEntityId
                  AND [Code] = N'HQ'
            )
            BEGIN
                INSERT INTO [dbo].[Branches]
                (
                    [LegalEntityId],
                    [Code],
                    [Name],
                    [IsActive],
                    [CreatedAt]
                )
                VALUES
                (
                    @LegalEntityId,
                    N'HQ',
                    N'ExpressService Headquarters',
                    1,
                    SYSUTCDATETIME()
                );
            END;

            INSERT INTO [dbo].[PlatformAccounts]
            (
                [LegalEntityId],
                [Code],
                [PlatformName],
                [ExternalAccountReference],
                [IsActive],
                [CreatedAt]
            )
            SELECT
                @LegalEntityId,
                N'LEGACY-' + CONVERT(nvarchar(11), [Company].[Id]),
                LEFT([Company].[Name], 100),
                N'legacy-company:' + CONVERT(nvarchar(11), [Company].[Id]),
                1,
                SYSUTCDATETIME()
            FROM [dbo].[Companies] AS [Company]
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[PlatformAccounts] AS [PlatformAccount]
                WHERE [PlatformAccount].[LegalEntityId] = @LegalEntityId
                  AND [PlatformAccount].[ExternalAccountReference] = N'legacy-company:' + CONVERT(nvarchar(11), [Company].[Id])
            );

            INSERT INTO [dbo].[LegacyCompanyPlatformMappings]
            (
                [CompanyId],
                [PlatformAccountId],
                [EffectiveFrom],
                [EffectiveTo],
                [CreatedAt]
            )
            SELECT
                [Company].[Id],
                [PlatformAccount].[Id],
                SYSUTCDATETIME(),
                NULL,
                SYSUTCDATETIME()
            FROM [dbo].[Companies] AS [Company]
            INNER JOIN [dbo].[PlatformAccounts] AS [PlatformAccount]
                ON [PlatformAccount].[LegalEntityId] = @LegalEntityId
               AND [PlatformAccount].[ExternalAccountReference] = N'legacy-company:' + CONVERT(nvarchar(11), [Company].[Id])
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [dbo].[LegacyCompanyPlatformMappings] AS [Mapping]
                WHERE [Mapping].[CompanyId] = [Company].[Id]
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            THROW 51003, 'BootstrapExpressServiceOrganization is intentionally non-reversible because it establishes the production accounting context.', 1;
            """);
    }
}
