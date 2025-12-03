using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class updatesup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "RiderShiftSubstitutions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // Step 1: Add columns as NULLABLE first (no default value)
            migrationBuilder.AddColumn<int>(
                name: "ActualRiderWorkingId",
                table: "RiderShiftSubstitutions",
                type: "int",
                nullable: true); // ✅ Nullable first

            migrationBuilder.AddColumn<int>(
                name: "SubstituteRiderId",
                table: "RiderShiftSubstitutions",
                type: "int",
                nullable: true); // ✅ Nullable first

            // Step 2: Populate SubstituteRiderId from existing SubstituteWorkingId
            migrationBuilder.Sql(@"
        UPDATE rss
        SET rss.SubstituteRiderId = rd.Id
        FROM RiderShiftSubstitutions rss
        INNER JOIN RiderDetails rd ON rss.SubstituteWorkingId = rd.WorkingId
    ");

            // Step 3: Populate ActualRiderWorkingId from ActualRider
            migrationBuilder.Sql(@"
        UPDATE rss
        SET rss.ActualRiderWorkingId = rd.WorkingId
        FROM RiderShiftSubstitutions rss
        INNER JOIN RiderDetails rd ON rss.ActualRiderId = rd.Id
    ");

            // Step 4: Make columns NOT NULL after data is populated
            migrationBuilder.AlterColumn<int>(
                name: "ActualRiderWorkingId",
                table: "RiderShiftSubstitutions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SubstituteRiderId",
                table: "RiderShiftSubstitutions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // Step 5: Now add the foreign key constraint
            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftSubstitutions_SubstituteRiderId",
                table: "RiderShiftSubstitutions",
                column: "SubstituteRiderId");

            migrationBuilder.AddForeignKey(
                name: "FK_RiderShiftSubstitutions_RiderDetails_SubstituteRiderId",
                table: "RiderShiftSubstitutions",
                column: "SubstituteRiderId",
                principalTable: "RiderDetails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RiderShiftSubstitutions_RiderDetails_SubstituteRiderId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropIndex(
                name: "IX_RiderShiftSubstitutions_SubstituteRiderId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "ActualRiderWorkingId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.DropColumn(
                name: "SubstituteRiderId",
                table: "RiderShiftSubstitutions");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "RiderShiftSubstitutions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }
    }
}
