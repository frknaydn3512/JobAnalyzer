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
    public class RemotiveScraper : ScraperBase
    {
        public override string ScraperName => "Remotive (Global Remote)";

        // Remotive ücretsiz API artık kategori fark etmeksizin aynı seti döndürüyor.
        // Tek call ile tüm mevcut ilanları çek, kategori filtresi olmadan.
        private readonly string[] _techCategories = {
            "software-dev", "devops-sysadmin", "data", "qa",
            "backend", "frontend", "mobile", "machine-learning"
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] API çekiliyor...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var existingUrls = LoadExistingUrls(db);
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;

            // Önce kategorisiz genel sorgu — tüm ilanları getir
            try
            {
                string apiUrl = "https://remotive.com/api/remote-jobs?limit=200";
                Console.WriteLine($"  📡 {apiUrl}");
                var response = await client.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<RemotiveApiResponse>(json, jsonOptions);

                if (data?.Jobs != null && data.Jobs.Count > 0)
                {
                    Console.WriteLine($"  🎉 {data.Jobs.Count} ilan bulundu!");

                    foreach (var job in data.Jobs)
                    {
                        if (string.IsNullOrWhiteSpace(job.Url) || string.IsNullOrWhiteSpace(job.Title)) continue;
                        if (!existingUrls.Add(job.Url)) continue;

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
                            DateScraped = DateTime.UtcNow,
                            DatePosted = job.PublicationDate != default ? DateTime.SpecifyKind(job.PublicationDate, DateTimeKind.Utc) : DateTime.UtcNow
                        });
                        totalAdded++;
                    }
                    db.SaveChanges();
                }
                else
                {
                    Console.WriteLine("  📭 Genel sorgu sonuç vermedi.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Genel sorgu hatası: {ex.Message}");
            }

            // Kategorili sorgular — API aynı seti döndürse bile yeni ilanlar olabilir
            foreach (var category in _techCategories)
            {
                Console.WriteLine($"\n  📂 Kategori: {category}");
                try
                {
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

                    int catAdded = 0;
                    foreach (var job in data.Jobs)
                    {
                        if (string.IsNullOrWhiteSpace(job.Url) || string.IsNullOrWhiteSpace(job.Title)) continue;
                        if (!existingUrls.Add(job.Url)) continue;

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
                            DateScraped = DateTime.UtcNow,
                            DatePosted = job.PublicationDate != default ? DateTime.SpecifyKind(job.PublicationDate, DateTimeKind.Utc) : DateTime.UtcNow
                        });
                        catAdded++;
                        totalAdded++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ {catAdded} YENİ ilan eklendi.");
                    await Task.Delay(300);
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
