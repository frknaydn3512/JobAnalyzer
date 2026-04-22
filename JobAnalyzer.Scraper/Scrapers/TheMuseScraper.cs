using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// The Muse — Ücretsiz public REST API. API key gerektirmez.
    /// Yazılım kategorisi filtreli. https://www.themuse.com/api/public/jobs
    /// </summary>
    public class TheMuseScraper : ScraperBase
    {
        public override string ScraperName => "TheMuse (Global API)";

        // TheMuse kategori listesi — yazılım ile ilgili olanlar
        private readonly string[] _categories = {
            "Software Engineer",
            "Data Science",
            "IT & Systems",
            "QA & Testing",
            "Product & Project Management",
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] API Başlatıldı...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            int totalAdded = 0;
            var seenUrls = new HashSet<string>();

            foreach (var category in _categories)
            {
                Console.WriteLine($"\n  📂 Kategori: {category}");
                string encodedCat = Uri.EscapeDataString(category);

                // TheMuse API sayfalama: page=0 başlar
                for (int page = 0; page < 5; page++)
                {
                    try
                    {
                        string url = $"https://www.themuse.com/api/public/jobs?category={encodedCat}&page={page}&level=Senior+Level&level=Mid+Level&level=Entry+Level";
                        var resp = await client.GetAsync(url);
                        resp.EnsureSuccessStatusCode();

                        string json = await resp.Content.ReadAsStringAsync();
                        var data = JsonSerializer.Deserialize<MuseResponse>(json, jsonOptions);

                        if (data?.Results == null || data.Results.Count == 0) break;

                        int pageAdded = 0;
                        foreach (var job in data.Results)
                        {
                            string jobUrl = job.Refs?.LandingPage ?? "";
                            if (string.IsNullOrWhiteSpace(jobUrl) || string.IsNullOrWhiteSpace(job.Name)) continue;
                            if (seenUrls.Contains(jobUrl)) continue;
                            seenUrls.Add(jobUrl);
                            if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                            string location = job.Locations?.FirstOrDefault()?.Name ?? "Remote / Global";
                            string company = job.Company?.Name ?? "Bilinmiyor";

                            // İçerik: HTML var ise temizle
                            string desc = job.Contents ?? "";
                            desc = Regex.Replace(desc, "<.*?>", " ");
                            desc = Regex.Replace(desc, @"\s+", " ").Trim();

                            db.JobPostings.Add(new JobPosting
                            {
                                Title       = job.Name.Length > 100 ? job.Name.Substring(0, 100) : job.Name,
                                CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                                Location    = location.Length > 100 ? location.Substring(0, 100) : location,
                                Description = desc.Length > 4000 ? desc.Substring(0, 4000) : desc,
                                Url         = jobUrl,
                                Source      = ScraperName,
                                ExtractedSkills = category,
                                DateScraped = DateTime.UtcNow,
                                DatePosted  = DateTime.TryParse(job.PublicationDate, out var dt) ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : DateTime.UtcNow
                            });
                            pageAdded++;
                            totalAdded++;
                        }

                        db.SaveChanges();
                        Console.WriteLine($"    Sayfa {page}: {pageAdded} ilan eklendi.");

                        // Son sayfaya ulaştık mı?
                        if (page >= data.PageCount - 1) break;

                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    ❌ Hata (Sayfa {page}): {ex.Message}");
                        break;
                    }
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private class MuseResponse
        {
            [JsonPropertyName("results")]   public List<MuseJob>? Results  { get; set; }
            [JsonPropertyName("page_count")] public int PageCount  { get; set; }
        }

        private class MuseJob
        {
            [JsonPropertyName("name")]             public string?         Name            { get; set; }
            [JsonPropertyName("contents")]         public string?         Contents        { get; set; }
            [JsonPropertyName("publication_date")] public string?         PublicationDate { get; set; }
            [JsonPropertyName("refs")]             public MuseRefs?       Refs            { get; set; }
            [JsonPropertyName("company")]          public MuseCompany?    Company         { get; set; }
            [JsonPropertyName("locations")]        public List<MuseLoc>?  Locations       { get; set; }
        }

        private class MuseRefs
        {
            [JsonPropertyName("landing_page")] public string? LandingPage { get; set; }
        }

        private class MuseCompany
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
        }

        private class MuseLoc
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
        }
    }
}
