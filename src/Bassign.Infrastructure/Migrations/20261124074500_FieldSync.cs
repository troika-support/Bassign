using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bassign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FieldSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SyncVersion",
                table: "Tasks",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "ProcessedOperations",
                columns: table => new
                {
                    OpId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedOperations", x => x.OpId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedOperations_ProcessedAt",
                table: "ProcessedOperations",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedOperations");

            migrationBuilder.DropColumn(
                name: "SyncVersion",
                table: "Tasks");
        }
    }
}
