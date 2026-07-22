using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingScopedCashDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = 'ACCOUNTANT')
                    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
                    VALUES ('52A7FC8D-27EF-4455-9190-ACCOUNTANT001', 'Accountant', 'ACCOUNTANT', CONVERT(varchar(36), NEWID()));
                """);

            migrationBuilder.CreateTable(
                name: "HousingCashUserAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    HousingId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    GrantedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HousingCashUserAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HousingCashUserAccesses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HousingCashUserAccesses_Housings_HousingId",
                        column: x => x.HousingId,
                        principalTable: "Housings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HousingCashUserAccesses_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HousingCashUserAccesses_HousingId",
                table: "HousingCashUserAccesses",
                column: "HousingId");

            migrationBuilder.CreateIndex(
                name: "IX_HousingCashUserAccesses_LegalEntityId",
                table: "HousingCashUserAccesses",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_HousingCashUserAccesses_UserId_LegalEntityId_HousingId",
                table: "HousingCashUserAccesses",
                columns: new[] { "UserId", "LegalEntityId", "HousingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HousingCashUserAccesses");
        }
    }
}
