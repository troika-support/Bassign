using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bassign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Scheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupeKey",
                table: "OutboundNotifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                table: "OutboundNotifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DigestEnabled",
                table: "OrganizationSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DigestHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<int>(
                name: "EscalateAfterMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "EscalationCooldownMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 1440);

            migrationBuilder.AddColumn<int>(
                name: "QuietEndHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<int>(
                name: "QuietStartHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "ReminderLeadMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_DedupeKey",
                table: "OutboundNotifications",
                column: "DedupeKey",
                unique: true,
                filter: "[DedupeKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_Kind_TaskId_CreatedAt",
                table: "OutboundNotifications",
                columns: new[] { "Kind", "TaskId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboundNotifications_DedupeKey",
                table: "OutboundNotifications");

            migrationBuilder.DropIndex(
                name: "IX_OutboundNotifications_Kind_TaskId_CreatedAt",
                table: "OutboundNotifications");

            migrationBuilder.DropColumn(
                name: "DedupeKey",
                table: "OutboundNotifications");

            migrationBuilder.DropColumn(
                name: "PayloadJson",
                table: "OutboundNotifications");

            migrationBuilder.DropColumn(
                name: "DigestEnabled",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "DigestHour",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "EscalateAfterMinutes",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "EscalationCooldownMinutes",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "QuietEndHour",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "QuietStartHour",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "ReminderLeadMinutes",
                table: "OrganizationSettings");
        }
    }
}
