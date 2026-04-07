using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class FreelancerComScraper : IJobScraper
    {
        public string ScraperName => "Freelancer.com (Global API)";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor... (Full Metin Modu)");

            // full_description=true parametresini ekleyerek API'yi zorluyoruz
            string apiUrl = "https://www.freelancer.com/api/projects/0.1/projects/active?limit=100&query=developer&job_details=true&full_description=true";

            try
            {
                using HttpClient client = new HttpClient();
                // API bazen User-Agent yoksa veriyi kırpar, bunu eklemek hayat kurtarır
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                HttpResponseMessage response = await client.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var freelancerData = JsonSerializer.Deserialize<FreelancerApiResponse>(jsonResponse, options);

                if (freelancerData?.Result?.Projects != null)
                {
                    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                    optionsBuilder.UseSqlServer(_connectionString);
                    using var db = new AppDbContext(optionsBuilder.Options);

                    int addedCount = 0;

                    foreach (var project in freelancerData.Result.Projects)
                    {
                        // Filtreleme (Senin filtren aynen duruyor)
                        string titleLower = project.Title.ToLower();
                        if (!titleLower.Contains("developer") && !titleLower.Contains("software") &&
                            !titleLower.Contains("app ") && !titleLower.Contains("api") &&
                            !titleLower.Contains(".net") && !titleLower.Contains("c#") &&
                            !titleLower.Contains("react") && !titleLower.Contains("script")) continue;

                        string fullUrl = $"https://www.freelancer.com/projects/{project.SeoUrl}";
                        if (db.JobPostings.Any(j => j.Url == fullUrl)) continue;

                        // --- KRİTİK DÜZELTME BÖLGESİ ---
                        // İki alanı da kontrol et ve en uzun olanı baz al (bazen biri diğerinden daha kapsamlı gelir)
                        string desc = project.Description ?? "";
                        string prev = project.PreviewDescription ?? "";

                        string finalRaw = desc.Length >= prev.Length ? desc : prev;

                        if (string.IsNullOrWhiteSpace(finalRaw)) finalRaw = project.Title;

                        // Regex temizliğini yaparken metnin sonundaki boşlukları ve gizli karakterleri de süpürelim
                        string cleanDescription = System.Text.RegularExpressions.Regex.Replace(finalRaw, "<.*?>", string.Empty);
                        cleanDescription = System.Text.RegularExpressions.Regex.Replace(cleanDescription, @"\s+", " ").Trim();
                        // -------------------------------

                        db.JobPostings.Add(new JobPosting
                        {
                            Title = project.Title.Length > 100 ? project.Title.Substring(0, 100) : project.Title,
                            CompanyName = "Freelance Müşteri",
                            Location = "Global (Remote)",
                            Description = cleanDescription,
                            Url = fullUrl,
                            Source = ScraperName,
                            ExtractedSkills = "",
                            JobType = "Freelance İlanı",
                            DateScraped = DateTime.Now,
                            DatePosted = DateTime.Now
                        });
                        addedCount++;
                    }

                    db.SaveChanges();
                    Console.WriteLine($"\n✅ İŞLEM TAMAM! {addedCount} adet proje tam açıklamalarıyla eklendi.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Hata: {ex.Message}");
            }
        }

        private class FreelancerApiResponse { [JsonPropertyName("result")] public FreelancerResult Result { get; set; } }
        private class FreelancerResult { [JsonPropertyName("projects")] public List<FreelancerProject> Projects { get; set; } }
        private class FreelancerProject
        {
            [JsonPropertyName("title")] public string Title { get; set; }
            [JsonPropertyName("seo_url")] public string SeoUrl { get; set; }
            [JsonPropertyName("description")] public string Description { get; set; }
            [JsonPropertyName("preview_description")] public string PreviewDescription { get; set; }
        }
    }
}