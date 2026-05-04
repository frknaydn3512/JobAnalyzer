using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Hacker News — "Ask HN: Who is Hiring?" aylık thread'i.
    /// Algolia HN API kullanır. Her ayın 1'inde yayınlanan thread'den yazılım ilanlarını çeker.
    /// https://hn.algolia.com/api/v1/search?query=ask+hn+who+is+hiring&tags=ask_hn
    /// </summary>
    public class HackerNewsScraper : ScraperBase
    {
        public override string ScraperName => "HackerNews Who's Hiring";

        private static readonly string[] _softwareKeywords = {
            "software", "engineer", "developer", "backend", "frontend", "fullstack",
            "devops", "cloud", "data", "python", "java", "react", "node", ".net",
            "typescript", "golang", "rust", "kotlin", "swift", "flutter", "android",
            "ios", "ml", "ai", "infrastructure", "platform", "sre", "security",
            "architect", "lead", "senior", "principal", "cto", "tech"
        };

        private static bool IsSoftwareRelated(string text) =>
            _softwareKeywords.Any(kw => text.ToLowerInvariant().Contains(kw));

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Algolia API'den son HN hiring thread'i aranıyor...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobAnalyzerBot/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);
            var existingUrls = LoadExistingUrls(db);

            // 1. En son "Ask HN: Who is hiring?" thread'ini bul
            int threadId = await GetLatestHiringThreadIdAsync(client, jsonOptions);
            if (threadId == 0)
            {
                Console.WriteLine("  ❌ Hiring thread bulunamadı.");
                return;
            }
            Console.WriteLine($"  ✅ Thread ID: {threadId}");

            // 2. Thread'in tüm comment ID'lerini çek
            var commentIds = await GetCommentIdsAsync(client, jsonOptions, threadId);
            if (commentIds.Count == 0)
            {
                Console.WriteLine("  ❌ Comment bulunamadı.");
                return;
            }
            Console.WriteLine($"  📋 {commentIds.Count} comment işlenecek...");

            int added = 0;
            int processed = 0;

            // 3. Her comment'i ayrı ayrı çek ve işle (rate limit için batch)
            foreach (var commentId in commentIds.Take(500)) // Max 500 comment
            {
                try
                {
                    string commentUrl = $"https://hacker-news.firebaseio.com/v0/item/{commentId}.json";
                    var resp = await client.GetAsync(commentUrl);
                    if (!resp.IsSuccessStatusCode) continue;

                    string json = await resp.Content.ReadAsStringAsync();
                    var comment = JsonSerializer.Deserialize<HnItem>(json, jsonOptions);

                    if (comment == null || string.IsNullOrWhiteSpace(comment.Text) || comment.Deleted == true) continue;

                    // HTML'i temizle
                    string cleanText = Regex.Replace(comment.Text, "<.*?>", " ");
                    cleanText = Regex.Replace(cleanText, @"\s+", " ").Trim();

                    if (!IsSoftwareRelated(cleanText)) continue;

                    // İlk satırı başlık olarak kullan
                    string title = cleanText.Split('\n', '.', '|')[0].Trim();
                    if (title.Length < 5) continue;
                    if (title.Length > 100) title = title.Substring(0, 100);

                    // URL: HN item linki
                    string postUrl = $"https://news.ycombinator.com/item?id={commentId}";
                    if (!existingUrls.Add(postUrl)) continue;

                    // Şirket adını almak için ilk satırı parse et ("|" veya "–" separator)
                    string company = "HN Community";
                    var parts = cleanText.Split(new[] { " | ", " - ", " – " }, 2, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        company = parts[0].Trim();
                        if (company.Length > 100) company = company.Substring(0, 100);
                    }

                    db.JobPostings.Add(new JobPosting
                    {
                        Title       = title,
                        CompanyName = company,
                        Location    = "Remote / Global (HN)",
                        Description = cleanText.Length > 4000 ? cleanText.Substring(0, 4000) : cleanText,
                        Url         = postUrl,
                        Source      = ScraperName,
                        ExtractedSkills = "",
                        DateScraped = DateTime.UtcNow,
                        DatePosted  = comment.Time.HasValue
                            ? DateTimeOffset.FromUnixTimeSeconds(comment.Time.Value).UtcDateTime
                            : DateTime.UtcNow
                    });
                    added++;
                    processed++;

                    if (processed % 10 == 0)
                    {
                        db.SaveChanges();
                        Console.Write($"\r  ⏳ {processed}/500 comment işlendi, {added} ilan eklendi...");
                    }
                }
                catch { /* tek comment hatası durdurmasın */ }
            }

            db.SaveChanges();
            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! {added} YENİ ilan eklendi.");
        }

        private async Task<int> GetLatestHiringThreadIdAsync(HttpClient client, JsonSerializerOptions opts)
        {
            try
            {
                // search_by_date: en yeni thread'i bul (search relevance yerine tarih sırası)
                string url = "https://hn.algolia.com/api/v1/search_by_date?query=Ask+HN+Who+is+hiring&tags=ask_hn&hitsPerPage=5";
                var resp = await client.GetAsync(url);
                resp.EnsureSuccessStatusCode();

                string json = await resp.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AlgoliaResult>(json, opts);

                var hit = result?.Hits?.FirstOrDefault(h =>
                    h.Title != null &&
                    h.Title.Contains("Who is hiring", StringComparison.OrdinalIgnoreCase));

                return hit?.ObjectID != null && int.TryParse(hit.ObjectID, out int id) ? id : 0;
            }
            catch { return 0; }
        }

        private async Task<List<int>> GetCommentIdsAsync(HttpClient client, JsonSerializerOptions opts, int threadId)
        {
            try
            {
                string url = $"https://hacker-news.firebaseio.com/v0/item/{threadId}.json";
                var resp = await client.GetAsync(url);
                resp.EnsureSuccessStatusCode();

                string json = await resp.Content.ReadAsStringAsync();
                var item = JsonSerializer.Deserialize<HnItem>(json, opts);
                return item?.Kids ?? new List<int>();
            }
            catch { return new List<int>(); }
        }

        // Algolia search result
        private class AlgoliaResult
        {
            [JsonPropertyName("hits")] public List<AlgoliaHit>? Hits { get; set; }
        }
        private class AlgoliaHit
        {
            [JsonPropertyName("objectID")] public string? ObjectID { get; set; }
            [JsonPropertyName("title")]    public string? Title    { get; set; }
        }

        // HN Firebase item
        private class HnItem
        {
            [JsonPropertyName("id")]      public int?       Id      { get; set; }
            [JsonPropertyName("text")]    public string?    Text    { get; set; }
            [JsonPropertyName("kids")]    public List<int>? Kids    { get; set; }
            [JsonPropertyName("time")]    public long?      Time    { get; set; }
            [JsonPropertyName("deleted")] public bool?      Deleted { get; set; }
            [JsonPropertyName("dead")]    public bool?      Dead    { get; set; }
        }
    }
}
