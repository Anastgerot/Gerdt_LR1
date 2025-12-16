using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class AddViewedAnswerFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ViewedAnswer",
                table: "UserAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ViewedAnswer",
                table: "UserAssignments");
        }
    }
}
