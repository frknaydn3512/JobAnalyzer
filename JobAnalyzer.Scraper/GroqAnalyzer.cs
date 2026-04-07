using System.Text.Json;
using System.Text;
using JobAnalyzer.Data;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Scraper
{
    public class GroqAnalyzer
    {
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";
        private readonly string _apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";

        public async Task RunAsync()
        {
            Console.WriteLine("\n🚀 Groq AI Analiz Motoru Başlatılıyor...");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);

            using var db = new AppDbContext(optionsBuilder.Options);

            // Analiz edilmemiş ilanları çek
            var jobs = await db.JobPostings
                .Where(j => !string.IsNullOrEmpty(j.Description) && (j.ExtractedSkills == "" || j.ExtractedSkills == null))
                .ToListAsync();

            if (jobs.Count == 0)
            {
                Console.WriteLine("✅ Tüm ilanlar zaten analiz edilmiş. Yapıcak bir şey yok!");
                return;
            }

            Console.WriteLine($"🚀 Toplam {jobs.Count} ilan analiz kuyruğuna alındı. Başlıyoruz...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            client.Timeout = TimeSpan.FromSeconds(60);

            int successCount = 0;
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            foreach (var job in jobs)
            {
                // Temizlik - AI Promt Injection'a karşı tek tırnak yapıyoruz ve uzunluğu sınırlıyoruz
                string safeDescription = job.Description!.Length > 4000
                    ? job.Description.Substring(0, 4000).Replace("\"", "'")
                    : job.Description.Replace("\"", "'");

                var requestBody = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = new[] {
                        new { role = "user", content = $"Extract technical skills (programming languages, frameworks, architectures) as a comma-separated list. No prose: {safeDescription}" }
                    }
                };

                string json = JsonSerializer.Serialize(requestBody, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var resJson = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(resJson);
                        var root = doc.RootElement;
                        string skills = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

                        job.ExtractedSkills = skills.Trim();
                        await db.SaveChangesAsync();
                        successCount++;
                        
                        string shortTitle = job.Title!.Length > 40 ? job.Title.Substring(0, 40) + "..." : job.Title;
                        Console.WriteLine($"  ✅ [{successCount}/{jobs.Count}] Analiz Edildi: {shortTitle}");
                    }
                    else if ((int)response.StatusCode == 429) // Too Many Requests
                    {
                        Console.WriteLine("  ⏳ Kota doldu, 15 saniye mola veriliyor...");
                        await Task.Delay(15000); 
                        continue;
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠️ API Hatası: {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ İstek Hatası: {ex.Message}");
                }

                // Her istekten sonra bekle (Groq rate limitleri için)
                await Task.Delay(2000);
            }

            Console.WriteLine($"\n🏁 MUAZZAM! Toplam {successCount} ilan başarıyla analiz edildi.");
        }
    }
}
