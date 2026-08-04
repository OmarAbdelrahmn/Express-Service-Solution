using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddKeetaBreakShiftPatterns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KeetaBreakShiftPatterns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatternKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShiftKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaBreakShiftPatterns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeetaBreakShiftPatterns_KeetaBreakConfigurations_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalTable: "KeetaBreakConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeetaBreakShiftPatterns_ConfigurationId_PatternKey",
                table: "KeetaBreakShiftPatterns",
                columns: new[] { "ConfigurationId", "PatternKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeetaBreakShiftPatterns");
        }
    }
}
