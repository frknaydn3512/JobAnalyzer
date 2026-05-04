using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Landing.jobs — Avrupa startup iş ilanları. Ücretsiz REST API.
    /// https://landing.jobs/api/v1/jobs?remote=true&offer_salary=true
    /// </summary>
    public class LandingJobsScraper : ScraperBase
    {
        public override string ScraperName => "Landing.jobs (Europe API)";

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] API Başlatıldı...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var existingUrls = LoadExistingUrls(db);
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            string[] endpoints = {
                "https://landing.jobs/api/v1/jobs?remote=true&limit=100",
                "https://landing.jobs/api/v1/jobs?remote=false&limit=100",
            };

            foreach (var baseUrl in endpoints)
            {
                for (int page = 1; page <= 5; page++)
                {
                    string url = $"{baseUrl}&page={page}";
                    Console.WriteLine($"\n  📄 {url}");
                    try
                    {
                        var resp = await client.GetAsync(url);
                        if (!resp.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"  ❌ HTTP {(int)resp.StatusCode}");
                            break;
                        }

                        string json = await resp.Content.ReadAsStringAsync();

                        // API format tespiti: object {"jobs":[...]} veya düz array [...] dene
                        List<LandingJob>? jobs = null;
                        if (json.TrimStart().StartsWith("["))
                        {
                            jobs = JsonSerializer.Deserialize<List<LandingJob>>(json, jsonOptions);
                        }
                        else
                        {
                            var data = JsonSerializer.Deserialize<LandingResponse>(json, jsonOptions);
                            jobs = data?.Jobs;
                        }

                        if (jobs == null || jobs.Count == 0) break;

                        int pageAdded = 0;
                        foreach (var job in jobs)
                        {
                            string jobUrl = !string.IsNullOrEmpty(job.Url)
                                ? (job.Url.StartsWith("http") ? job.Url : $"https://landing.jobs{job.Url}")
                                : "";
                            if (string.IsNullOrWhiteSpace(jobUrl) || string.IsNullOrWhiteSpace(job.Title)) continue;
                            if (!existingUrls.Add(jobUrl)) continue;

                            string cleanDesc = Regex.Replace(job.Description ?? "", "<.*?>", " ");
                            cleanDesc = Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                            string location = job.Remote ? "Remote / Europe" : (job.City ?? job.Country ?? "Europe");

                            db.JobPostings.Add(new JobPosting
                            {
                                Title       = job.Title.Length > 100 ? job.Title.Substring(0, 100) : job.Title,
                                CompanyName = (job.Company?.Name ?? "Bilinmiyor").Length > 100 ? (job.Company?.Name ?? "Bilinmiyor").Substring(0, 100) : (job.Company?.Name ?? "Bilinmiyor"),
                                Location    = location.Length > 100 ? location.Substring(0, 100) : location,
                                Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                                Url         = jobUrl,
                                Source      = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.UtcNow,
                                DatePosted  = DateTime.TryParse(job.CreatedAt, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.UtcNow
                            });
                            pageAdded++;
                            totalAdded++;
                        }

                        db.SaveChanges();
                        Console.WriteLine($"  ✅ Sayfa {page}: {pageAdded} YENİ ilan eklendi.");
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ❌ Hata: {ex.Message}");
                        if (ex.InnerException != null)
                            Console.WriteLine($"     Inner: {ex.InnerException.Message}");
                        break;
                    }
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class LandingResponse
        {
            [JsonPropertyName("jobs")] public List<LandingJob>? Jobs { get; set; }
        }

        private class LandingJob
        {
            [JsonPropertyName("title")]       public string?        Title       { get; set; }
            [JsonPropertyName("url")]         public string?        Url         { get; set; }
            [JsonPropertyName("description")] public string?        Description { get; set; }
            [JsonPropertyName("remote")]      public bool           Remote      { get; set; }
            [JsonPropertyName("city")]        public string?        City        { get; set; }
            [JsonPropertyName("country")]     public string?        Country     { get; set; }
            [JsonPropertyName("created_at")]  public string?        CreatedAt   { get; set; }
            [JsonPropertyName("company")]     public LandingCompany? Company    { get; set; }
        }

        private class LandingCompany
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
        }
    }
}
