using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bassign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AwaitingDeclineReasonForTaskId",
                table: "WhatsAppBindings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "Tasks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeclineReasonRequired",
                table: "OrganizationSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProofRequiredByDefault",
                table: "OrganizationSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CategoryId",
                table: "Tasks",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCategories_Name",
                table: "TaskCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_TaskCategories_CategoryId",
                table: "Tasks",
                column: "CategoryId",
                principalTable: "TaskCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_TaskCategories_CategoryId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "TaskCategories");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_CategoryId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "AwaitingDeclineReasonForTaskId",
                table: "WhatsAppBindings");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DeclineReasonRequired",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "ProofRequiredByDefault",
                table: "OrganizationSettings");
        }
    }
}
