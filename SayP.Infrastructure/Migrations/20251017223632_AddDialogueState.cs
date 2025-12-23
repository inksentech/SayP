using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SayP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDialogueState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DialogueStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PendingIntent = table.Column<int>(type: "integer", nullable: true),
                    CollectedSlotsJson = table.Column<string>(type: "text", nullable: false),
                    MissingSlotsJson = table.Column<string>(type: "text", nullable: false),
                    LastQuestion = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    TurnCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialogueStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DialogueStates_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DialogueStates_ConversationId_IsActive",
                table: "DialogueStates",
                columns: new[] { "ConversationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DialogueStates_ExpiresAt",
                table: "DialogueStates",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DialogueStates");
        }
    }
}
