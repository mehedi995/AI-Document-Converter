using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Document.Converter.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerItemCreditEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EstimatedCredits",
                table: "ConversionJobItems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedCredits",
                table: "ConversionJobItems");
        }
    }
}
