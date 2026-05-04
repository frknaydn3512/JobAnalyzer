using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// WorkingNomads.com — Global remote jobs free JSON API.
    /// https://www.workingnomads.com/api/exposed_jobs/?category=development
    /// Kayıt gerektirmez, ücretsiz.
    /// </summary>
    public class WorkingNomadsScraper : ScraperBase
    {
        public override string ScraperName => "WorkingNomads (Remote API)";

        private readonly string[] _categories = {
            "development", "data", "devops-sysadmin", "product", "design", "testing"
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Başlatıldı...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;
            var existingUrls = LoadExistingUrls(db);

            foreach (var category in _categories)
            {
                Console.WriteLine($"\n  📂 {category}");
                try
                {
                    string url = $"https://www.workingnomads.com/api/exposed_jobs/?category={category}";
                    var resp = await client.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) { Console.WriteLine($"  ⚠️ HTTP {(int)resp.StatusCode}"); continue; }

                    string json = await resp.Content.ReadAsStringAsync();
                    var jobs = JsonSerializer.Deserialize<List<WnJob>>(json, jsonOptions);
                    if (jobs == null || jobs.Count == 0) { Console.WriteLine("  📭 Sonuç yok."); continue; }

                    int added = 0;
                    foreach (var job in jobs)
                    {
                        string jobUrl = job.Url ?? "";
                        if (string.IsNullOrWhiteSpace(jobUrl) || string.IsNullOrWhiteSpace(job.Title)) continue;
                        if (!existingUrls.Add(jobUrl)) continue;

                        // tags alanı API'de string[] veya object[] olabilir — her ikisini de handle et
                        string skills = ExtractTags(job.Tags);

                        db.JobPostings.Add(new JobPosting
                        {
                            Title       = job.Title!.Length > 100 ? job.Title.Substring(0, 100) : job.Title,
                            CompanyName = (job.Company ?? "Bilinmiyor").Length > 100 ? job.Company!.Substring(0, 100) : (job.Company ?? "Bilinmiyor"),
                            Location    = "Remote",
                            Description = job.Description ?? "",
                            Url         = jobUrl,
                            Source      = ScraperName,
                            ExtractedSkills = skills,
                            DateScraped = DateTime.UtcNow,
                            DatePosted  = DateTimeOffset.TryParse(job.PubDate, out var dto) ? dto.UtcDateTime : DateTime.UtcNow,
                        });
                        added++; totalAdded++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ {added} YENİ ilan eklendi.");
                }
                catch (Exception ex) { Console.WriteLine($"  ❌ Hata: {ex.Message}"); }
                await Task.Delay(500);
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private static string ExtractTags(JsonElement? tags)
        {
            if (tags == null || tags.Value.ValueKind != JsonValueKind.Array) return "";
            var names = new List<string>();
            foreach (var el in tags.Value.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                    names.Add(el.GetString() ?? "");
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    if (el.TryGetProperty("name", out var nameProp)) names.Add(nameProp.GetString() ?? "");
                    else if (el.TryGetProperty("slug", out var slugProp)) names.Add(slugProp.GetString() ?? "");
                }
            }
            return string.Join(", ", names.Where(n => !string.IsNullOrWhiteSpace(n)));
        }

        private class WnJob
        {
            [JsonPropertyName("title")]       public string?       Title       { get; set; }
            [JsonPropertyName("company_name")]public string?       Company     { get; set; }
            [JsonPropertyName("url")]         public string?       Url         { get; set; }
            [JsonPropertyName("description")] public string?       Description { get; set; }
            [JsonPropertyName("pub_date")]    public string?       PubDate     { get; set; }
            [JsonPropertyName("tags")]        public JsonElement?  Tags        { get; set; }
        }
    }
}
