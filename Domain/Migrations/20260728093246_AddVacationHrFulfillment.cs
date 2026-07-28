using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddVacationHrFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HrStatus",
                table: "VacationRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE [VacationRequests]
                SET [HrStatus] =
                    CASE
                        WHEN [FullyApprovedAt] IS NOT NULL AND [Status] IN (4, 5) THEN 1
                        WHEN [Status] IN (6, 7, 8, 9) THEN 4
                        ELSE 0
                    END
                """);

            migrationBuilder.CreateTable(
                name: "VacationHrDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredRelativePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSuperseded = table.Column<bool>(type: "bit", nullable: false),
                    SupersededAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SupersededReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationHrDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacationHrDocuments_VacationRequests_VacationRequestId",
                        column: x => x.VacationRequestId,
                        principalTable: "VacationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VacationHrDocuments_VacationRequestId_Type_IsSuperseded",
                table: "VacationHrDocuments",
                columns: new[] { "VacationRequestId", "Type", "IsSuperseded" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationHrDocuments_VacationRequestId_Type_Version",
                table: "VacationHrDocuments",
                columns: new[] { "VacationRequestId", "Type", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VacationHrDocuments");

            migrationBuilder.DropColumn(
                name: "HrStatus",
                table: "VacationRequests");
        }
    }
}
