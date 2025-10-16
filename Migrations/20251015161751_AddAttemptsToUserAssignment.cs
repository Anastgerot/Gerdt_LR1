using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptsToUserAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "UserAssignments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAnsweredAt",
                table: "UserAssignments",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "UserAssignments");

            migrationBuilder.DropColumn(
                name: "LastAnsweredAt",
                table: "UserAssignments");
        }
    }
}
