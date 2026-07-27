using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddVacationApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VacationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiderId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FullyApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CancelledByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationRequests", x => x.Id);
                    table.CheckConstraint("CK_VacationRequests_DateRange", "[EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_VacationRequests_RiderDetails_RiderId",
                        column: x => x.RiderId,
                        principalTable: "RiderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VacationUserRoleAssignments",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    GrantedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationUserRoleAssignments", x => new { x.UserId, x.Role });
                    table.ForeignKey(
                        name: "FK_VacationUserRoleAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VacationApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VacationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DecidedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationApprovalDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacationApprovalDecisions_VacationRequests_VacationRequestId",
                        column: x => x.VacationRequestId,
                        principalTable: "VacationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VacationCancellationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationCancellationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacationCancellationRequests_VacationRequests_VacationRequestId",
                        column: x => x.VacationRequestId,
                        principalTable: "VacationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VacationDateChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProposedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProposedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationDateChangeRequests", x => x.Id);
                    table.CheckConstraint("CK_VacationDateChangeRequests_DateRange", "[ProposedEndDate] >= [ProposedStartDate]");
                    table.ForeignKey(
                        name: "FK_VacationDateChangeRequests_VacationRequests_VacationRequestId",
                        column: x => x.VacationRequestId,
                        principalTable: "VacationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VacationApprovalDecisions_VacationRequestId_Role",
                table: "VacationApprovalDecisions",
                columns: new[] { "VacationRequestId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VacationCancellationRequests_VacationRequestId",
                table: "VacationCancellationRequests",
                column: "VacationRequestId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_VacationCancellationRequests_VacationRequestId_Status",
                table: "VacationCancellationRequests",
                columns: new[] { "VacationRequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationDateChangeRequests_VacationRequestId",
                table: "VacationDateChangeRequests",
                column: "VacationRequestId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_VacationDateChangeRequests_VacationRequestId_Status",
                table: "VacationDateChangeRequests",
                columns: new[] { "VacationRequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_RiderId_Status_StartDate_EndDate",
                table: "VacationRequests",
                columns: new[] { "RiderId", "Status", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_Status_RequestedAt",
                table: "VacationRequests",
                columns: new[] { "Status", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VacationApprovalDecisions");

            migrationBuilder.DropTable(
                name: "VacationCancellationRequests");

            migrationBuilder.DropTable(
                name: "VacationDateChangeRequests");

            migrationBuilder.DropTable(
                name: "VacationUserRoleAssignments");

            migrationBuilder.DropTable(
                name: "VacationRequests");
        }
    }
}
