using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addthehungerdisability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SponsorNo",
                table: "Employees",
                newName: "sponsorNo");

            migrationBuilder.AlterColumn<long>(
                name: "OldSponsorNo",
                table: "TempEmployeeUpdates",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "NewSponsorNo",
                table: "TempEmployeeUpdates",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "sponsorNo",
                table: "Employees",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "HungerDisabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActualRiderId = table.Column<int>(type: "int", nullable: false),
                    ActualWorkingId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubstituteRiderId = table.Column<int>(type: "int", nullable: true),
                    SubstituteWorkingId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Days = table.Column<int>(type: "int", nullable: false),
                    ShiftDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    AcceptedDailyOrders = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HungerDisabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HungerDisabilities_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HungerDisabilities_RiderDetails_ActualRiderId",
                        column: x => x.ActualRiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEK7z5GS1aoZ46KNnS6CYlZOYMTK0S9TPIxCUHF9ChMFN8wbslUk7j8xWXxm88+4f/w==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEEBDlcEVlmGiRdK5xs7Q0NkyBaPR6IZ7HZ1Gxy07bLy1U4/A96BzJa9/JSGG9L/12A==");

            migrationBuilder.CreateIndex(
                name: "IX_HungerDisability_ActualRider_ShiftDate",
                table: "HungerDisabilities",
                columns: new[] { "ActualRiderId", "ShiftDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HungerDisability_ActualWorkingId",
                table: "HungerDisabilities",
                column: "ActualWorkingId");

            migrationBuilder.CreateIndex(
                name: "IX_HungerDisability_CompanyId",
                table: "HungerDisabilities",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_HungerDisability_ShiftDate",
                table: "HungerDisabilities",
                column: "ShiftDate");

            migrationBuilder.CreateIndex(
                name: "IX_HungerDisability_SubstituteRiderId",
                table: "HungerDisabilities",
                column: "SubstituteRiderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HungerDisabilities");

            migrationBuilder.RenameColumn(
                name: "sponsorNo",
                table: "Employees",
                newName: "SponsorNo");

            migrationBuilder.AlterColumn<int>(
                name: "OldSponsorNo",
                table: "TempEmployeeUpdates",
                type: "int",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "NewSponsorNo",
                table: "TempEmployeeUpdates",
                type: "int",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SponsorNo",
                table: "Employees",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEJmaWg42o3LN4b53Ugf9Lyx/dpHrMCbetjuC+kOmui/c6ctQMU5JB3j9NXzB6J67yw==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEJVMSrwscEutv0axNqQUZiX8dm3+F6bj55iCGDtfRbC6xDq/mnk99wy2WkJy0oHDYg==");
        }
    }
}
