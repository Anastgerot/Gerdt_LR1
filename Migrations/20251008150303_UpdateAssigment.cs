using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAssigment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assignments_AssignedToLogin_IsSolved",
                table: "Assignments");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_AssignedToLogin_TermId_Direction",
                table: "Assignments",
                columns: new[] { "AssignedToLogin", "TermId", "Direction" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assignments_AssignedToLogin_TermId_Direction",
                table: "Assignments");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_AssignedToLogin_IsSolved",
                table: "Assignments",
                columns: new[] { "AssignedToLogin", "IsSolved" });
        }
    }
}
