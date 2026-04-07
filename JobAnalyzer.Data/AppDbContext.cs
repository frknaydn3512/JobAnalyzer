using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<JobPosting> JobPostings { get; set; }
    }
}