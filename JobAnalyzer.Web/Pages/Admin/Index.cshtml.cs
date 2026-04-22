using Hangfire;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Web.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class AdminIndexModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;

        public AdminIndexModel(AppDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public int TotalJobs { get; set; }
        public int NewJobsToday { get; set; }
        public int PendingAnalysis { get; set; }
        public int TotalUsers { get; set; }
        public bool JobQueued { get; set; }
        public List<SourceStat> SourceStats { get; set; } = new();
        public List<UserRow> RecentUsers { get; set; } = new();

        public class SourceStat
        {
            public string Source { get; set; } = "";
            public int Total { get; set; }
            public int Analyzed { get; set; }
        }

        public class UserRow
        {
            public string Email { get; set; } = "";
            public string Role { get; set; } = "";
            public DateTime CreatedAt { get; set; }
        }

        public async Task OnGetAsync()
        {
            var today = DateTime.UtcNow.Date;

            TotalJobs = await _db.JobPostings.CountAsync();
            NewJobsToday = await _db.JobPostings.CountAsync(j => j.DateScraped >= today);
            PendingAnalysis = await _db.JobPostings.CountAsync(j =>
                !string.IsNullOrEmpty(j.Description) &&
                (j.ExtractedSkills == null || j.ExtractedSkills == ""));
            TotalUsers = await _userManager.Users.CountAsync();

            SourceStats = await _db.JobPostings
                .GroupBy(j => j.Source ?? "Bilinmiyor")
                .Select(g => new SourceStat
                {
                    Source = g.Key,
                    Total = g.Count(),
                    Analyzed = g.Count(j => j.ExtractedSkills != null && j.ExtractedSkills != "")
                })
                .OrderByDescending(s => s.Total)
                .ToListAsync();

            var users = await _userManager.Users
                .OrderByDescending(u => u.CreatedAt)
                .Take(10)
                .ToListAsync();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                RecentUsers.Add(new UserRow
                {
                    Email = u.Email ?? "",
                    Role = roles.FirstOrDefault() ?? "Free",
                    CreatedAt = u.CreatedAt
                });
            }
        }

        public IActionResult OnPostTriggerScraping()
        {
            BackgroundJob.Enqueue<ScrapingOrchestrator>(o => o.RunFullCycleAsync());
            JobQueued = true;

            // Reload stats
            var today = DateTime.UtcNow.Date;
            TotalJobs = _db.JobPostings.Count();
            NewJobsToday = _db.JobPostings.Count(j => j.DateScraped >= today);
            PendingAnalysis = _db.JobPostings.Count(j =>
                !string.IsNullOrEmpty(j.Description) &&
                (j.ExtractedSkills == null || j.ExtractedSkills == ""));
            TotalUsers = _userManager.Users.Count();

            SourceStats = _db.JobPostings
                .GroupBy(j => j.Source ?? "Bilinmiyor")
                .Select(g => new SourceStat
                {
                    Source = g.Key,
                    Total = g.Count(),
                    Analyzed = g.Count(j => j.ExtractedSkills != null && j.ExtractedSkills != "")
                })
                .OrderByDescending(s => s.Total)
                .ToList();

            return Page();
        }

        public IActionResult OnPostTriggerAnalysis()
        {
            BackgroundJob.Enqueue(() => new JobAnalyzer.Scraper.GroqAnalyzer().RunAsync());
            JobQueued = true;

            TotalJobs = _db.JobPostings.Count();
            PendingAnalysis = _db.JobPostings.Count(j =>
                !string.IsNullOrEmpty(j.Description) &&
                (j.ExtractedSkills == null || j.ExtractedSkills == ""));
            TotalUsers = _userManager.Users.Count();
            SourceStats = _db.JobPostings
                .GroupBy(j => j.Source ?? "Bilinmiyor")
                .Select(g => new SourceStat { Source = g.Key, Total = g.Count(), Analyzed = g.Count(j => j.ExtractedSkills != null && j.ExtractedSkills != "") })
                .OrderByDescending(s => s.Total).ToList();

            return Page();
        }
    }
}
