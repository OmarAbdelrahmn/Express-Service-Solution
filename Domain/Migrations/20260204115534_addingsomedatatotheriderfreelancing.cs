using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domain.Migrations
{
    /// <inheritdoc />
    public partial class addingsomedatatotheriderfreelancing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RiderId",
                table: "KetaFreeLancers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEBWIeZ2Kyg4thoNe/chD36ZnOgIa3aQIwb03Drcz+XmkNFoy1DYWGbMpZBwUGI+d7A==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEIjUSqRDGL2qejTDDaWWTkBfIzncLDiDVW8JtJ9ze0hVfrnMNPazFts3IYZdQNPY+g==");

            migrationBuilder.CreateIndex(
                name: "IX_KetaFreeLancers_RiderId",
                table: "KetaFreeLancers",
                column: "RiderId");

            migrationBuilder.AddForeignKey(
                name: "FK_KetaFreeLancers_RiderDetails_RiderId",
                table: "KetaFreeLancers",
                column: "RiderId",
                principalTable: "RiderDetails",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KetaFreeLancers_RiderDetails_RiderId",
                table: "KetaFreeLancers");

            migrationBuilder.DropIndex(
                name: "IX_KetaFreeLancers_RiderId",
                table: "KetaFreeLancers");

            migrationBuilder.DropColumn(
                name: "RiderId",
                table: "KetaFreeLancers");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59724D2D-E2B5-4C67-AB6F-D93478347B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEMN2JrNegcyIEqpfVBi9bhSMJg4G1t3FcNhxZ0gw7NenL8jkJrD50G51168YnOujwg==");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "59726D2D-E2B5-4C67-AB6F-D93478317B03",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOwNQsRFDo/4aDsGxoU+fWn6cb1UCrp+p/4UgL4hBrWv4gJDM2Gad3TlGhUDQaK77w==");
        }
    }
}
