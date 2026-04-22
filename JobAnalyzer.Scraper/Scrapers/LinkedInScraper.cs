using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class LinkedInScraper : ScraperBase
    {
        public override string ScraperName => "LinkedIn";

        // Türkiye LinkedIn geoId (location string'den çok daha güvenilir)
        private const string TurkeyGeoId = "102105699";
        private const int PageSize = 25;
private const int DetailParallelism = 4; // Aynı anda kaç detay isteği

        // Her kategori farklı ilan seti getirir → 1000 limitini aşmanın yolu
        private static readonly string[] SearchTerms =
        [
            // Genel
            "yazılım geliştirici", "yazılım mühendisi",
            "software developer", "software engineer",
            // Frontend
            "frontend developer", "react developer", "angular developer",
            "vue.js developer", "next.js developer", "typescript developer",
            "ui developer", "ux developer",
            // Backend
            "backend developer", "java developer", "python developer",
            "node.js developer", ".net developer", "c# developer",
            "golang developer", "php developer", "spring boot developer",
            "django developer", "laravel developer", "kotlin developer",
            // Fullstack
            "fullstack developer", "full stack developer",
            // Mobile
            "mobile developer", "android developer", "ios developer",
            "swift developer", "flutter developer", "react native developer",
            // DevOps & Cloud
            "devops engineer", "cloud engineer", "platform engineer",
            "site reliability engineer", "kubernetes engineer",
            "aws engineer", "azure engineer", "gcp engineer",
            "infrastructure engineer",
            // Data & AI
            "data scientist", "data engineer", "machine learning engineer",
            "ai engineer", "mlops engineer", "data analyst",
            "bi developer", "etl developer", "spark developer",
            // QA & Test
            "qa engineer", "test engineer", "test automation engineer",
            "sdet", "quality assurance",
            // Security
            "cybersecurity engineer", "information security", "penetration tester",
            // Diğer yazılım rolleri
            "game developer", "unity developer", "unreal engine developer",
            "embedded software engineer", "firmware engineer",
            "blockchain developer", "web3 developer",
            "software architect", "solution architect", "tech lead",
            "scrum master", "product owner",
        ];

        private static readonly string[] UserAgents =
        [
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        ];

        // ─── Ana giriş ───────────────────────────────────────────────────────────

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n[{ScraperName}] Başlatılıyor — Türkiye yazılım ilanları");

            using var http = BuildHttpClient();
            var dbOpts = BuildDbOptions();

            // FAZA 1: Guest API ile hızlı ilan listesi (browser yok)
            var collectedJobIds = await Phase1_CollectListings(http, dbOpts);
            Console.WriteLine($"\n[{ScraperName}] Faz 1 bitti. {collectedJobIds.Count} ilan için detay çekilecek.");

            // FAZA 2: Paralel detay çekimi
            await Phase2_FetchDetails(collectedJobIds);

            Console.WriteLine($"\n[{ScraperName}] Tüm işlemler tamamlandı.");
        }

        // ─── FAZ 1: HttpClient ile guest API ─────────────────────────────────────
        // LinkedIn'in seeMoreJobPostings endpoint'i login gerektirmez.
        // Browser yerine HttpClient → 5-10x hızlı, aynı veri.

        private async Task<List<string>> Phase1_CollectListings(
            HttpClient http, DbContextOptionsBuilder<AppDbContext> dbOpts)
        {
            var collectedIds = new List<string>();
            int totalSaved = 0;

            foreach (var term in SearchTerms)
            {
                Console.Write($"\n  \"{term}\" → ");
                string encodedTerm = Uri.EscapeDataString(term);
                int consecutiveEmpty = 0;
                int termSaved = 0;

                for (int start = 0; start < 1000; start += PageSize)
                {
                    string url = $"https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search" +
                                 $"?keywords={encodedTerm}&geoId={TurkeyGeoId}" +
                                 $"&start={start}&count={PageSize}&sortBy=DD";
                    try
                    {
                        using var resp = await http.GetAsync(url);
                        if (!resp.IsSuccessStatusCode)
                        {
                            Console.Write($"[HTTP {(int)resp.StatusCode}] ");
                            break;
                        }

                        string html = await resp.Content.ReadAsStringAsync();
                        var (saved, pageIds, cardCount) = await ParseAndSaveCards(html, dbOpts);
                        collectedIds.AddRange(pageIds);
                        totalSaved += saved;
                        termSaved += saved;

                        Console.Write($"+{saved}({cardCount}) ");

                        if (cardCount == 0) break;

                        if (pageIds.Count == 0)
                        {
                            consecutiveEmpty++;
                            if (consecutiveEmpty >= 3) break;
                        }
                        else consecutiveEmpty = 0;

                        await RandomDelay(600, 1200);
                    }
                    catch (Exception ex)
                    {
                        Console.Write($"[ERR:{ex.Message[..Math.Min(30, ex.Message.Length)]}] ");
                        await RandomDelay(2000, 4000);
                        break;
                    }
                }

                Console.WriteLine($"= {termSaved} yeni | Toplam: {totalSaved}");
            }

            return collectedIds.Distinct().ToList();
        }

        private async Task<(int saved, List<string> ids, int cardCount)> ParseAndSaveCards(
            string html, DbContextOptionsBuilder<AppDbContext> dbOpts)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // LinkedIn birden fazla kart HTML yapısı kullanıyor
            var cards = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'base-card')] | " +
                "//li[contains(@class,'jobs-search__results-list')]" +
                "//div[contains(@class,'base-search-card')]");

            if (cards == null || cards.Count == 0) return (0, [], 0);

            int saved = 0;
            var ids = new List<string>();

            using var db = new AppDbContext(dbOpts.Options);

            foreach (var card in cards)
            {
                var titleNode   = card.SelectSingleNode(".//*[contains(@class,'base-search-card__title')]");
                var companyNode = card.SelectSingleNode(".//*[contains(@class,'base-search-card__subtitle')]");
                var locNode     = card.SelectSingleNode(".//*[contains(@class,'job-search-card__location')]");
                var linkNode    = card.SelectSingleNode(".//a[contains(@class,'base-card__full-link')]")
                               ?? card.SelectSingleNode(".//a[@href]");
                var dateNode    = card.SelectSingleNode(".//*[@datetime]");
                var workNode    = card.SelectSingleNode(".//*[contains(@class,'job-search-card__workplace-type')]");

                if (titleNode == null || linkNode == null) continue;

                string rawUrl = linkNode.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(rawUrl))
                {
                    string urn = card.GetAttributeValue("data-entity-urn", "");
                    var m = Regex.Match(urn, @"jobPosting:(\d+)");
                    if (m.Success) rawUrl = $"https://www.linkedin.com/jobs/view/{m.Groups[1].Value}";
                }

                string? jobId = ParseJobId(rawUrl);
                if (string.IsNullOrEmpty(jobId)) continue;

                // Canonical URL: her zaman www.linkedin.com, query string yok
                string jobUrl = $"https://www.linkedin.com/jobs/view/{jobId}";

                // Dedup: job ID'ye göre (tr. vs www. farkını ortadan kaldırır)
                if (db.JobPostings.Any(j => j.Url != null && j.Url.Contains(jobId))) continue;

                string title   = Clean(titleNode.InnerText);
                string company = companyNode != null ? Clean(companyNode.InnerText) : "Bilinmiyor";
                string loc     = locNode     != null ? Clean(locNode.InnerText)     : "Türkiye";
                string jobType = workNode    != null ? Clean(workNode.InnerText)     : "Şirket İçi";

                DateTime? datePosted = null;
                if (dateNode != null)
                {
                    string dt = dateNode.GetAttributeValue("datetime", "");
                    if (DateTime.TryParse(dt, out var parsed)) datePosted = parsed.ToUniversalTime();
                }

                db.JobPostings.Add(new JobPosting
                {
                    Title           = Cap(title,   200),
                    CompanyName     = Cap(company, 200),
                    Location        = Cap(loc,     200),
                    Description     = "",            // Faz 2'de dolacak
                    Url             = jobUrl,
                    Source          = ScraperName,
                    ExtractedSkills = "",
                    DateScraped     = DateTime.UtcNow,
                    DatePosted      = datePosted ?? DateTime.UtcNow,
                    JobType         = Cap(jobType, 50),
                });

                ids.Add(jobId);
                saved++;
            }

            if (saved > 0) await db.SaveChangesAsync();
            return (saved, ids, cards.Count);
        }

        // ─── FAZ 2: Detay çekimi ─────────────────────────────────────────────────

        private async Task Phase2_FetchDetails(List<string> collectedIds)
        {
            var dbOpts = BuildDbOptions();

            // Faz 1'den gelen + DB'de description'sız kalan tüm ilanlar
            List<string> allIds = collectedIds.ToList();
            if (allIds.Count == 0)
            {
                using var db = new AppDbContext(dbOpts.Options);
                var urls = await db.JobPostings
                    .Where(j => j.Source == ScraperName &&
                                (j.Description == null || j.Description == ""))
                    .Select(j => j.Url)
                    .ToListAsync();

                allIds = urls
                    .Where(u => u != null)
                    .Select(u => ParseJobId(u!) ?? "")
                    .Where(id => id.Length > 0)
                    .Distinct()
                    .ToList();
            }

            if (allIds.Count == 0)
            {
                Console.WriteLine("  Detay çekilecek ilan bulunamadı.");
                return;
            }

            Console.WriteLine($"  {allIds.Count} ilan için detay çekimi başlıyor (paralel: {DetailParallelism})...");

            using var http = BuildHttpClient();
            var semaphore = new SemaphoreSlim(DetailParallelism);
            int done = 0;

            var tasks = allIds.Select(async jobId =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var detail = await FetchDetailAsync(http, jobId);
                    if (string.IsNullOrWhiteSpace(detail.Description)) return;

                    using var db = new AppDbContext(dbOpts.Options);
                    var posting = await db.JobPostings
                        .FirstOrDefaultAsync(j => j.Url != null && j.Url.Contains(jobId));

                    if (posting == null) return;

                    posting.Description = detail.Description;
                    if (!string.IsNullOrWhiteSpace(detail.Seniority))      posting.Level   = Cap(detail.Seniority, 50);
                    if (!string.IsNullOrWhiteSpace(detail.WorkArrangement)) posting.JobType = Cap(detail.WorkArrangement, 50);

                    await db.SaveChangesAsync();

                    int current = Interlocked.Increment(ref done);
                    if (current % 25 == 0)
                        Console.WriteLine($"  Detay: {current}/{allIds.Count} tamamlandı");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Detay hata ({jobId}): {ex.Message}");
                }
                finally
                {
                    await RandomDelay(1500, 3000);
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            Console.WriteLine($"  Faz 2 tamamlandı: {done} ilanın detayı çekildi.");
        }

        private record DetailResult(string Description, string Seniority, string WorkArrangement);

        private async Task<DetailResult> FetchDetailAsync(HttpClient http, string jobId)
        {
            string url = $"https://www.linkedin.com/jobs-guest/jobs/api/jobPosting/{jobId}";

            string html = "";
            for (int attempt = 0; attempt < 4; attempt++)
            {
                using var response = await http.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    int backoff = (int)Math.Pow(2, attempt + 1) * 10000; // 20s, 40s, 80s
                    Console.WriteLine($"  ⏳ LinkedIn 429 (jobId={jobId}), {backoff / 1000}s bekleniyor...");
                    await Task.Delay(backoff);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"  ⚠️ LinkedIn detay HTTP {(int)response.StatusCode}: jobId={jobId}");
                    return new DetailResult("", "", "");
                }

                html = await response.Content.ReadAsStringAsync();
                break;
            }

            if (string.IsNullOrEmpty(html)) return new DetailResult("", "", "");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Birden fazla selector dene — LinkedIn zaman içinde class adlarını değiştirebiliyor
            var descNode = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'show-more-less-html__markup')] | " +
                "//div[contains(@class,'description__text')] | " +
                "//div[contains(@class,'job-description')] | " +
                "//section[contains(@class,'description')] | " +
                "//div[@class='description__text description__text--rich']");
            string description = descNode != null ? Clean(descNode.InnerText) : "";

            // Kriter satırları: Kıdem seviyesi, İstihdam türü, Çalışma düzeni
            string seniority = "", workArrangement = "";

            var criteria = doc.DocumentNode.SelectNodes(
                "//li[contains(@class,'description__job-criteria-item')]");

            if (criteria != null)
            {
                foreach (var item in criteria)
                {
                    string header = Clean(item.SelectSingleNode(".//h3")?.InnerText ?? "").ToLowerInvariant();
                    string value  = Clean(item.SelectSingleNode(
                        ".//span[contains(@class,'description__job-criteria-text')]")?.InnerText ?? "");

                    if (header.Contains("seniority") || header.Contains("kıdem"))
                        seniority = value;
                    else if (header.Contains("workplace") || header.Contains("work mode") || header.Contains("remote"))
                        workArrangement = value;
                }
            }

            // Alternatif work arrangement konumu
            if (string.IsNullOrEmpty(workArrangement))
            {
                var badge = doc.DocumentNode.SelectSingleNode(
                    "//*[contains(@class,'workplace-type')] | " +
                    "//*[contains(@class,'remote-type')]");
                if (badge != null) workArrangement = Clean(badge.InnerText);
            }

            return new DetailResult(description, seniority, workArrangement);
        }

        // ─── Yardımcı metotlar ────────────────────────────────────────────────────

        private DbContextOptionsBuilder<AppDbContext> BuildDbOptions()
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>();
            opts.UseNpgsql(ConnectionString);
            return opts;
        }

        private static HttpClient BuildHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression =
                    System.Net.DecompressionMethods.GZip |
                    System.Net.DecompressionMethods.Deflate |
                    System.Net.DecompressionMethods.Brotli,
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PickRandom(UserAgents));
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language",
                "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding",
                "gzip, deflate, br");
            return client;
        }


        // /jobs/view/yazilim-muhendisi-at-sirket-3987654321 → "3987654321"
        private static string? ParseJobId(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = Regex.Match(url, @"/(?:view|posting)/(?:[^/]+-)?(\d{8,})");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(url, @"(\d{10,})");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string StripQuery(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int q = url.IndexOf('?');
            return q > 0 ? url[..q] : url;
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = HtmlEntity.DeEntitize(text);
            return Regex.Replace(text.Trim(), @"\s+", " ");
        }

        private static string Cap(string text, int max) =>
            text.Length > max ? text[..max] : text;

        private static string PickRandom(string[] arr) =>
            arr[Random.Shared.Next(arr.Length)];

        private static Task RandomDelay(int minMs, int maxMs) =>
            Task.Delay(Random.Shared.Next(minMs, maxMs));
    }
}
