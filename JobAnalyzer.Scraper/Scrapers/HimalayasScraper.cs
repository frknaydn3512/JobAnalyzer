using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Himalayas.app - API tabanlı, kayıt gerektirmez, remote yazılım ilanları.
    /// Tamamen bedava ve açık JSON API kullanır.
    /// </summary>
    public class HimalayasScraper : ScraperBase
    {
        public override string ScraperName => "Himalayas (Global Remote API)";

        // Ücretsiz API kategorisiz çağrıda daha fazla sonuç verebilir
        // Kategorili çağrılar aynı seti döndürüyorsa duplikat kontrolü ile elenir
        private readonly string[] _categories = {
            "software-engineering", "devops-sysadmin", "data-science",
            "product-management", "backend", "frontend", "mobile",
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] API Hortumu Bağlanıyor...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            // Kategoriler: önce genel, sonra her kategori — her biri için sayfalama
            var targets = new List<(string? category, string label)>
            {
                (null, "Genel (tüm ilanlar)")
            };
            foreach (var cat in _categories)
                targets.Add((cat, cat));

            foreach (var (category, label) in targets)
            {
                Console.WriteLine($"\n  📂 {label}");
                int catAdded = 0;

                for (int page = 1; page <= 20; page++)
                {
                    string apiUrl = category == null
                        ? $"https://himalayas.app/jobs/api?limit=20&page={page}"
                        : $"https://himalayas.app/jobs/api?category={category}&limit=20&page={page}";

                    try
                    {
                        var response = await client.GetAsync(apiUrl);
                        response.EnsureSuccessStatusCode();

                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        var data = JsonSerializer.Deserialize<HimalayasResponse>(jsonResponse, jsonOptions);

                        if (data?.Jobs == null || data.Jobs.Count == 0)
                        {
                            Console.WriteLine($"  📭 Sayfa {page}: Sonuç yok, duruluyor.");
                            break;
                        }

                        Console.WriteLine($"  🎉 Sayfa {page}: {data.Jobs.Count} ilan");
                        int pageAdded = 0;

                        foreach (var job in data.Jobs)
                        {
                            if (string.IsNullOrWhiteSpace(job.Title)) continue;

                            string rawUrl = !string.IsNullOrWhiteSpace(job.Url) ? job.Url
                                          : !string.IsNullOrWhiteSpace(job.ApplicationLink) ? job.ApplicationLink
                                          : "";
                            if (string.IsNullOrWhiteSpace(rawUrl)) continue;

                            string fullUrl = rawUrl.StartsWith("http") ? rawUrl : $"https://himalayas.app{rawUrl}";
                            if (db.JobPostings.Any(j => j.Url == fullUrl)) continue;

                            string cleanDesc = System.Text.RegularExpressions.Regex.Replace(job.Description ?? "", "<.*?>", "");
                            cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                            string locationStr = (job.LocationRestrictions != null && job.LocationRestrictions.Count > 0)
                                ? string.Join(", ", job.LocationRestrictions)
                                : "Remote / Global";

                            db.JobPostings.Add(new JobPosting
                            {
                                Title = (job.Title ?? "").Length > 100 ? job.Title!.Substring(0, 100) : (job.Title ?? ""),
                                CompanyName = (job.CompanyName ?? "Bilinmiyor").Length > 100 ? job.CompanyName!.Substring(0, 100) : (job.CompanyName ?? "Bilinmiyor"),
                                Location = locationStr.Length > 100 ? locationStr.Substring(0, 100) : locationStr,
                                Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                                Url = fullUrl,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.UtcNow,
                                DatePosted = DateTime.UtcNow
                            });
                            pageAdded++;
                            catAdded++;
                            totalAdded++;
                        }

                        db.SaveChanges();
                        Console.WriteLine($"  ✅ {pageAdded} YENİ ilan eklendi.");

                        // Sayfada yeni ilan yoksa sonraki sayfalar da boş olacaktır
                        if (pageAdded == 0) break;
                        await Task.Delay(400);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ❌ Hata ({label} sayfa {page}): {ex.Message}");
                        break;
                    }
                }

                Console.WriteLine($"  📊 {label}: toplam {catAdded} yeni ilan");
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class HimalayasResponse
        {
            [JsonPropertyName("jobs")]
            public List<HimalayasJob> Jobs { get; set; } = new();
        }

        private class HimalayasJob
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            // Himalayasdaki ilanın kendi sayfası — her zaman dolu gelir
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            // Şirkete ait harici başvuru linki — bazen null olur
            [JsonPropertyName("applicationLink")]
            public string? ApplicationLink { get; set; }

            [JsonPropertyName("companyName")]
            public string? CompanyName { get; set; }

            [JsonPropertyName("locationRestrictions")]
            public List<string>? LocationRestrictions { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }
        }
    }
}

