using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Data
{
    public class AppDbContext : IdentityDbContext<AppUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<JobPosting>     JobPostings     { get; set; }
        public DbSet<UserProfile>    UserProfiles    { get; set; }
        public DbSet<SavedJob>       SavedJobs       { get; set; }
        public DbSet<SavedSearch>    SavedSearches   { get; set; }
        public DbSet<Subscription>   Subscriptions   { get; set; }
        public DbSet<UsageTracking>  UsageTrackings  { get; set; }
        public DbSet<PendingPayment> PendingPayments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // UserProfile → AppUser (1-to-1)
            builder.Entity<UserProfile>()
                .HasOne(p => p.User)
                .WithOne(u => u.Profile)
                .HasForeignKey<UserProfile>(p => p.UserId);

            // SavedJob → AppUser (many-to-1)
            builder.Entity<SavedJob>()
                .HasOne(s => s.User)
                .WithMany(u => u.SavedJobs)
                .HasForeignKey(s => s.UserId);

            // SavedJob → JobPosting (many-to-1)
            builder.Entity<SavedJob>()
                .HasOne(s => s.JobPosting)
                .WithMany()
                .HasForeignKey(s => s.JobPostingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Performans için temel index'ler
            builder.Entity<JobPosting>()
                .HasIndex(j => j.Url)
                .HasDatabaseName("IX_JobPostings_Url");

            builder.Entity<JobPosting>()
                .HasIndex(j => j.DateScraped)
                .HasDatabaseName("IX_JobPostings_DateScraped");

            builder.Entity<JobPosting>()
                .HasIndex(j => j.Source)
                .HasDatabaseName("IX_JobPostings_Source");

            // Scraping istatistikleri için composite index (Dashboard sorguları)
            builder.Entity<JobPosting>()
                .HasIndex(j => new { j.Source, j.DateScraped })
                .HasDatabaseName("IX_JobPostings_Source_DateScraped");

            // Listings filtreleme için composite index
            builder.Entity<JobPosting>()
                .HasIndex(j => new { j.Level, j.DateScraped })
                .HasDatabaseName("IX_JobPostings_Level_DateScraped");

            // CV eşleştirme sorgusu için — ExtractedSkills boş olmayan kayıtlar
            builder.Entity<JobPosting>()
                .HasIndex(j => new { j.DateScraped, j.ExtractedSkills })
                .HasDatabaseName("IX_JobPostings_DateScraped_ExtractedSkills");

            builder.Entity<SavedJob>()
                .HasIndex(s => new { s.UserId, s.JobPostingId })
                .IsUnique()
                .HasDatabaseName("IX_SavedJobs_UserJob");

            // SavedJob listesi sıralaması için
            builder.Entity<SavedJob>()
                .HasIndex(s => new { s.UserId, s.SavedAt })
                .HasDatabaseName("IX_SavedJobs_UserId_SavedAt");

            // SavedSearch → AppUser (many-to-1)
            builder.Entity<SavedSearch>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Subscription → AppUser (many-to-1)
            builder.Entity<Subscription>()
                .HasOne(s => s.User)
                .WithMany(u => u.Subscriptions)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // UsageTracking → AppUser (many-to-1) + unique index
            builder.Entity<UsageTracking>()
                .HasOne(u => u.User)
                .WithMany(u => u.UsageTrackings)
                .HasForeignKey(u => u.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UsageTracking>()
                .HasIndex(u => new { u.UserId, u.Year, u.Month })
                .IsUnique()
                .HasDatabaseName("IX_UsageTracking_UserYearMonth");
        }
    }
}
