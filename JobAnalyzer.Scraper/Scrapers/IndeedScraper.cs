using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Indeed İlanları — RapidAPI JSearch üzerinden çekilir.
    /// Browser scraping + Cloudflare bypass yerine API kullanılır.
    /// RAPIDAPI_KEY env var gerekli.
    /// </summary>
    public class IndeedScraper : ScraperBase
    {
        public override string ScraperName => "Indeed (via RapidAPI)";

        private readonly string _apiKey  = Environment.GetEnvironmentVariable("RAPIDAPI_KEY")  ?? "";
        private readonly string _apiHost = Environment.GetEnvironmentVariable("RAPIDAPI_HOST") ?? "jsearch.p.rapidapi.com";

        private readonly string[] _keywords = {
            "software developer",
            "software engineer",
            "backend developer",
            "frontend developer",
            "fullstack developer",
            "devops engineer",
            "mobile developer",
            "data engineer",
            "python developer",
            "react developer",
            ".net developer",
            "java developer",
            "yazılım geliştirici",
            "yazılım mühendisi",
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Başlatıldı...");

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                Console.WriteLine("  ⚠️ RAPIDAPI_KEY bulunamadı. Atlanıyor.");
                return;
            }

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-rapidapi-key",  _apiKey);
            client.DefaultRequestHeaders.Add("x-rapidapi-host", _apiHost);
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var existingUrls = LoadExistingUrls(db);
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            foreach (var keyword in _keywords)
            {
                Console.WriteLine($"\n  🔍 '{keyword}'");
                try
                {
                    string encoded = Uri.EscapeDataString(keyword);
                    string url = $"https://{_apiHost}/search?query={encoded}&page=1&num_pages=3";

                    var resp = await client.GetAsync(url);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"  ⚠️ HTTP {(int)resp.StatusCode}");
                        break;
                    }

                    string json = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"  🔎 API yanıtı (ilk 300 karakter): {json[..Math.Min(300, json.Length)]}");

                    if (json.Contains("\"message\"") && json.Contains("subscribe"))
                    {
                        Console.WriteLine("  ❌ RapidAPI kota hatası.");
                        return;
                    }

                    var data = JsonSerializer.Deserialize<JSearchResponse>(json, jsonOptions);
                    if (data?.Data == null || data.Data.Count == 0)
                    {
                        Console.WriteLine("  📭 Sonuç yok.");
                        continue;
                    }

                    int added = 0;
                    foreach (var job in data.Data)
                    {
                        string jobUrl = job.JobApplyLink ?? job.JobUrl ?? "";
                        if (string.IsNullOrWhiteSpace(jobUrl) || string.IsNullOrWhiteSpace(job.JobTitle)) continue;
                        if (!existingUrls.Add(jobUrl)) continue;

                        string jobLocation = !string.IsNullOrWhiteSpace(job.JobCity)
                            ? $"{job.JobCity}, {job.JobCountry ?? "Türkiye"}"
                            : (job.JobCountry ?? "Türkiye");
                        string cleanDesc = System.Text.RegularExpressions.Regex.Replace(job.JobDescription ?? "", "<.*?>", " ");
                        cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                        db.JobPostings.Add(new JobPosting
                        {
                            Title       = job.JobTitle.Length > 100 ? job.JobTitle.Substring(0, 100) : job.JobTitle,
                            CompanyName = (job.EmployerName ?? "Bilinmiyor").Length > 100 ? job.EmployerName!.Substring(0, 100) : (job.EmployerName ?? "Bilinmiyor"),
                            Location    = jobLocation.Length > 100 ? jobLocation.Substring(0, 100) : jobLocation,
                            Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                            Url         = jobUrl,
                            Source      = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.UtcNow,
                            DatePosted  = DateTime.UtcNow
                        });
                        added++;
                        totalAdded++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ {added} YENİ ilan eklendi.");
                    await Task.Delay(1500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Hata: {ex.Message}");
                    break;
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class JSearchResponse
        {
            [JsonPropertyName("data")] public List<JSearchJob>? Data { get; set; }
        }

        private class JSearchJob
        {
            [JsonPropertyName("job_title")]         public string? JobTitle       { get; set; }
            [JsonPropertyName("employer_name")]     public string? EmployerName   { get; set; }
            [JsonPropertyName("job_city")]          public string? JobCity        { get; set; }
            [JsonPropertyName("job_country")]       public string? JobCountry     { get; set; }
            [JsonPropertyName("job_description")]   public string? JobDescription { get; set; }
            [JsonPropertyName("job_apply_link")]    public string? JobApplyLink   { get; set; }
            [JsonPropertyName("job_url")]           public string? JobUrl         { get; set; }
        }
    }
}
