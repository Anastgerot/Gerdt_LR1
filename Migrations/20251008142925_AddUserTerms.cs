using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.CreateTable(
                name: "UserTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserLogin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TermId = table.Column<int>(type: "int", nullable: false),
                    LastViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTerms_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTerms_Users_UserLogin",
                        column: x => x.UserLogin,
                        principalTable: "Users",
                        principalColumn: "Login",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTerms_TermId",
                table: "UserTerms",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTerms_UserLogin_TermId",
                table: "UserTerms",
                columns: new[] { "UserLogin", "TermId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTerms");

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
    }
}
