using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddKeetaManagerVacationStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vacation
                SET vacation.Status = 10
                FROM VacationRequests AS vacation
                INNER JOIN RiderDetails AS rider ON rider.Id = vacation.RiderId
                WHERE rider.CompanyId = 2
                  AND vacation.Status = 1
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM VacationApprovalDecisions AS decision
                      WHERE decision.VacationRequestId = vacation.Id
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE VacationRequests
                SET Status = 1
                WHERE Status = 10;
                """);
        }
    }
}
