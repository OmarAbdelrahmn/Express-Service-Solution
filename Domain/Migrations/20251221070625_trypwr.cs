using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class trypwr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VehiclePlateNumber",
                table: "TempVehicleOperations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "ResolvedBy",
                table: "TempVehicleOperations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Resolution",
                table: "TempVehicleOperations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RequestedBy",
                table: "TempVehicleOperations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<DateTime>(
                name: "RequestedAt",
                table: "TempVehicleOperations",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "TempVehicleOperations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<bool>(
                name: "IsResolved",
                table: "TempVehicleOperations",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<string>(
                name: "AdminNotes",
                table: "TempVehicleOperations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Permission",
                table: "TempVehicleOperations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PermissionEndDate",
                table: "TempVehicleOperations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "Timestamp",
                table: "RiderVehicleStatus",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "RiderVehicleStatus",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AddColumn<string>(
                name: "Permission",
                table: "RiderVehicleStatus",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PermissionEndDate",
                table: "RiderVehicleStatus",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PermissionStartDate",
                table: "RiderVehicleStatus",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEL6RlmqJ6zMZrjighNK/ptCQeOiOjWMa8gBtzQ6M/7abFKG9kGept/ThmXUd5EbIQg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEG0IhVr73eKY+3TiJUZ4FxT51sFknfO7E7KqkW02S42Io4A5VVqwt01YhNnU98hIWg==");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_IsResolved_VehicleStatusType",
                table: "TempVehicleOperations",
                columns: new[] { "IsResolved", "VehicleStatusType" },
                filter: "[IsResolved] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TempVehicleOperations_RiderIqamaNo_IsResolved",
                table: "TempVehicleOperations",
                columns: new[] { "RiderIqamaNo", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_EmployeeIqamaNo_IsActive",
                table: "RiderVehicleStatus",
                columns: new[] { "EmployeeIqamaNo", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_Timestamp",
                table: "RiderVehicleStatus",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber_IsActive_PermissionEndDate",
                table: "RiderVehicleStatus",
                columns: new[] { "VehicleNumber", "IsActive", "PermissionEndDate" },
                filter: "[PermissionEndDate] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber_IsActive_StatusType",
                table: "RiderVehicleStatus",
                columns: new[] { "VehicleNumber", "IsActive", "StatusType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TempVehicleOperations_IsResolved_VehicleStatusType",
                table: "TempVehicleOperations");

            migrationBuilder.DropIndex(
                name: "IX_TempVehicleOperations_RiderIqamaNo_IsResolved",
                table: "TempVehicleOperations");

            migrationBuilder.DropIndex(
                name: "IX_RiderVehicleStatus_EmployeeIqamaNo_IsActive",
                table: "RiderVehicleStatus");

            migrationBuilder.DropIndex(
                name: "IX_RiderVehicleStatus_Timestamp",
                table: "RiderVehicleStatus");

            migrationBuilder.DropIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber_IsActive_PermissionEndDate",
                table: "RiderVehicleStatus");

            migrationBuilder.DropIndex(
                name: "IX_RiderVehicleStatus_VehicleNumber_IsActive_StatusType",
                table: "RiderVehicleStatus");

            migrationBuilder.DropColumn(
                name: "Permission",
                table: "TempVehicleOperations");

            migrationBuilder.DropColumn(
                name: "PermissionEndDate",
                table: "TempVehicleOperations");

            migrationBuilder.DropColumn(
                name: "Permission",
                table: "RiderVehicleStatus");

            migrationBuilder.DropColumn(
                name: "PermissionEndDate",
                table: "RiderVehicleStatus");

            migrationBuilder.DropColumn(
                name: "PermissionStartDate",
                table: "RiderVehicleStatus");

            migrationBuilder.AlterColumn<string>(
                name: "VehiclePlateNumber",
                table: "TempVehicleOperations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ResolvedBy",
                table: "TempVehicleOperations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Resolution",
                table: "TempVehicleOperations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RequestedBy",
                table: "TempVehicleOperations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTime>(
                name: "RequestedAt",
                table: "TempVehicleOperations",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETDATE()");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "TempVehicleOperations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsResolved",
                table: "TempVehicleOperations",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "AdminNotes",
                table: "TempVehicleOperations",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "Timestamp",
                table: "RiderVehicleStatus",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETDATE()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "RiderVehicleStatus",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEEoSl1kiHphF/86NoPNSe6IldMwZiml8DsSSnMhMNJH8YX0w8lehdCMa3p6dIie0uA==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOTspXTw+ZQ/QyYR4HaRpDBdk3LVyuYQhYV/kM4gk+b+NtnVFAag2OuufW/91jxLcg==");
        }
    }
}
