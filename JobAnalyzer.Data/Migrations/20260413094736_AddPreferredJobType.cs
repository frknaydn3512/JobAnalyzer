using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredJobType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredJobType",
                table: "UserProfiles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredJobType",
                table: "UserProfiles");
        }
    }
}
