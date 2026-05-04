using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Djinni.co — Ukrayna ve Doğu Avrupa yazılım ilanları. Ücretsiz public API.
    /// https://djinni.co/jobs/ — JSON API uç noktası.
    /// </summary>
    public class DjinniScraper : ScraperBase
    {
        public override string ScraperName => "Djinni.co (Ukraine/EU API)";

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
            var existingUrls = LoadExistingUrls(db);

            // Djinni API offset tabanlı pagination kullanıyor (limit=10 fixed)
            Console.WriteLine($"\n  📂 Tüm ilanlar (sayfalama ile)");
            const int limit = 10;
            int consecutiveEmpty = 0;
            for (int page = 1; page <= 50; page++)
            {
                int offset = (page - 1) * limit;
                try
                {
                    string url = $"https://djinni.co/api/jobs/?offset={offset}&limit={limit}";

                    var resp = await client.GetAsync(url);
                    if (!resp.IsSuccessStatusCode)
                    {
                        // Djinni bazen 403 döner — atla
                        Console.WriteLine($"  ⚠️ HTTP {(int)resp.StatusCode}, atlanıyor.");
                        continue;
                    }

                    string json = await resp.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<DjinniResponse>(json, jsonOptions);

                    if (data?.Jobs == null || data.Jobs.Count == 0)
                    {
                        Console.WriteLine("  📭 Sonuç yok.");
                        break;
                    }

                    Console.WriteLine($"  🎉 Sayfa {page} (offset={offset}): {data.Jobs.Count} ilan | Toplam: {data.Count}");
                    int added = 0;

                    foreach (var job in data.Jobs)
                    {
                        string jobUrl = !string.IsNullOrWhiteSpace(job.AbsoluteUrl)
                            ? job.AbsoluteUrl
                            : (!string.IsNullOrWhiteSpace(job.Url)
                                ? (job.Url.StartsWith("http") ? job.Url : $"https://djinni.co{job.Url}")
                                : "");
                        if (string.IsNullOrWhiteSpace(jobUrl) || string.IsNullOrWhiteSpace(job.Title)) continue;
                        if (!existingUrls.Add(jobUrl)) continue;

                        string cleanDesc = Regex.Replace(job.LongDescription ?? "", "<.*?>", " ");
                        cleanDesc = Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                        string location = job.RemoteType == "full_remote"
                            ? "Remote / Ukraine"
                            : (job.Location ?? "Ukraine");

                        db.JobPostings.Add(new JobPosting
                        {
                            Title       = job.Title.Length > 100 ? job.Title.Substring(0, 100) : job.Title,
                            CompanyName = (job.CompanyName ?? "Bilinmiyor").Length > 100 ? job.CompanyName!.Substring(0, 100) : (job.CompanyName ?? "Bilinmiyor"),
                            Location    = location.Length > 100 ? location.Substring(0, 100) : location,
                            Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                            Url         = jobUrl,
                            Source      = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.UtcNow,
                            DatePosted  = DateTime.TryParse(job.PublishedAt, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.UtcNow
                        });
                        added++;
                        totalAdded++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ Sayfa {page}: {added} YENİ ilan eklendi.");

                    // 3 sayfa üst üste yeni ilan yoksa dur (eski ilanların arasına karışmış yeniler olabilir)
                    if (added == 0) consecutiveEmpty++;
                    else consecutiveEmpty = 0;
                    if (consecutiveEmpty >= 3) break;

                    // Tüm ilanlar tükendi
                    if (offset + limit >= data.Count) break;
                    await Task.Delay(600);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Hata (Sayfa {page}): {ex.Message}");
                    break;
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class DjinniResponse
        {
            [JsonPropertyName("results")] public List<DjinniJob>? Jobs  { get; set; }
            [JsonPropertyName("count")]   public int              Count { get; set; }
        }

        private class DjinniJob
        {
            [JsonPropertyName("title")]            public string? Title          { get; set; }
            [JsonPropertyName("company_name")]     public string? CompanyName    { get; set; }
            [JsonPropertyName("long_description")] public string? LongDescription { get; set; }
            [JsonPropertyName("location")]         public string? Location        { get; set; }
            [JsonPropertyName("remote_type")]      public string? RemoteType      { get; set; }
            [JsonPropertyName("url")]              public string? Url             { get; set; }
            [JsonPropertyName("absolute_url")]     public string? AbsoluteUrl    { get; set; }
            [JsonPropertyName("published_at")]     public string? PublishedAt    { get; set; }
        }
    }
}
