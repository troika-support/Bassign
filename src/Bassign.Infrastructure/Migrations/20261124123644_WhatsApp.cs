using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bassign.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboundWebhookMessages",
                columns: table => new
                {
                    ProviderMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundWebhookMessages", x => x.ProviderMessageId);
                });

            migrationBuilder.CreateTable(
                name: "OutboundNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PhoneE164 = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Verified = table.Column<bool>(type: "bit", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LinkCodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LinkCodeExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AwaitingProofForTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Direction = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PhoneE164 = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundWebhookMessages_Status_ReceivedAt",
                table: "InboundWebhookMessages",
                columns: new[] { "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundNotifications_Status_NextAttemptAt",
                table: "OutboundNotifications",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppBindings_LinkCodeHash",
                table: "WhatsAppBindings",
                column: "LinkCodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppBindings_PhoneE164",
                table: "WhatsAppBindings",
                column: "PhoneE164",
                unique: true,
                filter: "[PhoneE164] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppBindings_UserId",
                table: "WhatsAppBindings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_PhoneE164_At",
                table: "WhatsAppMessages",
                columns: new[] { "PhoneE164", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ProviderMessageId",
                table: "WhatsAppMessages",
                column: "ProviderMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundWebhookMessages");

            migrationBuilder.DropTable(
                name: "OutboundNotifications");

            migrationBuilder.DropTable(
                name: "WhatsAppBindings");

            migrationBuilder.DropTable(
                name: "WhatsAppMessages");
        }
    }
}
