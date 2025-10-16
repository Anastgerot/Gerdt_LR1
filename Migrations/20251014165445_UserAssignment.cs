using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class UserAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assignments_Users_AssignedToLogin",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_AssignedToLogin_TermId_Direction",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_TermId",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "AssignedToLogin",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "IsSolved",
                table: "Assignments");

            migrationBuilder.AlterColumn<string>(
                name: "Domain",
                table: "Terms",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "UserAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserLogin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssignmentId = table.Column<int>(type: "int", nullable: false),
                    IsSolved = table.Column<bool>(type: "bit", nullable: false),
                    SolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAssignments_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAssignments_Users_UserLogin",
                        column: x => x.UserLogin,
                        principalTable: "Users",
                        principalColumn: "Login",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_TermId_Direction",
                table: "Assignments",
                columns: new[] { "TermId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAssignments_AssignmentId",
                table: "UserAssignments",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAssignments_UserLogin_AssignmentId",
                table: "UserAssignments",
                columns: new[] { "UserLogin", "AssignmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_TermId_Direction",
                table: "Assignments");

            migrationBuilder.AlterColumn<string>(
                name: "Domain",
                table: "Terms",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToLogin",
                table: "Assignments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsSolved",
                table: "Assignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_AssignedToLogin_TermId_Direction",
                table: "Assignments",
                columns: new[] { "AssignedToLogin", "TermId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_TermId",
                table: "Assignments",
                column: "TermId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assignments_Users_AssignedToLogin",
                table: "Assignments",
                column: "AssignedToLogin",
                principalTable: "Users",
                principalColumn: "Login",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
