using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using JobAnalyzer.Data;
using System.Text.Json;
using System.Linq;

namespace JobAnalyzer.Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        public IndexModel(AppDbContext context) => _context = context;

        public int TotalJobs { get; set; }
        public List<TechStat> TopTechs { get; set; } = new List<TechStat>();
        
        // YENİ EKLENEN VERİ MODELLERİ
        public List<int> RadarData { get; set; } = new List<int>(); // Frontend, Backend, Database, DevOps, Web/Mobile
        public List<TechStat> NicheSkills { get; set; } = new List<TechStat>();
        public List<int> WorkModelData { get; set; } = new List<int>(); // Remote, Hibrit, Ofis

        public async Task OnGetAsync()
        {
            TotalJobs = await _context.JobPostings.CountAsync();

            var allSkillsRaw = await _context.JobPostings
                .Where(j => !string.IsNullOrEmpty(j.ExtractedSkills))
                .Select(j => j.ExtractedSkills)
                .ToListAsync();

            var allSkills = allSkillsRaw
                .Where(s => s != null)
                .SelectMany(s => s!.Split(','))
                .Select(s => s.Trim().ToLower())
                .ToList();

            // Klasik Top 10 Teknolojiler
            TopTechs = allSkills
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new TechStat { Name = Capitalize(g.Key), Count = g.Count() })
                .ToList();

            // Radar Grafiği için Kategorizasyon Dağılımı
            string[] frontendKeywords = { "react", "angular", "vue", "javascript", "html", "css", "typescript", "tailwind", "bootstrap" };
            string[] backendKeywords = { "c#", ".net", "java", "python", "node.js", "php", "ruby", "golang", "spring", "django" };
            string[] databaseKeywords = { "sql", "mysql", "postgresql", "mongodb", "redis", "oracle", "nosql" };
            string[] devopsKeywords = { "docker", "kubernetes", "aws", "azure", "gcp", "ci/cd", "jenkins", "linux" };
            string[] mobileKeywords = { "flutter", "react native", "swift", "kotlin", "android", "ios" };

            int frontendCount = allSkills.Count(s => frontendKeywords.Any(k => s.Contains(k)));
            int backendCount = allSkills.Count(s => backendKeywords.Any(k => s.Contains(k)));
            int databaseCount = allSkills.Count(s => databaseKeywords.Any(k => s.Contains(k)));
            int devopsCount = allSkills.Count(s => devopsKeywords.Any(k => s.Contains(k)));
            int mobileCount = allSkills.Count(s => mobileKeywords.Any(k => s.Contains(k)));

            RadarData = new List<int> { frontendCount, backendCount, databaseCount, devopsCount, mobileCount };

            // Yükselişteki Niş Teknolojiler (Klasik Top 10 dışındaki ama çok arananlar)
            var top10Names = TopTechs.Select(t => t.Name?.ToLower()).ToList();
            NicheSkills = allSkills
                .GroupBy(s => s)
                .Where(g => !top10Names.Contains(g.Key) && g.Key.Length > 1)
                .OrderByDescending(g => g.Count())
                .Take(7)
                .Select(g => new TechStat { Name = Capitalize(g.Key), Count = g.Count() })
                .ToList();

            // Çalışma Modeli Dağılımı (Yarı Statik/Yarı Dinamik)
            var locs = await _context.JobPostings
                .Select(j => (j.Location ?? "") + " " + (j.Title ?? ""))
                .ToListAsync();

            int remote = locs.Count(l => l.Contains("Remote", StringComparison.OrdinalIgnoreCase) || l.Contains("Uzaktan", StringComparison.OrdinalIgnoreCase));
            int hybrid = locs.Count(l => l.Contains("Hybrid", StringComparison.OrdinalIgnoreCase) || l.Contains("Hibrit", StringComparison.OrdinalIgnoreCase));
            int office = TotalJobs - (remote + hybrid);
            if (office < 0) office = 0; // Güvenlik kontrolü

            WorkModelData = new List<int> { remote, hybrid, office };
        }

        private string Capitalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            if (input.ToLower() == "c#") return "C#";
            if (input.ToLower() == ".net") return ".NET";
            if (input.ToLower() == "php") return "PHP";
            if (input.ToLower() == "sql") return "SQL";
            return char.ToUpper(input[0]) + input.Substring(1);
        }
    }

    public class TechStat 
    { 
        public string? Name { get; set; } 
        public int Count { get; set; } 
    }
}