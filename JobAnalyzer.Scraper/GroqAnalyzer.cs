using System.Text.RegularExpressions;
using JobAnalyzer.Data;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Scraper
{
    public class GroqAnalyzer
    {
        private static readonly string _defaultConnectionString =
            Environment.GetEnvironmentVariable("DEFAULT_CONNECTION")
            ?? throw new InvalidOperationException("DEFAULT_CONNECTION ortam değişkeni ayarlanmamış.");

        private readonly string _connectionString;

        public GroqAnalyzer(string? connectionString = null)
        {
            _connectionString = connectionString ?? _defaultConnectionString;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("\n🚀 Local Skill Analiz Motoru Başlatılıyor...");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(_connectionString);

            using var db = new AppDbContext(optionsBuilder.Options);

            var jobs = await db.JobPostings
                .Where(j => !string.IsNullOrEmpty(j.Description) && (j.ExtractedSkills == "" || j.ExtractedSkills == null))
                .ToListAsync();

            if (jobs.Count == 0)
            {
                Console.WriteLine("✅ Tüm ilanlar zaten analiz edilmiş. Yapıcak bir şey yok!");
                return;
            }

            Console.WriteLine($"🚀 Toplam {jobs.Count} ilan analiz kuyruğuna alındı. Başlıyoruz...");

            int successCount = 0;

            // Kapsamlı yetenek havuzu
            var techKeywords = new List<string>
            {
                "c#", ".net", "java", "python", "javascript", "typescript", "react", "angular", "vue", "node.js",
                "go", "golang", "ruby", "php", "swift", "kotlin", "android", "ios", "flutter", "react native",
                "sql", "mysql", "postgresql", "mongodb", "redis", "elasticsearch", "oracle", "nosql",
                "docker", "kubernetes", "aws", "azure", "gcp", "ci/cd", "jenkins", "git", "github", "gitlab",
                "linux", "bash", "powershell", "terraform", "ansible", "django", "spring boot", "asp.net core",
                "laravel", "express", "next.js", "nuxt", "tailwind", "bootstrap", "sass", "less", "html", "css",
                "graphql", "rest", "soap", "rabbitmq", "kafka", "microservices", "agile", "scrum", "jira"
            };

            foreach (var job in jobs)
            {
                var extracted = new HashSet<string>();
                string description = job.Description!.ToLower();

                foreach (var keyword in techKeywords)
                {
                    // Tam kelime eşleşmesi kontrolü (örn: "go" ararken "google" bulunmasın diye)
                    // C# ve .NET gibi özel karakter içerenler için basit contains de kullanılabilir
                    if (keyword == "c#" || keyword == ".net" || keyword == "c++")
                    {
                        if (description.Contains(keyword)) extracted.Add(keyword);
                    }
                    else
                    {
                        string pattern = $@"\b{Regex.Escape(keyword)}\b";
                        if (Regex.IsMatch(description, pattern))
                        {
                            extracted.Add(keyword);
                        }
                    }
                }

                if (extracted.Count > 0)
                {
                    job.ExtractedSkills = SkillNormalizer.Normalize(string.Join(", ", extracted));
                    successCount++;
                }
                else
                {
                    // Hiç yetenek bulunamadıysa bile boş kalmaması için "Bilinmiyor" veya işaret koy
                    job.ExtractedSkills = "Non tech"; 
                }
            }

            // Toplu kaydetme - performans için
            await db.SaveChangesAsync();

            Console.WriteLine($"\n🏁 MUAZZAM! Toplam {successCount} ilandan yerel olarak yetenek çıkarıldı.");
        }
    }
}
