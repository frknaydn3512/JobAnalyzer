using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class RemoteOKScraper : ScraperBase
    {
        public override string ScraperName => "RemoteOK (Global Remote)";

        private static readonly string[] _softwareKeywords = {
            "software", "developer", "engineer", "backend", "frontend", "fullstack", "full-stack",
            "web", "mobile", "devops", "cloud", "data", "python", "java", "react", "angular",
            "node", ".net", "php", "qa", "test", "typescript", "kotlin", "swift", "flutter",
            "android", "ios", "golang", "api", "ai", "machine learning", "programmer", "architect",
            "infrastructure", "platform", "sre", "security", "tech", "cto", "cio",
            "yazılım", "geliştirici", "mühendis"
        };

        private static bool IsSoftwareRelated(string title, string tags) =>
            _softwareKeywords.Any(kw => (title + " " + tags).ToLowerInvariant().Contains(kw));

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor... (Ücretsiz JSON API!)");

            // RemoteOK'un herkese açık ücretsiz API'si — kayıt gerekmez!
            string apiUrl = "https://remoteok.com/api";

            try
            {
                using HttpClient client = new HttpClient();
                // RemoteOK bot engelini atlatmak için gerçek bir User-Agent şart
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                Console.WriteLine("⏳ RemoteOK API'den veriler çekiliyor (tarayıcı açılmaz, saniyeler sürer)...");

                HttpResponseMessage response = await client.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // RemoteOK API'si ilk eleman olarak metadata döndürür, onu atlıyoruz
                // Bu yüzden JsonElement dizisi olarak parse ediyoruz
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rawArray = JsonSerializer.Deserialize<JsonElement[]>(jsonResponse, options);

                if (rawArray == null || rawArray.Length < 2)
                {
                    Console.WriteLine("⚠️ RemoteOK API boş veri döndürdü.");
                    return;
                }

                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseNpgsql(ConnectionString);
                using var db = new AppDbContext(optionsBuilder.Options);

                int addedCount = 0;

                // İlk eleman metadata, 1'den itibaren gerçek ilanlar
                foreach (var element in rawArray.Skip(1))
                {
                    try
                    {
                        string jobUrl = element.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                        string title = element.TryGetProperty("position", out var titleProp) ? titleProp.GetString() ?? "" : "";
                        string company = element.TryGetProperty("company", out var companyProp) ? companyProp.GetString() ?? "Bilinmiyor" : "Bilinmiyor";
                        string location = element.TryGetProperty("location", out var locProp) ? locProp.GetString() ?? "Remote / Worldwide" : "Remote / Worldwide";
                        string description = element.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";

                        // Tagsleri skill olarak alalım
                        string skills = "";
                        if (element.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                        {
                            skills = string.Join(", ", tagsProp.EnumerateArray().Select(t => t.GetString() ?? ""));
                        }

                        if (string.IsNullOrWhiteSpace(jobUrl) || string.IsNullOrWhiteSpace(title)) continue;

                        // Tam URL'yi oluştur
                        if (!jobUrl.StartsWith("http"))
                            jobUrl = "https://remoteok.com" + jobUrl;

                        // Sadece yazılım ilanlarını al
                        if (!IsSoftwareRelated(title, skills)) continue;

                        // Aynı ilan varsa ekleme
                        if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                        // HTML temizle
                        string cleanDescription = System.Text.RegularExpressions.Regex.Replace(description, "<.*?>", String.Empty);
                        cleanDescription = System.Text.RegularExpressions.Regex.Replace(cleanDescription, @"\s+", " ").Trim();

                        var newJob = new JobPosting
                        {
                            Title = title.Length > 100 ? title.Substring(0, 100) : title,
                            CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                            Location = location.Length > 100 ? location.Substring(0, 100) : location,
                            Description = cleanDescription,
                            Url = jobUrl,
                            Source = ScraperName,
                            ExtractedSkills = skills.Length > 500 ? skills.Substring(0, 500) : skills,
                            DateScraped = DateTime.UtcNow,
                            DatePosted = DateTime.UtcNow
                        };

                        db.JobPostings.Add(newJob);
                        addedCount++;
                        Console.WriteLine($"   ✅ {company} | {title}");
                    }
                    catch (Exception itemEx)
                    {
                        Console.WriteLine($"   ⚠️ İlan parse hatası: {itemEx.Message}");
                    }
                }

                db.SaveChanges();
                Console.WriteLine($"\n✅ RemoteOK Tamamlandı! {addedCount} YENİ ilan eklendi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ RemoteOK Hata: {ex.Message}");
            }
        }
    }
}

