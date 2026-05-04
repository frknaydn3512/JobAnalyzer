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
    public class AdzunaScraper : ScraperBase
    {
        public override string ScraperName => "Adzuna (Global)";

        private readonly string _appId = Environment.GetEnvironmentVariable("ADZUNA_APP_ID") ?? "";
        private readonly string _appKey = Environment.GetEnvironmentVariable("ADZUNA_APP_KEY") ?? "";

        // Adzuna'nın desteklediği tüm ülkeler — TR desteklenmiyor
        // 250 istek/gün limiti var, requestCount >= 240 olunca otomatik durur
        private readonly (string country, string label, int pages)[] _targets = {
            ("gb", "UK",          2),
            ("us", "USA",         2),
            ("au", "Avustralya",  2),
            ("ca", "Kanada",      2),
            ("de", "Almanya",     2),
            ("fr", "Fransa",      2),
            ("nl", "Hollanda",    2),
            ("in", "Hindistan",   2),
            ("it", "İtalya",      2),
            ("sg", "Singapur",    2),
            ("br", "Brezilya",    2),
            ("pl", "Polonya",     2),
            ("at", "Avusturya",   1),
            ("be", "Belçika",     1),
            ("nz", "Yeni Zelanda",1),
            ("za", "G. Afrika",   1),
            ("mx", "Meksika",     1),
            ("ch", "İsviçre",     1),
            ("es", "İspanya",     1),
            ("ru", "Rusya",       1),
        };

        private readonly string[] _keywords = {
            "software developer",
            "backend developer",
            "frontend developer",
            "devops engineer",
            "data engineer",
            "full stack developer",
            "python developer",
            "mobile developer",
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Çok Ülkeli API Başlatıldı...");

            if (string.IsNullOrWhiteSpace(_appId) || _appId == "your_adzuna_app_id_here" ||
                string.IsNullOrWhiteSpace(_appKey) || _appKey == "your_adzuna_app_key_here")
            {
                Console.WriteLine("  ⚠️ ADZUNA_APP_ID veya ADZUNA_APP_KEY bulunamadı.");
                Console.WriteLine("  👉 https://developer.adzuna.com adresinden ücretsiz key al, .env dosyasına ekle.");
                return;
            }

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var existingUrls = LoadExistingUrls(db);
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

                            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                            string json = System.Text.Encoding.UTF8.GetString(bytes);
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
                                if (!existingUrls.Add(job.RedirectUrl)) continue;

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
                                    DateScraped = DateTime.UtcNow,
                                    DatePosted = DateTime.TryParse(job.Created, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.UtcNow
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

