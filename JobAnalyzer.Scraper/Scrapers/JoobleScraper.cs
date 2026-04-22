using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class JoobleScraper : ScraperBase
    {
        public override string ScraperName => "Jooble Turkey";
        
        private readonly string _apiKey = Environment.GetEnvironmentVariable("JOOBLE_API_KEY") ?? "";

        private readonly string[] _keywords = {
            // Türkçe
            "yazılım geliştirici", "yazılım mühendisi", "backend geliştirici",
            "frontend geliştirici", "mobil geliştirici", "veri mühendisi",
            // İngilizce
            "software developer", "software engineer",
            "backend developer", "frontend developer",
            "fullstack developer", "full stack developer",
            "devops engineer", "mobile developer",
            "golang developer",
            "data engineer", "python developer",
            "react developer", ".net developer",
            "java developer", "node.js developer",
            "flutter developer", "android developer", "ios developer",
            "machine learning engineer", "data scientist",
            "qa engineer", "cloud engineer",
        };

        // Jooble Türkiye için şehir bazlı arama — "Turkey" ile 0-3 sonuç geliyor,
        // şehir adları daha iyi sonuç veriyor.
        private readonly string[] _locations = {
            "",           // global (tüm dünya — Türk şirketlerin İngilizce ilanları da dahil)
            "İstanbul",
            "Ankara",
            "İzmir",
            "Bursa",
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Başlatıldı...");

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                Console.WriteLine("⚠️ JOOBLE_API_KEY bulunamadı! Lütfen .env dosyanıza API anahtarınızı ekleyin.");
                return;
            }

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            foreach (var location in _locations)
            foreach (var keyword in _keywords)
            {
                string locLabel = string.IsNullOrEmpty(location) ? "Global" : location;
                Console.WriteLine($"\n🔍 [{locLabel}] '{keyword}' aranıyor...");

                for (int page = 1; page <= 10; page++)
                {
                    try
                    {
                        var requestBody = new
                        {
                            keywords = keyword,
                            location = location,
                            page = page
                        };

                        string jsonPayload = JsonSerializer.Serialize(requestBody);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        string apiUrl = $"https://jooble.org/api/{_apiKey}";
                        var response = await client.PostAsync(apiUrl, content);

                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"    ⚠️ HTTP {(int)response.StatusCode} — API Reddedildi veya kota bitti.");
                            break;
                        }

                        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                        string jsonResponse = Encoding.UTF8.GetString(bytes);
                        
                        var data = JsonSerializer.Deserialize<JoobleResponse>(jsonResponse, jsonOptions);

                        if (data?.Jobs == null || data.Jobs.Count == 0)
                        {
                            Console.WriteLine($"    📭 Sayfa {page}: Sonuç yok.");
                            break;
                        }

                        int pageAdded = 0;
                        foreach (var job in data.Jobs)
                        {
                            if (string.IsNullOrWhiteSpace(job.Link) || string.IsNullOrWhiteSpace(job.Title)) continue;
                            if (db.JobPostings.Any(j => j.Url == job.Link)) continue;

                            string cleanDesc = System.Text.RegularExpressions.Regex.Replace(job.Snippet ?? "", "<.*?>", "");
                            cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                            db.JobPostings.Add(new JobPosting
                            {
                                Title = job.Title.Length > 100 ? job.Title.Substring(0, 100) : job.Title,
                                CompanyName = (job.Company ?? "Bilinmiyor").Length > 100 ? job.Company!.Substring(0, 100) : (job.Company ?? "Bilinmiyor"),
                                Location = (job.Location ?? "Türkiye").Length > 100 ? job.Location!.Substring(0, 100) : (job.Location ?? "Türkiye"),
                                Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                                Url = job.Link,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.UtcNow,
                                DatePosted = DateTime.TryParse(job.Updated, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.UtcNow
                            });
                            pageAdded++;
                            totalAdded++;
                        }

                        db.SaveChanges();
                        Console.WriteLine($"    ✅ Sayfa {page}: {pageAdded} YENİ ilan ({data.Jobs.Count} bulundu)");
                        await Task.Delay(500); // Wait between requests
                    }
                    catch (Exception ex)
                    {
                        string detail = ex.InnerException?.InnerException?.Message
                                     ?? ex.InnerException?.Message
                                     ?? ex.Message;
                        Console.WriteLine($"    ⚠️ Sayfa {page} hata: {detail} — devam ediliyor...");
                        continue;
                    }
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class JoobleResponse
        {
            [JsonPropertyName("jobs")] public List<JoobleJob>? Jobs { get; set; }
        }

        private class JoobleJob
        {
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("location")] public string? Location { get; set; }
            [JsonPropertyName("snippet")] public string? Snippet { get; set; }
            [JsonPropertyName("salary")] public string? Salary { get; set; }
            [JsonPropertyName("source")] public string? Source { get; set; }
            [JsonPropertyName("type")] public string? Type { get; set; }
            [JsonPropertyName("link")] public string? Link { get; set; }
            [JsonPropertyName("company")] public string? Company { get; set; }
            [JsonPropertyName("updated")] public string? Updated { get; set; }
            [JsonPropertyName("id")] public long? Id { get; set; }
        }
    }
}
