using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Document.Converter.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetentionAndDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "ConversionJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutputBytesPurgedAtUtc",
                table: "ConversionJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceBytesPurgedAtUtc",
                table: "ConversionJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_CompletedAtUtc_OutputBytesPurgedAtUtc",
                table: "ConversionJobs",
                columns: new[] { "CompletedAtUtc", "OutputBytesPurgedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_CreatedAtUtc_SourceBytesPurgedAtUtc",
                table: "ConversionJobs",
                columns: new[] { "CreatedAtUtc", "SourceBytesPurgedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversionJobs_CompletedAtUtc_OutputBytesPurgedAtUtc",
                table: "ConversionJobs");

            migrationBuilder.DropIndex(
                name: "IX_ConversionJobs_CreatedAtUtc_SourceBytesPurgedAtUtc",
                table: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "OutputBytesPurgedAtUtc",
                table: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "SourceBytesPurgedAtUtc",
                table: "ConversionJobs");
        }
    }
}
