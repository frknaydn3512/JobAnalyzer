using JobAnalyzer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Web.Pages
{
    public class SalaryBenchmarkModel : PageModel
    {
        private readonly AppDbContext _db;
        public SalaryBenchmarkModel(AppDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public string? Category { get; set; }
        [BindProperty(SupportsGet = true)] public string? Location { get; set; }
        [BindProperty(SupportsGet = true)] public string? Level { get; set; }

        public int TotalAnalyzed { get; set; }
        public List<SkillStat> SkillStats { get; set; } = new();

        // Skill → kategori map
        private static readonly Dictionary<string, string> _categoryMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Frontend
            { "React", "Frontend" }, { "Angular", "Frontend" }, { "Vue.js", "Frontend" },
            { "JavaScript", "Frontend" }, { "TypeScript", "Frontend" }, { "HTML", "Frontend" },
            { "CSS", "Frontend" }, { "Tailwind CSS", "Frontend" }, { "Bootstrap", "Frontend" },
            { "Next.js", "Frontend" }, { "Nuxt.js", "Frontend" },
            // Backend
            { "C#", "Backend" }, { ".NET", "Backend" }, { "ASP.NET Core", "Backend" },
            { "Python", "Backend" }, { "Java", "Backend" }, { "Spring Boot", "Backend" },
            { "Node.js", "Backend" }, { "Express.js", "Backend" }, { "Django", "Backend" },
            { "Flask", "Backend" }, { "FastAPI", "Backend" }, { "PHP", "Backend" },
            { "Ruby", "Backend" }, { "Go", "Backend" }, { "Rust", "Backend" }, { "Scala", "Backend" },
            // Database
            { "SQL", "Database" }, { "SQL Server", "Database" }, { "MySQL", "Database" },
            { "PostgreSQL", "Database" }, { "MongoDB", "Database" }, { "Redis", "Database" },
            { "Elasticsearch", "Database" }, { "Oracle", "Database" }, { "SQLite", "Database" },
            // DevOps
            { "Docker", "DevOps" }, { "Kubernetes", "DevOps" }, { "AWS", "DevOps" },
            { "Azure", "DevOps" }, { "GCP", "DevOps" }, { "CI/CD", "DevOps" },
            { "Jenkins", "DevOps" }, { "Terraform", "DevOps" }, { "Linux", "DevOps" },
            { "GitHub Actions", "DevOps" }, { "GitLab CI", "DevOps" }, { "Ansible", "DevOps" },
            // Mobile
            { "Flutter", "Mobile" }, { "React Native", "Mobile" }, { "Swift", "Mobile" },
            { "Kotlin", "Mobile" }, { "Android", "Mobile" }, { "iOS", "Mobile" },
            // AI/ML
            { "Machine Learning", "AI" }, { "Deep Learning", "AI" }, { "TensorFlow", "AI" },
            { "PyTorch", "AI" }, { "LangChain", "AI" }, { "NLP", "AI" }, { "LLM", "AI" },
        };

        public async Task OnGetAsync()
        {
            var query = _db.JobPostings
                .Where(j => !string.IsNullOrEmpty(j.ExtractedSkills));

            if (!string.IsNullOrEmpty(Location))
                query = query.Where(j => j.Location != null && j.Location.ToLower().Contains(Location.ToLower()));

            if (!string.IsNullOrEmpty(Level))
                query = query.Where(j => j.Level != null && j.Level.ToLower().Contains(Level.ToLower()));

            TotalAnalyzed = await query.CountAsync();

            var allSkillsRaw = await query.Select(j => j.ExtractedSkills).ToListAsync();

            var skillCounts = allSkillsRaw
                .Where(s => s != null)
                .SelectMany(s => s!.Split(','))
                .Select(s => s.Trim())
                .Where(s => s.Length > 1)
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count());

            var stats = skillCounts
                .Select(kv => new SkillStat
                {
                    Name = kv.Key,
                    Count = kv.Value,
                    Category = _categoryMap.TryGetValue(kv.Key, out var cat) ? cat : "Diğer"
                })
                .Where(s => string.IsNullOrEmpty(Category) || s.Category == Category)
                .OrderByDescending(s => s.Count)
                .Take(50)
                .ToList();

            SkillStats = stats;
        }

        public class SkillStat
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public string Category { get; set; } = "Diğer";
        }
    }
}
