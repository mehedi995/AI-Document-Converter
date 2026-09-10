using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.Document.Converter.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditRoleChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "OperatorAuditEntries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "TargetEmail",
                table: "OperatorAuditEntries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                table: "OperatorAuditEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatorAuditEntries_TargetUserId",
                table: "OperatorAuditEntries",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OperatorAuditEntries_TargetUserId",
                table: "OperatorAuditEntries");

            migrationBuilder.DropColumn(
                name: "TargetEmail",
                table: "OperatorAuditEntries");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "OperatorAuditEntries");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "OperatorAuditEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
