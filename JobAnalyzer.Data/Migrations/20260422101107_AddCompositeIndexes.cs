using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SavedJobs_UserId_SavedAt",
                table: "SavedJobs",
                columns: new[] { "UserId", "SavedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_DateScraped_ExtractedSkills",
                table: "JobPostings",
                columns: new[] { "DateScraped", "ExtractedSkills" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_Level_DateScraped",
                table: "JobPostings",
                columns: new[] { "Level", "DateScraped" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_Source_DateScraped",
                table: "JobPostings",
                columns: new[] { "Source", "DateScraped" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedJobs_UserId_SavedAt",
                table: "SavedJobs");

            migrationBuilder.DropIndex(
                name: "IX_JobPostings_DateScraped_ExtractedSkills",
                table: "JobPostings");

            migrationBuilder.DropIndex(
                name: "IX_JobPostings_Level_DateScraped",
                table: "JobPostings");

            migrationBuilder.DropIndex(
                name: "IX_JobPostings_Source_DateScraped",
                table: "JobPostings");
        }
    }
}
