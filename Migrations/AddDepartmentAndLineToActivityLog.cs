using Microsoft.EntityFrameworkCore.Migrations;

public partial class AddDepartmentAndLineToActivityLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add DepartmentName column
        migrationBuilder.AddColumn<string>(
            name: "DepartmentName",
            table: "ActivityLogs",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        // Add LineName column
        migrationBuilder.AddColumn<string>(
            name: "LineName",
            table: "ActivityLogs",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DepartmentName",
            table: "ActivityLogs");

        migrationBuilder.DropColumn(
            name: "LineName",
            table: "ActivityLogs");
    }
}