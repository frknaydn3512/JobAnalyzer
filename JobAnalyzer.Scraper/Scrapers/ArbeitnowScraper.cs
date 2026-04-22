using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Arbeitnow.com — Tamamen ücretsiz, API key gerektirmez.
    /// Global remote yazılım ilanları. https://arbeitnow.com/api/job-board-api
    /// Sayfalama destekler: ?page=2, ?page=3 ...
    /// </summary>
    public class ArbeitnowScraper : ScraperBase
    {
        public override string ScraperName => "Arbeitnow (Remote API)";

        private static readonly string[] _softwareKeywords = {
            "software", "developer", "engineer", "backend", "frontend", "fullstack", "full-stack",
            "web", "mobile", "devops", "cloud", "data", "python", "java", "react", "angular",
            "node", ".net", "php", "qa", "test", "typescript", "kotlin", "swift", "flutter",
            "android", "ios", "golang", "api", "ai", "machine learning", "programmer", "architect",
            "infrastructure", "platform", "sre", "security", "tech", "it ", "cto", "cio",
            "yazılım", "geliştirici", "mühendis"
        };

        private static bool IsSoftwareRelated(string title, string tags)
        {
            string combined = (title + " " + tags).ToLowerInvariant();
            return _softwareKeywords.Any(kw => combined.Contains(kw));
        }

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] API Başlatıldı...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            for (int page = 1; page <= 10; page++)
            {
                Console.WriteLine($"\n  📄 Sayfa {page}...");
                try
                {
                    string url = $"https://arbeitnow.com/api/job-board-api?page={page}";
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<ArbeitnowResponse>(json, jsonOptions);

                    if (data?.Data == null || data.Data.Count == 0)
                    {
                        Console.WriteLine($"  📭 Sayfa {page}: Sonuç yok.");
                        break;
                    }

                    Console.WriteLine($"  🎉 {data.Data.Count} ilan!");
                    int pageAdded = 0;

                    foreach (var job in data.Data)
                    {
                        if (string.IsNullOrWhiteSpace(job.Url) || string.IsNullOrWhiteSpace(job.Title)) continue;
                        if (db.JobPostings.Any(j => j.Url == job.Url)) continue;

                        // Sadece yazılım ilanlarını al
                        string tagsStr = string.Join(" ", job.Tags ?? new List<string>());
                        if (!IsSoftwareRelated(job.Title, tagsStr)) continue;

                        string cleanDesc = System.Text.RegularExpressions.Regex.Replace(job.Description ?? "", "<.*?>", "");
                        cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                        string location = job.Remote ? "Remote / Global" : (job.Location ?? "Bilinmiyor");
                        string tags = string.Join(", ", job.Tags ?? new List<string>());

                        db.JobPostings.Add(new JobPosting
                        {
                            Title       = job.Title.Length > 100 ? job.Title.Substring(0, 100) : job.Title,
                            CompanyName = (job.CompanyName ?? "Bilinmiyor").Length > 100 ? job.CompanyName!.Substring(0, 100) : (job.CompanyName ?? "Bilinmiyor"),
                            Location    = location.Length > 100 ? location.Substring(0, 100) : location,
                            Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                            Url         = job.Url,
                            Source      = ScraperName,
                            ExtractedSkills = tags.Length > 500 ? tags.Substring(0, 500) : tags,
                            DateScraped = DateTime.UtcNow,
                            DatePosted  = DateTimeOffset.FromUnixTimeSeconds(job.CreatedAt).UtcDateTime
                        });
                        pageAdded++;
                        totalAdded++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ Sayfa {page}: {pageAdded} YENİ ilan eklendi.");

                    // Sonraki sayfa yoksa dur
                    if (data.Links?.Next == null) break;
                    await Task.Delay(400);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Hata (Sayfa {page}): {ex.Message}");
                    break;
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class ArbeitnowResponse
        {
            [JsonPropertyName("data")]  public List<ArbeitnowJob>? Data  { get; set; }
            [JsonPropertyName("links")] public ArbeitnowLinks?     Links { get; set; }
        }

        private class ArbeitnowLinks
        {
            [JsonPropertyName("next")] public string? Next { get; set; }
            [JsonPropertyName("prev")] public string? Prev { get; set; }
        }

        private class ArbeitnowJob
        {
            [JsonPropertyName("slug")]         public string?       Slug        { get; set; }
            [JsonPropertyName("company_name")] public string?       CompanyName { get; set; }
            [JsonPropertyName("title")]        public string?       Title       { get; set; }
            [JsonPropertyName("description")]  public string?       Description { get; set; }
            [JsonPropertyName("remote")]       public bool          Remote      { get; set; }
            [JsonPropertyName("url")]          public string?       Url         { get; set; }
            [JsonPropertyName("tags")]         public List<string>? Tags        { get; set; }
            [JsonPropertyName("location")]     public string?       Location    { get; set; }
            [JsonPropertyName("created_at")]   public long          CreatedAt   { get; set; }
        }
    }
}

