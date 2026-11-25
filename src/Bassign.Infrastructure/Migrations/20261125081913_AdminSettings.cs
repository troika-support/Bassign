using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bassign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdminSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HoldOutsideWorkingTime",
                table: "OrganizationSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ObservePublicHolidays",
                table: "OrganizationSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkEndHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkStartHour",
                table: "OrganizationSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkingDays",
                table: "OrganizationSettings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSecret = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "OrgClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgClosures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgClosures_Date",
                table: "OrgClosures",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "OrgClosures");

            migrationBuilder.DropColumn(
                name: "HoldOutsideWorkingTime",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "ObservePublicHolidays",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "WorkEndHour",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "WorkStartHour",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "OrganizationSettings");
        }
    }
}
