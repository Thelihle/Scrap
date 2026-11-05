using Microsoft.EntityFrameworkCore.Migrations;

public partial class AddLoginAttemptsTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LoginAttempts",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Identifier = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                AttemptTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsSuccessful = table.Column<bool>(type: "bit", nullable: false),
                IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoginAttempts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LoginAttempts_Identifier_AttemptTime",
            table: "LoginAttempts",
            columns: new[] { "Identifier", "AttemptTime" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "LoginAttempts");
    }
}