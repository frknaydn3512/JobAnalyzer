using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Remotive.com - Tüm kategoriler + çoklu sayfa
    /// API: https://remotive.com/api/remote-jobs?category=X&limit=100
    /// </summary>
    public class RemotiveScraper : IJobScraper
    {
        public string ScraperName => "Remotive (Global Remote)";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        // Remotive'in tüm yazılım/tech kategorileri
        private readonly string[] _categories = {
            "software-dev",
            "devops-sysadmin",
            "data",
            "qa",
            "product",
            "design",
            "backend",
            "frontend",
            "mobile",
            "machine-learning",
        };

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Tüm kategoriler çekiliyor...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            foreach (var category in _categories)
            {
                Console.WriteLine($"\n  📂 Kategori: {category}");
                try
                {
                    // limit=200 ile maksimum ilan çek
                    string apiUrl = $"https://remotive.com/api/remote-jobs?category={category}&limit=200";
                    var response = await client.GetAsync(apiUrl);
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<RemotiveApiResponse>(json, jsonOptions);

                    if (data?.Jobs == null || data.Jobs.Count == 0)
                    {
                        Console.WriteLine($"  📭 Sonuç yok.");
                        continue;
                    }

                    Console.WriteLine($"  🎉 {data.Jobs.Count} ilan bulundu!");
                    int catAdded = 0;

                    foreach (var job in data.Jobs)
                    {
                        if (string.IsNullOrWhiteSpace(job.Url) || string.IsNullOrWhiteSpace(job.Title)) continue;
                        if (db.JobPostings.Any(j => j.Url == job.Url)) continue;

                        string cleanDesc = System.Text.RegularExpressions.Regex.Replace(job.Description ?? "", "<.*?>", "");
                        cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();

                        db.JobPostings.Add(new JobPosting
                        {
                            Title = job.Title.Length > 100 ? job.Title.Substring(0, 100) : job.Title,
                            CompanyName = (job.CompanyName ?? "Bilinmiyor").Length > 100 ? job.CompanyName!.Substring(0, 100) : (job.CompanyName ?? "Bilinmiyor"),
                            Location = (job.CandidateRequiredLocation ?? "Remote").Length > 100 ? job.CandidateRequiredLocation!.Substring(0, 100) : (job.CandidateRequiredLocation ?? "Remote"),
                            Description = cleanDesc.Length > 4000 ? cleanDesc.Substring(0, 4000) : cleanDesc,
                            Url = job.Url,
                            Source = ScraperName,
                            ExtractedSkills = string.Join(",", job.Tags ?? new List<string>()),
                            DateScraped = DateTime.Now,
                            DatePosted = job.PublicationDate != default ? job.PublicationDate : DateTime.Now
                        });
                        catAdded++;
                        totalAdded++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ {catAdded} YENİ ilan eklendi.");
                    await Task.Delay(500); // API'ye nazik ol
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Hata ({category}): {ex.Message}");
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class RemotiveApiResponse
        {
            [JsonPropertyName("jobs")]
            public List<RemotiveJob>? Jobs { get; set; }
        }

        private class RemotiveJob
        {
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("company_name")] public string? CompanyName { get; set; }
            [JsonPropertyName("url")] public string Url { get; set; } = "";
            [JsonPropertyName("candidate_required_location")] public string? CandidateRequiredLocation { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
            [JsonPropertyName("publication_date")] public DateTime PublicationDate { get; set; }
        }
    }
}