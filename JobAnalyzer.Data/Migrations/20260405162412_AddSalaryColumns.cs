using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "JobPostings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxSalary",
                table: "JobPostings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinSalary",
                table: "JobPostings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "MaxSalary",
                table: "JobPostings");

            migrationBuilder.DropColumn(
                name: "MinSalary",
                table: "JobPostings");
        }
    }
}
