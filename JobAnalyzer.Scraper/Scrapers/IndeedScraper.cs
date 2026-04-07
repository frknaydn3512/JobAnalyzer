using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Indeed Turkey Scraper
    /// Indeed dünyanın en büyük iş ilanı sitesi.
    /// Cloudflare koruması var ama Puppeteer ile aşılabiliyor.
    /// </summary>
    public class IndeedScraper : IJobScraper
    {
        public string ScraperName => "Indeed Turkey";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        private static readonly string[] _softwareKeywords = {
            "software", "developer", "geliştirici", "yazılım", "backend", "frontend",
            "fullstack", "full stack", "web", "mobile", "mobil", "android", "ios",
            "devops", "cloud", "data", "python", "java", "react", "angular", "node",
            ".net", "php", "engineer", "mühendis", "tech", "qa", "test",
            "typescript", "kotlin", "swift", "flutter", "c#", "golang", "api"
        };

        private static bool IsSoftwareRelated(string title) =>
            _softwareKeywords.Any(kw => title.ToLowerInvariant().Contains(kw));

        // Arama terimleri — Türkçe ve İngilizce
        private readonly string[] _searchTerms =
        {
            "yazılım geliştirici",
            "software developer",
            "backend developer",
            "frontend developer",
        };

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor...");

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            var launchOptions = new LaunchOptions
            {
                Headless = false,
                DefaultViewport = null,
                Args = new[] {
                    "--disable-blink-features=AutomationControlled",
                    "--window-size=1366,768",
                    "--no-sandbox"
                }
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();

            // Indeed'in bot dedektörünü atlatmak için gerçek bir kullanıcı gibi davran
            await page.SetUserAgentAsync(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
            );
            await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                { "Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8" },
                { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8" }
            });

            // Navigator.webdriver özelliğini gizle (Bot tespitini aşmak için)
            await page.EvaluateFunctionAsync(@"() => {
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            }");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            int totalAdded = 0;

            foreach (var term in _searchTerms)
            {
                Console.WriteLine($"\n🔍 Indeed'de '{term}' aranıyor...");

                // Indeed Turkey — sayfa başına 15 ilan, start=0, 15, 30...
                // Limit 30'dan (2 sayfa) 300'e (20 sayfa) çıkartıldı.
                for (int startIdx = 0; startIdx <= 300; startIdx += 15)
                {
                    string encodedTerm = Uri.EscapeDataString(term);
                    // Indeed Turkey URL formatı
                    string targetUrl = $"https://tr.indeed.com/jobs?q={encodedTerm}&l=Türkiye&start={startIdx}";

                    try
                    {
                        Console.WriteLine($"  📄 Sayfa {startIdx / 15 + 1} yükleniyor...");
                        await page.GoToAsync(targetUrl, new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                            Timeout = 60000 // Cloudflare vs çözesin diye 60 sn
                        });

                        await Task.Delay(3000);

                        // Çerez/popup kapat
                        await page.EvaluateFunctionAsync(@"async () => {
                            // Close cookie banner
                            let cookieBtn = document.querySelector('[id*=""cookie""] button, [class*=""cookie""] button');
                            if (cookieBtn) cookieBtn.click();
                            // Close any modal
                            let closeBtn = document.querySelector('.icl-CloseButton, [aria-label=""Kapat""]');
                            if (closeBtn) closeBtn.click();
                        }");

                        await Task.Delay(1000);

                        // Bot engeli var mı? Kullanıcının çözmesi için bekleniyor.
                        string htmlContent = await page.GetContentAsync();
                        if (htmlContent.Contains("captcha") || htmlContent.Contains("robot") || htmlContent.Contains("unusual traffic") || htmlContent.Contains("cloudflare"))
                        {
                            Console.WriteLine("  ⚠️ Güvenlik (Cloudflare) geldi! Lütfen açılan tarayıcıdan çözün. 20 saniye bekliyorum...");
                            await Task.Delay(20000);
                            htmlContent = await page.GetContentAsync(); // Tekrar oku
                        }

                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(htmlContent);

                        // Indeed iş kartı seçicileri
                        var jobCards = document.DocumentNode.SelectNodes(
                            "//div[contains(@class,'job_seen_beacon')] | " +
                            "//div[contains(@class,'cardOutline')] | " +
                            "//li[contains(@class,'css-') and .//h2[contains(@class,'jobTitle')]]"
                        );

                        if (jobCards == null || jobCards.Count == 0)
                        {
                            Console.WriteLine($"  📭 Sayfa {startIdx / 15 + 1}: İlan bulunamadı.");
                            break;
                        }

                        int pageAdded = 0;

                        foreach (var card in jobCards)
                        {
                            // Başlık
                            var titleNode = card.SelectSingleNode(
                                ".//h2[contains(@class,'jobTitle')]//span | " +
                                ".//a[contains(@class,'jcs-JobTitle')]//span"
                            );
                            if (titleNode == null) continue;

                            string title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim());
                            title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
                            if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                            // Yazılım filtresi
                            if (!IsSoftwareRelated(title))
                            {
                                Console.WriteLine($"  ⏭️ Yazılımla alakasız: {title}");
                                continue;
                            }

                            // URL — Indeed job ID'sinden URL oluştur
                            var linkNode = card.SelectSingleNode(".//h2[contains(@class,'jobTitle')]//a | .//a[contains(@class,'jcs-JobTitle')]");
                            string jobUrl = "";
                            if (linkNode != null)
                            {
                                string href = linkNode.GetAttributeValue("href", "");
                                if (!string.IsNullOrWhiteSpace(href))
                                {
                                    // href /pagead/clk?... veya /rc/clk?... olabilir, jk= parametresinden ilan ID'si alınır
                                    if (href.StartsWith("http"))
                                        jobUrl = href;
                                    else
                                        jobUrl = "https://tr.indeed.com" + href;

                                    // Tracking parametrelerini kaldır
                                    int fromIdx = jobUrl.IndexOf("&from=");
                                    if (fromIdx > 0) jobUrl = jobUrl.Substring(0, fromIdx);
                                }
                            }

                            if (string.IsNullOrWhiteSpace(jobUrl) || jobUrl.Length < 20) continue;

                            // Şirket adı
                            var companyNode = card.SelectSingleNode(
                                ".//span[contains(@data-testid,'company-name')] | " +
                                ".//span[contains(@class,'companyName')]"
                            );
                            string company = companyNode != null ? companyNode.InnerText.Trim() : "Bilinmiyor";

                            // Lokasyon
                            var locNode = card.SelectSingleNode(
                                ".//div[contains(@data-testid,'text-location')] | " +
                                ".//div[contains(@class,'companyLocation')]"
                            );
                            string location = locNode != null ? locNode.InnerText.Trim() : "Türkiye";

                            if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                            db.JobPostings.Add(new JobPosting
                            {
                                Title = title.Length > 100 ? title.Substring(0, 100) : title,
                                CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                                Location = location.Length > 100 ? location.Substring(0, 100) : location,
                                Description = $"Indeed ilanı: {jobUrl}",
                                Url = jobUrl,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.Now,
                                DatePosted = DateTime.Now
                            });

                            pageAdded++;
                            totalAdded++;
                            Console.WriteLine($"  💾 {company} | {title}");
                        }

                        db.SaveChanges();
                        Console.WriteLine($"  ✅ Sayfa {startIdx / 15 + 1}: {pageAdded} yeni ilan.");

                        // Sayfa geçişlerinde insan simülasyonu
                        await Task.Delay(new Random().Next(3000, 5000));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ❌ Hata: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }
    }
}
