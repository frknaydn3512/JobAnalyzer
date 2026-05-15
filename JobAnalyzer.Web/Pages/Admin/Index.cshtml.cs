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
        public string? ActionMessage { get; set; }
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
            await LoadStats();
        }

        private async Task LoadStats()
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

            RecentUsers.Clear();
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

        public async Task<IActionResult> OnPostTriggerScrapingAsync()
        {
            BackgroundJob.Enqueue<ScrapingOrchestrator>(o => o.RunFullCycleAsync());
            JobQueued = true;
            ActionMessage = "Tüm scraper'lar kuyruğa alındı.";
            await LoadStats();
            return Page();
        }

        public async Task<IActionResult> OnPostTriggerSingleScrapingAsync(string scraperName)
        {
            if (!string.IsNullOrEmpty(scraperName))
            {
                BackgroundJob.Enqueue<ScrapingOrchestrator>(o => o.RunSingleScraperAsync(scraperName));
                JobQueued = true;
                ActionMessage = $"{scraperName} kuyruğa alındı.";
            }
            await LoadStats();
            return Page();
        }

        public async Task<IActionResult> OnPostTriggerAnalysisAsync()
        {
            BackgroundJob.Enqueue(() => new JobAnalyzer.Scraper.GroqAnalyzer(null).RunAsync());
            JobQueued = true;
            await LoadStats();
            return Page();
        }

        public async Task<IActionResult> OnPostCleanOldJobsAsync()
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            
            // Performans için ham SQL ile silme işlemi (EF Core'un hepsini belleğe almasını önler)
            int deleted = await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"JobPostings\" WHERE \"DateScraped\" < {0}", cutoff);
            
            ActionMessage = $"{deleted} adet 30 günden eski ilan başarıyla silindi.";
            await LoadStats();
            return Page();
        }

        public async Task<IActionResult> OnPostDistributeDatesAsync()
        {
            // Demo/Sunum için mevcut ilanların tarihlerini son 30 güne rastgele yayar
            // Büyük veri setlerinde timeout olmaması için sadece son 30 günden eski olanları güncelleriz
            var cutoff = DateTime.UtcNow.AddDays(-30);
            
            // PostgreSQL özel rastgele gün güncelleme sorgusu
            // 0-30 gün arasında rastgele bir tarih atar
            string sql = @"
                UPDATE ""JobPostings""
                SET ""DateScraped"" = NOW() - (random() * 30 || ' days')::interval
                WHERE ""DateScraped"" < @p0 OR ""DateScraped"" IS NULL;";
                
            int updated = await _db.Database.ExecuteSqlRawAsync(sql, cutoff);

            ActionMessage = $"Mükemmel! {updated} ilanın tarihi son 30 güne rastgele dağıtıldı. Dashboard grafikleri artık dolu görünecek.";
            await LoadStats();
            return Page();
        }
    }
}
