using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Document.Converter.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneOpenReservationPerJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsageReservations_JobId",
                table: "UsageReservations");

            migrationBuilder.CreateIndex(
                name: "IX_UsageReservations_JobId",
                table: "UsageReservations",
                column: "JobId",
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsageReservations_JobId",
                table: "UsageReservations");

            migrationBuilder.CreateIndex(
                name: "IX_UsageReservations_JobId",
                table: "UsageReservations",
                column: "JobId",
                unique: true);
        }
    }
}
