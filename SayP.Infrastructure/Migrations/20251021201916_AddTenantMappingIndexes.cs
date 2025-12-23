using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SayP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMappingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add composite index for phone number and tenant lookups
            migrationBuilder.CreateIndex(
                name: "IX_TenantMappings_PhoneNumber_TenantId",
                table: "TenantMappings",
                columns: new[] { "PhoneNumber", "TenantId" });

            // Add index for active mappings
            migrationBuilder.CreateIndex(
                name: "IX_TenantMappings_IsActive",
                table: "TenantMappings",
                column: "IsActive");

            // Add composite index for conversations by phone and tenant
            migrationBuilder.CreateIndex(
                name: "IX_Conversations_PhoneNumber_TenantId_Status",
                table: "Conversations",
                columns: new[] { "PhoneNumber", "TenantId", "Status" });

            // Add index for conversation window expiration
            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WindowExpiresAt",
                table: "Conversations",
                column: "WindowExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantMappings_PhoneNumber_TenantId",
                table: "TenantMappings");

            migrationBuilder.DropIndex(
                name: "IX_TenantMappings_IsActive",
                table: "TenantMappings");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_PhoneNumber_TenantId_Status",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_WindowExpiresAt",
                table: "Conversations");
        }
    }
}
