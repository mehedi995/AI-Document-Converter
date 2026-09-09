using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Document.Converter.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillingAndMetering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentPeriodStartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentPeriodEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccessEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderSubscriptionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsageLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsagePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Credits = table.Column<long>(type: "bigint", nullable: false),
                    BasisDescription = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsagePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IncludedCredits = table.Column<long>(type: "bigint", nullable: false),
                    ReservedCredits = table.Column<long>(type: "bigint", nullable: false),
                    SettledCredits = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsagePeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsagePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstimatedCredits = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageReservations_UsagePeriods_UsagePeriodId",
                        column: x => x.UsagePeriodId,
                        principalTable: "UsagePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_WorkspaceId",
                table: "Subscriptions",
                column: "WorkspaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageLedgerEntries_IdempotencyKey",
                table: "UsageLedgerEntries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageLedgerEntries_WorkspaceId_UsagePeriodId_CreatedAtUtc",
                table: "UsageLedgerEntries",
                columns: new[] { "WorkspaceId", "UsagePeriodId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsagePeriods_WorkspaceId_StartsAtUtc_EndsAtUtc",
                table: "UsagePeriods",
                columns: new[] { "WorkspaceId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageReservations_JobId",
                table: "UsageReservations",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageReservations_UsagePeriodId",
                table: "UsageReservations",
                column: "UsagePeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "UsageLedgerEntries");

            migrationBuilder.DropTable(
                name: "UsageReservations");

            migrationBuilder.DropTable(
                name: "UsagePeriods");
        }
    }
}
