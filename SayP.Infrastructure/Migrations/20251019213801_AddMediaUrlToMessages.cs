using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SayP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaUrlToMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns first
            migrationBuilder.AddColumn<string>(
                name: "MediaUrl",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "Messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Fix Status column type with USING clause
            migrationBuilder.Sql(@"
                ALTER TABLE ""Messages"" 
                ALTER COLUMN ""Status"" TYPE integer 
                USING CASE 
                    WHEN ""Status"" IS NULL THEN 0
                    WHEN ""Status"" ~ '^[0-9]+$' THEN ""Status""::integer
                    ELSE 0
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaUrl",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "Messages");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Messages",
                type: "text",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
