using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bassign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ReminderLeadMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 120);

            migrationBuilder.AlterColumn<int>(
                name: "QuietStartHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 20);

            migrationBuilder.AlterColumn<int>(
                name: "QuietEndHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 6);

            migrationBuilder.AlterColumn<int>(
                name: "EscalationCooldownMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1440);

            migrationBuilder.AlterColumn<int>(
                name: "EscalateAfterMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 60);

            migrationBuilder.AlterColumn<int>(
                name: "DigestHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 6);

            migrationBuilder.AlterColumn<bool>(
                name: "DigestEnabled",
                table: "OrganizationSettings",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ReminderLeadMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 120,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "QuietStartHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 20,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "QuietEndHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 6,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "EscalationCooldownMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 1440,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "EscalateAfterMinutes",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 60,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "DigestHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: false,
                defaultValue: 6,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<bool>(
                name: "DigestEnabled",
                table: "OrganizationSettings",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");
        }
    }
}
