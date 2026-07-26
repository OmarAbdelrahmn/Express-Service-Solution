using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AllowReuploadAfterRejectedImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_ExternalReference",
                table: "PlatformImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_StoredFileId",
                table: "PlatformImportBatches");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_ExternalReference",
                table: "PlatformImportBatches",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "ExternalReference" },
                unique: true,
                filter: "[Status] <> 6");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_StoredFileId",
                table: "PlatformImportBatches",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "StoredFileId" },
                unique: true,
                filter: "[Status] <> 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_ExternalReference",
                table: "PlatformImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_StoredFileId",
                table: "PlatformImportBatches");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_ExternalReference",
                table: "PlatformImportBatches",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformImportBatches_LegalEntityId_PlatformAccountId_StoredFileId",
                table: "PlatformImportBatches",
                columns: new[] { "LegalEntityId", "PlatformAccountId", "StoredFileId" },
                unique: true);
        }
    }
}
