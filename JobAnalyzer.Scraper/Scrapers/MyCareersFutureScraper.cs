using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// MyCareersFuture.sg — Singapur resmi hükümet iş platformu.
    /// Ücretsiz REST API, kayıt gerektirmez.
    /// https://api.mycareersfuture.gov.sg/v2/jobs
    /// </summary>
    public class MyCareersFutureScraper : ScraperBase
    {
        public override string ScraperName => "MyCareersFuture.sg (Singapore Official API)";

        private readonly string[] _keywords = {
            "software engineer", "backend developer", "frontend developer",
            "devops", "data engineer", "machine learning", "mobile developer",
            "full stack", "python", "java", "cloud engineer",
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Başlatıldı...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;
            var seenUrls = new HashSet<string>();

            foreach (var keyword in _keywords)
            {
                Console.WriteLine($"\n  🔍 '{keyword}'");
                try
                {
                    string encoded = Uri.EscapeDataString(keyword);
                    string url = $"https://api.mycareersfuture.gov.sg/v2/jobs?search={encoded}&limit=100&page=0";

                    var resp = await client.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) { Console.WriteLine($"  ⚠️ HTTP {(int)resp.StatusCode}"); continue; }

                    string json = await resp.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<McfResponse>(json, jsonOptions);
                    if (data?.Results == null || data.Results.Count == 0) { Console.WriteLine("  📭 Sonuç yok."); continue; }

                    int added = 0;
                    foreach (var job in data.Results)
                    {
                        string jobUrl = job.Metadata?.JobDetailsUrl ?? $"https://www.mycareersfuture.gov.sg/job/{job.Uuid}";
                        if (seenUrls.Contains(jobUrl)) continue;
                        seenUrls.Add(jobUrl);
                        if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                        string title   = job.Title ?? "";
                        string company = job.Postedcompany?.Name ?? "Bilinmiyor";
                        string location = job.Metadata?.LocationName ?? "Singapore";

                        int? minSal = job.Salary?.Minimum;
                        int? maxSal = job.Salary?.Maximum;

                        db.JobPostings.Add(new JobPosting
                        {
                            Title       = title.Length > 100 ? title.Substring(0, 100) : title,
                            CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                            Location    = location.Length > 100 ? location.Substring(0, 100) : location,
                            Description = job.Description ?? "",
                            Url         = jobUrl,
                            Source      = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.UtcNow,
                            DatePosted  = DateTime.TryParse(job.PostedAt, out var dt) ? dt : DateTime.UtcNow,
                            MinSalary   = minSal,
                            MaxSalary   = maxSal,
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

        private class McfResponse
        {
            [JsonPropertyName("results")] public List<McfJob>? Results { get; set; }
        }

        private class McfJob
        {
            [JsonPropertyName("uuid")]          public string?      Uuid          { get; set; }
            [JsonPropertyName("title")]         public string?      Title         { get; set; }
            [JsonPropertyName("description")]   public string?      Description   { get; set; }
            [JsonPropertyName("postedAt")]      public string?      PostedAt      { get; set; }
            [JsonPropertyName("postedCompany")] public McfCompany?  Postedcompany { get; set; }
            [JsonPropertyName("salary")]        public McfSalary?   Salary        { get; set; }
            [JsonPropertyName("metadata")]      public McfMeta?     Metadata      { get; set; }
        }

        private class McfCompany  { [JsonPropertyName("name")] public string? Name { get; set; } }
        private class McfSalary
        {
            [JsonPropertyName("minimum")] public int? Minimum { get; set; }
            [JsonPropertyName("maximum")] public int? Maximum { get; set; }
        }
        private class McfMeta
        {
            [JsonPropertyName("jobDetailsUrl")] public string? JobDetailsUrl { get; set; }
            [JsonPropertyName("locationName")]  public string? LocationName  { get; set; }
        }
    }
}
