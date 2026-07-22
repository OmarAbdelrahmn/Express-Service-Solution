using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class LinkSourceEvidenceToPrivateFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StoredFileId",
                table: "SourceEvidences",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidences_StoredFileId",
                table: "SourceEvidences",
                column: "StoredFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_SourceEvidences_AccountingStoredFiles_StoredFileId",
                table: "SourceEvidences",
                column: "StoredFileId",
                principalTable: "AccountingStoredFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SourceEvidences_AccountingStoredFiles_StoredFileId",
                table: "SourceEvidences");

            migrationBuilder.DropIndex(
                name: "IX_SourceEvidences_StoredFileId",
                table: "SourceEvidences");

            migrationBuilder.DropColumn(
                name: "StoredFileId",
                table: "SourceEvidences");
        }
    }
}
