using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Document.Converter.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderEventLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEvents_ProviderName_EventId",
                table: "ProviderEvents",
                columns: new[] { "ProviderName", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderEvents");
        }
    }
}
