using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SayP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PimlandInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaContextJson",
                table: "DialogueStates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaContextJson",
                table: "DialogueStates");
        }
    }
}
