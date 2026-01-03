using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramAuthTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramAuthStates",
                columns: table => new
                {
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TempLogin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramAuthStates", x => x.TelegramUserId);
                });

            migrationBuilder.CreateTable(
                name: "TelegramUserLinks",
                columns: table => new
                {
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    UserLogin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramUserLinks", x => x.TelegramUserId);
                    table.ForeignKey(
                        name: "FK_TelegramUserLinks_Users_UserLogin",
                        column: x => x.UserLogin,
                        principalTable: "Users",
                        principalColumn: "Login",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramAuthStates_UpdatedAtUtc",
                table: "TelegramAuthStates",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramUserLinks_UserLogin",
                table: "TelegramUserLinks",
                column: "UserLogin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramAuthStates");

            migrationBuilder.DropTable(
                name: "TelegramUserLinks");
        }
    }
}
