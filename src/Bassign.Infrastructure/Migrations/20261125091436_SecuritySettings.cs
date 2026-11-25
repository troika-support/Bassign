using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bassign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecuritySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedInviteDomains",
                table: "OrganizationSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InviteLifetimeDays",
                table: "OrganizationSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyContactEmail",
                table: "OrganizationSettings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyContactName",
                table: "OrganizationSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyContactPhone",
                table: "OrganizationSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedInviteDomains",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "InviteLifetimeDays",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "PrivacyContactEmail",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "PrivacyContactName",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "PrivacyContactPhone",
                table: "OrganizationSettings");
        }
    }
}
