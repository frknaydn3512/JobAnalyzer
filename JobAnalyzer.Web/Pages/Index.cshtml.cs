using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using JobAnalyzer.Data;

namespace JobAnalyzer.Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        public IndexModel(AppDbContext context) => _context = context;

        public int TotalJobs { get; set; }
        public int NewThisWeek { get; set; }
        public int UniqueCompanies { get; set; }
        public int AnalyzedJobs { get; set; }

        public List<TechStat> TopTechs { get; set; } = new();
        public List<TechStat> NicheSkills { get; set; } = new();
        public List<int> RadarData { get; set; } = new();
        public List<int> WorkModelData { get; set; } = new();
        public List<TrendPoint> DailyTrend { get; set; } = new();
        public List<TechStat> TopCompanies { get; set; } = new();

        public async Task OnGetAsync()
        {
            var weekAgo = DateTime.UtcNow.AddDays(-7);
            var monthAgo = DateTime.UtcNow.AddDays(-30);

            TotalJobs = await _context.JobPostings.CountAsync();
            NewThisWeek = await _context.JobPostings.CountAsync(j => j.DateScraped >= weekAgo);
            UniqueCompanies = await _context.JobPostings
                .Where(j => j.CompanyName != null && j.CompanyName != "Bilinmiyor" && j.CompanyName != "Freelance Müşteri")
                .Select(j => j.CompanyName)
                .Distinct()
                .CountAsync();
            AnalyzedJobs = await _context.JobPostings
                .CountAsync(j => j.ExtractedSkills != null && j.ExtractedSkills != "");

            // Son 30 gün günlük ilan trendi
            var trendRaw = await _context.JobPostings
                .Where(j => j.DateScraped >= monthAgo)
                .GroupBy(j => j.DateScraped.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(g => g.Date)
                .ToListAsync();

            DailyTrend = trendRaw.Select(t => new TrendPoint
            {
                Label = t.Date.ToString("dd MMM"),
                Count = t.Count
            }).ToList();

            // Skill analizi — salt-okunur, change tracking yükü olmadan
            var allSkillsRaw = await _context.JobPostings
                .Where(j => !string.IsNullOrEmpty(j.ExtractedSkills))
                .AsNoTracking()
                .Select(j => j.ExtractedSkills)
                .ToListAsync();

            var allSkills = allSkillsRaw
                .Where(s => s != null)
                .SelectMany(s => s!.Split(','))
                .Select(s => s.Trim().ToLower())
                .Where(s => s.Length > 1)
                .ToList();

            TopTechs = allSkills
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new TechStat { Name = Capitalize(g.Key), Count = g.Count() })
                .ToList();

            var top10Names = TopTechs.Select(t => t.Name?.ToLower()).ToHashSet();
            NicheSkills = allSkills
                .GroupBy(s => s)
                .Where(g => !top10Names.Contains(g.Key) && g.Key.Length > 1)
                .OrderByDescending(g => g.Count())
                .Take(8)
                .Select(g => new TechStat { Name = Capitalize(g.Key), Count = g.Count() })
                .ToList();

            // Radar kategorileri
            string[] frontendKw = { "react", "angular", "vue", "javascript", "html", "css", "typescript", "tailwind", "bootstrap" };
            string[] backendKw = { "c#", ".net", "java", "python", "node.js", "php", "ruby", "golang", "spring", "django" };
            string[] databaseKw = { "sql", "mysql", "postgresql", "mongodb", "redis", "oracle", "nosql" };
            string[] devopsKw = { "docker", "kubernetes", "aws", "azure", "gcp", "ci/cd", "jenkins", "linux" };
            string[] mobileKw = { "flutter", "react native", "swift", "kotlin", "android", "ios" };

            RadarData = new List<int>
            {
                allSkills.Count(s => frontendKw.Any(k => s.Contains(k))),
                allSkills.Count(s => backendKw.Any(k => s.Contains(k))),
                allSkills.Count(s => databaseKw.Any(k => s.Contains(k))),
                allSkills.Count(s => devopsKw.Any(k => s.Contains(k))),
                allSkills.Count(s => mobileKw.Any(k => s.Contains(k)))
            };

            // Çalışma modeli — tüm kayıtları belleğe almak yerine veritabanında COUNT
            int remote = await _context.JobPostings.CountAsync(j =>
                EF.Functions.ILike(j.Location ?? "", "%remote%") ||
                EF.Functions.ILike(j.Location ?? "", "%uzaktan%"));
            int hybrid = await _context.JobPostings.CountAsync(j =>
                EF.Functions.ILike(j.Location ?? "", "%hybrid%") ||
                EF.Functions.ILike(j.Location ?? "", "%hibrit%"));
            int office = Math.Max(0, TotalJobs - remote - hybrid);
            WorkModelData = new List<int> { remote, hybrid, office };

            // En çok ilan açan şirketler
            TopCompanies = await _context.JobPostings
                .Where(j => j.CompanyName != null && j.CompanyName != "Bilinmiyor" && j.CompanyName != "Freelance Müşteri")
                .GroupBy(j => j.CompanyName!)
                .Select(g => new TechStat { Name = g.Key, Count = g.Count() })
                .OrderByDescending(t => t.Count)
                .Take(8)
                .ToListAsync();
        }

        private static string Capitalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.ToLower() switch
            {
                "c#" => "C#",
                ".net" => ".NET",
                "php" => "PHP",
                "sql" => "SQL",
                "css" => "CSS",
                "html" => "HTML",
                "aws" => "AWS",
                "gcp" => "GCP",
                "ci/cd" => "CI/CD",
                _ => char.ToUpper(input[0]) + input[1..]
            };
        }
    }

    public class TechStat
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    public class TrendPoint
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }
}
