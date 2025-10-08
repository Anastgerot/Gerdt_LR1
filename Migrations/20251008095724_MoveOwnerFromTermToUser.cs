using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class MoveOwnerFromTermToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Terms_Users_OwnerLogin",
                table: "Terms");

            migrationBuilder.DropIndex(
                name: "IX_Terms_OwnerLogin",
                table: "Terms");

            migrationBuilder.DropColumn(
                name: "OwnerLogin",
                table: "Terms");

            migrationBuilder.AddColumn<int>(
                name: "TermId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TermId",
                table: "Users",
                column: "TermId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Terms_TermId",
                table: "Users",
                column: "TermId",
                principalTable: "Terms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Terms_TermId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TermId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TermId",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "OwnerLogin",
                table: "Terms",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terms_OwnerLogin",
                table: "Terms",
                column: "OwnerLogin");

            migrationBuilder.AddForeignKey(
                name: "FK_Terms_Users_OwnerLogin",
                table: "Terms",
                column: "OwnerLogin",
                principalTable: "Users",
                principalColumn: "Login",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
