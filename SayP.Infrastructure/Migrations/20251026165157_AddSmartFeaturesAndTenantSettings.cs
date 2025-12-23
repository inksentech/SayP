using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SayP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartFeaturesAndTenantSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EnableSmartIntentClassifier = table.Column<bool>(type: "boolean", nullable: false),
                    EnableEntityExtraction = table.Column<bool>(type: "boolean", nullable: false),
                    EnableMultiTurnDialogue = table.Column<bool>(type: "boolean", nullable: false),
                    EnableIntelligentFallback = table.Column<bool>(type: "boolean", nullable: false),
                    EnableUserLearning = table.Column<bool>(type: "boolean", nullable: false),
                    EnableIntentDiscovery = table.Column<bool>(type: "boolean", nullable: false),
                    EnablePatternLearning = table.Column<bool>(type: "boolean", nullable: false),
                    EnableContextAware = table.Column<bool>(type: "boolean", nullable: false),
                    EnableSlotFilling = table.Column<bool>(type: "boolean", nullable: false),
                    EnableAnalytics = table.Column<bool>(type: "boolean", nullable: false),
                    MinConfidenceThreshold = table.Column<double>(type: "double precision", nullable: false),
                    HighConfidenceThreshold = table.Column<double>(type: "double precision", nullable: false),
                    DialogueTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxDialogueAttempts = table.Column<int>(type: "integer", nullable: false),
                    ProfileAnalysisInterval = table.Column<int>(type: "integer", nullable: false),
                    BehaviorLogRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalMessageCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCommandCount = table.Column<int>(type: "integer", nullable: false),
                    FrequentCommandsJson = table.Column<string>(type: "text", nullable: true),
                    PreferredEntitiesJson = table.Column<string>(type: "text", nullable: true),
                    CommunicationStyleJson = table.Column<string>(type: "text", nullable: true),
                    LearnedPatternsJson = table.Column<string>(type: "text", nullable: true),
                    UsageHabitsJson = table.Column<string>(type: "text", nullable: true),
                    CustomContextJson = table.Column<string>(type: "text", nullable: true),
                    PreferredLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    AverageResponseTime = table.Column<double>(type: "double precision", nullable: false),
                    ActiveHoursJson = table.Column<string>(type: "text", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LearningScore = table.Column<int>(type: "integer", nullable: false),
                    SatisfactionScore = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettingsLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettingsLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSettingsLogs_TenantSettings_TenantSettingsId",
                        column: x => x.TenantSettingsId,
                        principalTable: "TenantSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserBehaviorLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    BehaviorType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BehaviorDataJson = table.Column<string>(type: "text", nullable: true),
                    MessageContent = table.Column<string>(type: "text", nullable: true),
                    CommandType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsSuccessful = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    ResponseTimeMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBehaviorLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBehaviorLogs_UserProfiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettingsLogs_TenantSettingsId",
                table: "TenantSettingsLogs",
                column: "TenantSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBehaviorLogs_UserProfileId",
                table: "UserBehaviorLogs",
                column: "UserProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSettingsLogs");

            migrationBuilder.DropTable(
                name: "UserBehaviorLogs");

            migrationBuilder.DropTable(
                name: "TenantSettings");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}
