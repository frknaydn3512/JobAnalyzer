using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Adzuna Multi-Country — GB, US, AU, CA, DE ülkelerinden yazılım ilanları
    /// Ücretsiz plan: 250 istek/gün, sayfa başına 50 ilan
    /// </summary>
    public class AdzunaScraper : IJobScraper
    {
        public string ScraperName => "Adzuna (Global)";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        private readonly string _appId = Environment.GetEnvironmentVariable("ADZUNA_APP_ID") ?? "";
        private readonly string _appKey = Environment.GetEnvironmentVariable("ADZUNA_APP_KEY") ?? "";

        // Ülke kodu → kaç sayfa çekileceği (sayfa başına 50 ilan)
        private readonly (string country, string label, int pages)[] _targets = {
            ("gb", "UK",        4),   // 200 ilan
            ("us", "USA",       4),   // 200 ilan
            ("au", "Avustralya",2),   // 100 ilan
            ("ca", "Kanada",    2),   // 100 ilan
            ("de", "Almanya",   2),   // 100 ilan
            ("fr", "Fransa",    1),   // 50 ilan
            ("nl", "Hollanda",  1),   // 50 ilan
        };

        private readonly string[] _keywords = {
            "software developer",
            "backend developer",
            "frontend developer",
        };

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Çok Ülkeli API Başlatıldı...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;
            int requestCount = 0;

            foreach (var keyword in _keywords)
            {
                foreach (var (country, label, pages) in _targets)
                {
                    Console.WriteLine($"\n  🌍 [{label}] '{keyword}' aranıyor ({pages} sayfa)...");

                    for (int page = 1; page <= pages; page++)
                    {
                        // Günlük limit kontrolü (250 istek/gün)
                        if (requestCount >= 240)
                        {
                            Console.WriteLine("⚠️ Günlük API limitine yaklaşıldı (240/250), duruyorum.");
                            goto Done;
                        }

                        try
                        {
                            string encodedKw = Uri.EscapeDataString(keyword);
                            string apiUrl = $"https://api.adzuna.com/v1/api/jobs/{country}/search/{page}" +
                                            $"?app_id={_appId}&app_key={_appKey}" +
                                            $"&what={encodedKw}&results_per_page=50&content-type=application/json";

                            var response = await client.GetAsync(apiUrl);
                            requestCount++;

                            if (!response.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"    ⚠️ HTTP {(int)response.StatusCode} — atlandı.");
                                break;
                            }

                            string json = await response.Content.ReadAsStringAsync();
                            var data = JsonSerializer.Deserialize<AdzunaResponse>(json, jsonOptions);

                            if (data?.Results == null || data.Results.Count == 0)
                            {
                                Console.WriteLine($"    📭 Sayfa {page}: Sonuç yok.");
                                break;
                            }

                            int pageAdded = 0;
                            foreach (var job in data.Results)
                            {
                                if (string.IsNullOrWhiteSpace(job.RedirectUrl) || string.IsNullOrWhiteSpace(job.Title)) continue;
                                if (db.JobPostings.Any(j => j.Url == job.RedirectUrl)) continue;

                                string cleanDesc = System.Text.RegularExpressions.Regex.Replace(job.Description ?? "", "<.*?>", "");
                                cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                                db.JobPostings.Add(new JobPosting
                                {
                                    Title = (job.Title ?? "").Length > 100 ? job.Title!.Substring(0, 100) : (job.Title ?? ""),
                                    CompanyName = (job.Company?.DisplayName ?? "Bilinmiyor").Length > 100
                                        ? job.Company!.DisplayName!.Substring(0, 100)
                                        : (job.Company?.DisplayName ?? "Bilinmiyor"),
                                    Location = $"{label} - {job.Location?.DisplayName ?? "Bilinmiyor"}",
                                    Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                                    Url = job.RedirectUrl,
                                    Source = ScraperName,
                                    ExtractedSkills = "",
                                    DateScraped = DateTime.Now,
                                    DatePosted = DateTime.TryParse(job.Created, out var dt) ? dt : DateTime.Now
                                });
                                pageAdded++;
                                totalAdded++;
                            }

                            db.SaveChanges();
                            Console.WriteLine($"    ✅ Sayfa {page}: {pageAdded} YENİ ilan ({data.Results.Count} bulundu, {requestCount} istek kullanıldı)");
                            await Task.Delay(400); // API'ye nazik ol
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"    ❌ Hata: {ex.Message}");
                        }
                    }
                }
            }

            Done:
            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan, {requestCount} API isteği.");
        }

        private class AdzunaResponse
        {
            [JsonPropertyName("results")] public List<AdzunaJob>? Results { get; set; }
        }

        private class AdzunaJob
        {
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("redirect_url")] public string? RedirectUrl { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("created")] public string? Created { get; set; }
            [JsonPropertyName("company")] public AdzunaCompany? Company { get; set; }
            [JsonPropertyName("location")] public AdzunaLocation? Location { get; set; }
        }

        private class AdzunaCompany
        {
            [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        }

        private class AdzunaLocation
        {
            [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        }
    }
}
