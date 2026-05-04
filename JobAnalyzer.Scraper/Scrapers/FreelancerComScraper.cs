using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class FreelancerComScraper : ScraperBase
    {
        public override string ScraperName => "Freelancer.com (Global API)";

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor... (Full Metin Modu)");

            // Birden fazla keyword ile çağır, daha fazla proje çek
            string apiUrl = "https://www.freelancer.com/api/projects/0.1/projects/active?limit=100&job_details=true&full_description=true";

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
                    optionsBuilder.UseNpgsql(ConnectionString);
                    using var db = new AppDbContext(optionsBuilder.Options);
                    var existingUrls = LoadExistingUrls(db);

                    int addedCount = 0;

                    foreach (var project in freelancerData.Result.Projects)
                    {
                        if (string.IsNullOrWhiteSpace(project.Title)) continue;

                        string fullUrl = $"https://www.freelancer.com/projects/{project.SeoUrl}";
                        if (!existingUrls.Add(fullUrl)) continue;

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
                            DateScraped = DateTime.UtcNow,
                            DatePosted = DateTime.UtcNow
                        });
                        addedCount++;
                    }

                    try
                    {
                        db.SaveChanges();
                        Console.WriteLine($"\n✅ İŞLEM TAMAM! {addedCount} adet proje tam açıklamalarıyla eklendi.");
                    }
                    catch (Exception dbEx)
                    {
                        Console.WriteLine($"\n⚠️ Veritabanı kayıt hatası: {dbEx.InnerException?.Message ?? dbEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Hata: {ex.Message}");
            }
        }

        private class FreelancerApiResponse { [JsonPropertyName("result")] public FreelancerResult? Result { get; set; } }
        private class FreelancerResult { [JsonPropertyName("projects")] public List<FreelancerProject>? Projects { get; set; } }
        private class FreelancerProject
        {
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("seo_url")] public string? SeoUrl { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("preview_description")] public string? PreviewDescription { get; set; }
        }
    }
}
