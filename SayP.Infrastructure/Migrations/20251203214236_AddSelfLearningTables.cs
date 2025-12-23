using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SayP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfLearningTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContextReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EntityDataJson = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContextReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContextReferences_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlobalLearningPool",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedPattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Intent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TenantCount = table.Column<int>(type: "integer", nullable: false),
                    TotalUsageCount = table.Column<int>(type: "integer", nullable: false),
                    AverageSuccessRate = table.Column<double>(type: "double precision", nullable: false),
                    GlobalConfidenceScore = table.Column<double>(type: "double precision", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalLearningPool", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnedAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Intent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Alias = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedAlias = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false),
                    SuccessRate = table.Column<double>(type: "double precision", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnedAliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantTerminology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardTerm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomTerm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedCustomTerm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantTerminology", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedbackType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OriginalMessage = table.Column<string>(type: "text", nullable: true),
                    DetectedIntent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CorrectIntent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserComment = table.Column<string>(type: "text", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    IsProcessed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFeedback_UserProfiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContextReferences_ConversationId_ReferenceType",
                table: "ContextReferences",
                columns: new[] { "ConversationId", "ReferenceType" });

            migrationBuilder.CreateIndex(
                name: "IX_ContextReferences_EntityId",
                table: "ContextReferences",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalLearningPool_Intent_Language",
                table: "GlobalLearningPool",
                columns: new[] { "Intent", "Language" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalLearningPool_IsActive_GlobalConfidenceScore",
                table: "GlobalLearningPool",
                columns: new[] { "IsActive", "GlobalConfidenceScore" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalLearningPool_NormalizedPattern",
                table: "GlobalLearningPool",
                column: "NormalizedPattern");

            migrationBuilder.CreateIndex(
                name: "IX_LearnedAliases_Language_IsActive",
                table: "LearnedAliases",
                columns: new[] { "Language", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnedAliases_NormalizedAlias",
                table: "LearnedAliases",
                column: "NormalizedAlias");

            migrationBuilder.CreateIndex(
                name: "IX_LearnedAliases_TenantId_Intent",
                table: "LearnedAliases",
                columns: new[] { "TenantId", "Intent" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantTerminology_TenantId_NormalizedCustomTerm",
                table: "TenantTerminology",
                columns: new[] { "TenantId", "NormalizedCustomTerm" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantTerminology_TenantId_StandardTerm",
                table: "TenantTerminology",
                columns: new[] { "TenantId", "StandardTerm" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedback_DetectedIntent",
                table: "UserFeedback",
                column: "DetectedIntent");

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedback_FeedbackType_IsProcessed",
                table: "UserFeedback",
                columns: new[] { "FeedbackType", "IsProcessed" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedback_UserProfileId",
                table: "UserFeedback",
                column: "UserProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContextReferences");

            migrationBuilder.DropTable(
                name: "GlobalLearningPool");

            migrationBuilder.DropTable(
                name: "LearnedAliases");

            migrationBuilder.DropTable(
                name: "TenantTerminology");

            migrationBuilder.DropTable(
                name: "UserFeedback");
        }
    }
}
