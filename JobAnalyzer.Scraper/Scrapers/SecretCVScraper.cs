using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// SecretCV - JavaScript lazy-load sayfası
    /// Doğrulanan selector: a.title.lh-title (browser ile doğrulandı)
    /// </summary>
    public class SecretCVScraper : IJobScraper
    {
        public string ScraperName => "SecretCV";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        private static readonly string[] _softwareKeywords = {
            "software","developer","geliştirici","yazılım","backend","frontend",
            "fullstack","web","mobile","android","ios","devops","cloud","data",
            "python","java","react","angular","node",".net","php","engineer",
            "tech","qa","test","typescript","kotlin","swift","flutter","c#","api"
        };
        private static bool IsSoftwareRelated(string title) =>
            _softwareKeywords.Any(kw => title.ToLowerInvariant().Contains(kw));

        private readonly string[] _searchTerms = {
            "yazilim-gelistirici",
            "software-developer",
            "backend-developer",
            "frontend-developer",
        };

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Başlatılıyor...");

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = false,
                DefaultViewport = null,
                Args = new[] { "--disable-blink-features=AutomationControlled" }
            });
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);
            int totalAdded = 0;

            foreach (var term in _searchTerms)
            {
                // SecretCV kategori URL formatı — keyword bazlı arama
                string targetUrl = $"https://www.secretcv.com/is-ilanlari?keyword={term}";
                Console.WriteLine($"\n🔍 '{term}' aranıyor → {targetUrl}");

                try
                {
                    await page.GoToAsync(targetUrl, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                        Timeout = 30000
                    });
                    await Task.Delay(4000); // JS yüklenmesi için bekle

                    // Tamamen aşağı kaydır — lazy-load ilanları yüklemek için
                    await page.EvaluateFunctionAsync(@"async () => {
                        for (let i = 0; i < 6; i++) {
                            window.scrollBy(0, window.innerHeight);
                            await new Promise(r => setTimeout(r, 800));
                        }
                        window.scrollTo(0, document.body.scrollHeight);
                        await new Promise(r => setTimeout(r, 1500));
                    }");

                    string html = await page.GetContentAsync();
                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // Browser ile doğrulanan selector: a.title.lh-title
                    var jobLinks = doc.DocumentNode.SelectNodes(
                        "//a[contains(@class,'lh-title')] | " +
                        "//a[contains(@class,'cv-all-jobs') and @href] | " +
                        "//a[contains(@href,'is-ilanlari-')]"
                    );

                    if (jobLinks == null || jobLinks.Count == 0)
                    {
                        Console.WriteLine("  ⚠️ İlan bulunamadı, selector tutmadı.");
                        continue;
                    }

                    Console.WriteLine($"  🎉 {jobLinks.Count} ilan linki bulundu!");
                    int termAdded = 0;
                    HashSet<string> seen = new();

                    foreach (var link in jobLinks)
                    {
                        string href = link.GetAttributeValue("href", "");
                        if (string.IsNullOrWhiteSpace(href)) continue;

                        string jobUrl = href.StartsWith("http") ? href : "https://www.secretcv.com" + href;
                        if (!jobUrl.Contains("is-ilanlari-") && !jobUrl.Contains("pozisyon")) continue;
                        if (seen.Contains(jobUrl)) continue;
                        seen.Add(jobUrl);

                        string title = link.InnerText.Trim();
                        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
                        if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                        if (!IsSoftwareRelated(title)) continue;
                        if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                        // Şirket adı — parent card'dan al
                        var card = link.ParentNode?.ParentNode;
                        string company = "Daha Sonra Çekilecek";
                        if (card != null)
                        {
                            var compNode = card.SelectSingleNode(".//a[contains(@class,'company')] | .//span[contains(@class,'company')]");
                            if (compNode != null) company = compNode.InnerText.Trim();
                        }

                        db.JobPostings.Add(new JobPosting
                        {
                            Title = title.Length > 100 ? title.Substring(0, 100) : title,
                            CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                            Location = "Türkiye",
                            Description = $"SecretCV ilanı: {jobUrl}",
                            Url = jobUrl,
                            Source = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.Now,
                            DatePosted = DateTime.Now
                        });
                        termAdded++;
                        totalAdded++;
                        Console.WriteLine($"  💾 {title.Substring(0, Math.Min(title.Length, 50))}");
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ '{term}': {termAdded} yeni ilan eklendi.");
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Hata ({term}): {ex.Message}");
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! {totalAdded} YENİ ilan.");
        }
    }
}