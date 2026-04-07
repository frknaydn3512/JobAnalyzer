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
    public class HimalayasScraper : IJobScraper
    {
        public string ScraperName => "Himalayas (Global Remote API)";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] API Hortumu Bağlanıyor...");

            // Tüm tech kategorileri
            string[] categories = {
                "software-engineering",
                "devops-sysadmin",
                "data-science",
                "product-management",
                "design",
                "backend",
                "frontend",
                "mobile",
            };

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            foreach (var category in categories)
            {
                Console.WriteLine($"\n  📂 Katalog: {category}");
                try
                {
                    string apiUrl = $"https://himalayas.app/jobs/api?category={category}&limit=100";
                    var response = await client.GetAsync(apiUrl);
                    response.EnsureSuccessStatusCode();

                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<HimalayasResponse>(jsonResponse, jsonOptions);

                    if (data?.Jobs == null || data.Jobs.Count == 0)
                    {
                        Console.WriteLine($"  📭 Sonuç yok.");
                        continue;
                    }

                    Console.WriteLine($"  🎉 {data.Jobs.Count} ilan! İşleniyor...");
                    int catAdded = 0;

                    foreach (var job in data.Jobs)
                    {
                        if (string.IsNullOrWhiteSpace(job.Url) || string.IsNullOrWhiteSpace(job.Title)) continue;
                        string fullUrl = job.Url.StartsWith("http") ? job.Url : $"https://himalayas.app{job.Url}";
                        if (db.JobPostings.Any(j => j.Url == fullUrl)) continue;

                        string cleanDesc = System.Text.RegularExpressions.Regex.Replace(job.Description ?? "", "<.*?>", "");
                        cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                        db.JobPostings.Add(new JobPosting
                        {
                            Title = (job.Title ?? "").Length > 100 ? job.Title!.Substring(0, 100) : (job.Title ?? ""),
                            CompanyName = (job.CompanyName ?? "Bilinmiyor").Length > 100 ? job.CompanyName!.Substring(0, 100) : (job.CompanyName ?? "Bilinmiyor"),
                            Location = string.IsNullOrWhiteSpace(job.Location) ? "Remote / Global" : job.Location,
                            Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                            Url = fullUrl,
                            Source = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.Now,
                            DatePosted = DateTime.Now
                        });
                        catAdded++;
                        totalAdded++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ {catAdded} YENİ ilan eklendi.");
                    await Task.Delay(600);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Hata ({category}): {ex.Message}");
                }
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

            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("companyName")]
            public string? CompanyName { get; set; }

            [JsonPropertyName("location")]
            public string? Location { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }
        }
    }
}
