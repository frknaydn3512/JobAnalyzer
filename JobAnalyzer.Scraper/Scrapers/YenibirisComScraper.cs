using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class YenibirisComScraper : IJobScraper
    {
        public string ScraperName => "Yenibiris.com";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        // Yazılım kategorisi path'leri — yazılımla ilgili ilanlar doğrudan burada
        private readonly string[] _categoryPaths =
        {
            "yazilim",          // Yazılım / Genel
            "web-tasarim",      // Web & Frontend
            "bilgi-teknolojileri",// IT / Sistem
        };

        // Ek arama terimleri
        private readonly string[] _searchTerms = { "software developer", "backend developer", "frontend developer" };

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor...");

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            var launchOptions = new LaunchOptions
            {
                Headless = false,
                DefaultViewport = null,
                Args = new[] { "--disable-blink-features=AutomationControlled", "--window-size=1920,1080" }
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            await page.SetExtraHttpHeadersAsync(new Dictionary<string, string> { { "Accept-Language", "tr-TR,tr;q=0.9" } });

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            int totalAdded = 0;

            // Kategori bazlı tarama (en verimli yöntem)
            foreach (var category in _categoryPaths)
            {
                Console.WriteLine($"\n📂 '{category}' kategorisi taranıyor...");

                for (int pageNum = 1; pageNum <= 5; pageNum++)
                {
                    // Gerçek Yenibiris kategori URL formatı
                    string targetUrl = $"https://www.yenibiris.com/is-ilanlari/{category}?page={pageNum}";
                    int added = await ScrapePage(page, db, targetUrl, pageNum);
                    totalAdded += added;

                    if (added == 0) break; // Son sayfa
                    await Task.Delay(1500);
                }
            }

            // Arama terimi bazlı tarama
            foreach (var term in _searchTerms)
            {
                Console.WriteLine($"\n🔍 '{term}' aranıyor...");
                string targetUrl = $"https://www.yenibiris.com/is-ilanlari?query={Uri.EscapeDataString(term)}";
                int added = await ScrapePage(page, db, targetUrl, 1);
                totalAdded += added;
                await Task.Delay(1500);
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private async Task<int> ScrapePage(IPage page, AppDbContext db, string url, int pageNum)
        {
            try
            {
                await page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                    Timeout = 20000
                });
                await Task.Delay(2000);

                string htmlContent = await page.GetContentAsync();
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(htmlContent);

                // Yenibiris gerçek selector'ları (browser ile doğrulandı):
                // - Başlık linki: a.gtmTitle
                // - Detay butonu: a.btnNoQuickApply
                var jobLinks = document.DocumentNode.SelectNodes(
                    "//a[contains(@class,'gtmTitle')] | //a[contains(@class,'btnNoQuickApply')]"
                );

                if (jobLinks == null || jobLinks.Count == 0)
                {
                    Console.WriteLine($"   📭 Sayfa {pageNum}: İlan bulunamadı.");
                    return 0;
                }

                int added = 0;
                HashSet<string> seen = new();

                foreach (var node in jobLinks)
                {
                    string href = node.GetAttributeValue("href", "");
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    string jobUrl = href.StartsWith("http") ? href : "https://www.yenibiris.com" + href;

                    // Sadece iş ilanı URL'lerini al
                    if (!jobUrl.Contains("/is-ilani/")) continue;
                    if (seen.Contains(jobUrl)) continue;
                    seen.Add(jobUrl);

                    // Başlık: önce title attribute, sonra node text
                    string title = node.GetAttributeValue("title", "").Trim();
                    if (string.IsNullOrWhiteSpace(title))
                        title = node.InnerText.Trim();
                    title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();

                    if (string.IsNullOrWhiteSpace(title) || title.Length < 3 || title == "DETAY") continue;

                    // Şirket bilgisi — parent card içinden al
                    var card = node.ParentNode?.ParentNode?.ParentNode;
                    string company = "Daha Sonra Çekilecek";
                    string location = "Türkiye";

                    if (card != null)
                    {
                        var companyNode = card.SelectSingleNode(".//a[contains(@class,'gtmCompanyName')] | .//span[contains(@class,'company')]");
                        var cityNode = card.SelectSingleNode(".//span[contains(@class,'city')] | .//span[contains(@class,'location')]");
                        if (companyNode != null) company = companyNode.InnerText.Trim();
                        if (cityNode != null) location = cityNode.InnerText.Trim();
                    }

                    if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                    db.JobPostings.Add(new JobPosting
                    {
                        Title = title.Length > 100 ? title.Substring(0, 100) : title,
                        CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                        Location = location.Length > 100 ? location.Substring(0, 100) : location,
                        Description = $"Detaylı açıklama: {jobUrl}",
                        Url = jobUrl,
                        Source = ScraperName,
                        ExtractedSkills = "",
                        DateScraped = DateTime.Now,
                        DatePosted = DateTime.Now
                    });

                    added++;
                    totalAdded_global++;
                    Console.WriteLine($"   💾 {company} | {title}");
                }

                db.SaveChanges();
                Console.WriteLine($"   Sayfa {pageNum}: {added} yeni ilan eklendi.");
                return added;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Hata (Sayfa {pageNum}): {ex.Message}");
                return 0;
            }
        }

        private int totalAdded_global = 0;
    }
}
