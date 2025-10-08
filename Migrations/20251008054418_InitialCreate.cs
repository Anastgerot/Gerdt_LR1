using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gerdt_LR1.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Login = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Login);
                });

            migrationBuilder.CreateTable(
                name: "Terms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    En = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Ru = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OwnerLogin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Terms_Users_OwnerLogin",
                        column: x => x.OwnerLogin,
                        principalTable: "Users",
                        principalColumn: "Login",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TermId = table.Column<int>(type: "int", nullable: false),
                    AssignedToLogin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    IsSolved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assignments_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assignments_Users_AssignedToLogin",
                        column: x => x.AssignedToLogin,
                        principalTable: "Users",
                        principalColumn: "Login",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_AssignedToLogin_IsSolved",
                table: "Assignments",
                columns: new[] { "AssignedToLogin", "IsSolved" });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_TermId",
                table: "Assignments",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_En",
                table: "Terms",
                column: "En");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_En_Ru",
                table: "Terms",
                columns: new[] { "En", "Ru" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terms_OwnerLogin",
                table: "Terms",
                column: "OwnerLogin");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_Ru",
                table: "Terms",
                column: "Ru");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "Terms");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
