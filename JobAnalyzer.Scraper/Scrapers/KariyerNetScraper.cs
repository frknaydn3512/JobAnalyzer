using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class KariyerNetScraper : IJobScraper
    {
        public string ScraperName => "Kariyer.net";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        // Yazılım/teknoloji keyword whitelist
        private static readonly string[] _softwareKeywords = {
            "software", "developer", "geliştirici", "yazılım", "backend", "frontend",
            "fullstack", "full stack", "web", "mobile", "mobil", "android", "ios",
            "devops", "cloud", "data", "python", "java", "react", "angular", "node",
            ".net", "php", "engineer", "mühendis", "makine", "cyber", "sistem",
            "tech", "qa", "test", "typescript", "kotlin", "swift", "flutter",
            "scrum", "architect", "database", "api", "c#", "c++", "golang", "yapay zeka",
            "ai", "bilişim", "it ", "ux", "ui"
        };
        private static bool IsSoftwareRelated(string title) =>
            _softwareKeywords.Any(kw => title.ToLowerInvariant().Contains(kw));

        // Aramalara göre çalışıyor — birden fazla keyword, maksimum sayfa
        // Kariyer.net: sayfa başına ~50 ilan, 916 = 18 sayfa
        private readonly (string keyword, int maxPages)[] _searches =
        {
            ("yazılım",             50),   // ~2500 ilan hedefi
            ("software developer",   15),
            ("backend developer",    10),
            ("frontend developer",   10),
            ("mobile developer",     10),
            ("devops",               5),
        };

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor...");

            await ShallowScrapeAsync();
            await DeepScrapeAsync();

            Console.WriteLine($"✅ [{ScraperName}] Tamamlandı!\n");
        }

        private async Task ShallowScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 1: Yeni İlanlar Taranıyor...");

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

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            int totalAdded = 0;

            foreach (var (keyword, maxPages) in _searches)
            {
                Console.WriteLine($"\n🔍 '{keyword}' aranıyor (max {maxPages} sayfa)...");

                for (int cp = 1; cp <= maxPages; cp++)
                {
                    // DOĞRU URL FORMATI: cp= parametresi (pg= değil!)
                    string encodedKw = Uri.EscapeDataString(keyword);
                    string targetUrl = $"https://www.kariyer.net/is-ilanlari?kw={encodedKw}&cp={cp}";

                    try
                    {
                        await page.GoToAsync(targetUrl, new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                            Timeout = 60000 // Cloudflare "Ekrana basılı tut" için 60 sn
                        });

                        // İlk sayfada cookie/popup kapat
                        if (cp == 1)
                        {
                            await Task.Delay(2000);
                            await page.EvaluateFunctionAsync(@"() => {
                                // Cookie banner kapat
                                let cookieBtn = document.querySelector('button.kabul-et, [data-test=""accept""], button[class*=""cookie""]');
                                if (cookieBtn) cookieBtn.click();
                                // Popup kapat
                                let closeBtn = document.querySelector('.modal-close, button[class*=""close""], .popup-close');
                                if (closeBtn) closeBtn.click();
                            }");
                        }

                        await Task.Delay(1500);

                        // Sayfa boyunca hafif scroll — lazy load ilanları yüklemek için
                        await page.EvaluateFunctionAsync(@"async () => {
                            window.scrollTo(0, document.body.scrollHeight / 2);
                            await new Promise(r => setTimeout(r, 500));
                            window.scrollTo(0, document.body.scrollHeight);
                            await new Promise(r => setTimeout(r, 500));
                        }");
                        await Task.Delay(1000);

                        string htmlContent = await page.GetContentAsync();
                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(htmlContent);

                        // DOĞRU SELECTOR (browser ile doğrulandı):
                        // a.k-ad-card.radius — her ilan kartı bu tag
                        var jobCards = document.DocumentNode.SelectNodes(
                            "//a[contains(@class,'k-ad-card') and contains(@class,'radius')]"
                        );

                        // Fallback selector
                        if (jobCards == null || jobCards.Count == 0)
                        {
                            jobCards = document.DocumentNode.SelectNodes(
                                "//a[contains(@class,'k-ad-card')]"
                            );
                        }

                        if (jobCards == null || jobCards.Count == 0)
                        {
                            Console.WriteLine($"   📭 Sayfa {cp}: İlan yok → Son sayfa veya selector tutmadı.");
                            break;
                        }

                        int pageAdded = 0;
                        foreach (var card in jobCards)
                        {
                            string href = card.GetAttributeValue("href", "");
                            if (string.IsNullOrWhiteSpace(href)) continue;

                            // Tracking parametrelerini temizle
                            int qIdx = href.IndexOf('?');
                            if (qIdx > 0) href = href.Substring(0, qIdx);

                            string jobUrl = href.StartsWith("http")
                                ? href
                                : "https://www.kariyer.net" + href;

                            // Sadece iş ilanı URL'leri al
                            if (!jobUrl.Contains("/is-ilani/")) continue;

                            // DOĞRU BAŞLIK SELECTOR: span.k-ad-card-title.multiline
                            var titleNode = card.SelectSingleNode(
                                ".//span[contains(@class,'k-ad-card-title')]"
                            );
                            var companyNode = card.SelectSingleNode(
                                ".//span[contains(@class,'k-ad-card-company-name')] | " +
                                ".//span[contains(@class,'company')]"
                            );
                            var locationNode = card.SelectSingleNode(
                                ".//span[contains(@class,'location')] | " +
                                ".//span[contains(@class,'city')] | " +
                                ".//li[contains(@class,'location')]"
                            );

                            string title = titleNode != null
                                ? System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim())
                                : card.InnerText.Trim().Split('\n')[0].Trim();

                            title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
                            if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                            // Sponsorlu ilanları atla
                            if (title.Contains("Sponsorlu") || title.Contains("sponsorlu")) continue;

                            // Yazılım filtresi — "yazılım" arama zaten filtreli olduğundan sadece açık filtre
                            if (!IsSoftwareRelated(title))
                            {
                                Console.WriteLine($"   ⏭️ Atlandı (alakasız): {title.Substring(0, Math.Min(title.Length, 40))}");
                                continue;
                            }

                            string company = companyNode != null
                                ? companyNode.InnerText.Trim()
                                : "Daha Sonra Çekilecek";
                            string location = locationNode != null
                                ? locationNode.InnerText.Trim()
                                : "Türkiye";

                            if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                            db.JobPostings.Add(new JobPosting
                            {
                                Title = title.Length > 100 ? title.Substring(0, 100) : title,
                                CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                                Location = location.Length > 100 ? location.Substring(0, 100) : location,
                                Description = $"Kariyer.net ilanı: {jobUrl}",
                                Url = jobUrl,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.Now,
                                DatePosted = DateTime.Now
                            });

                            pageAdded++;
                            totalAdded++;
                            Console.WriteLine($"   💾 {company} | {title.Substring(0, Math.Min(title.Length, 50))}");
                        }

                        db.SaveChanges();
                        Console.WriteLine($"   ✅ Sayfa {cp}: {pageAdded} yeni ilan ({jobCards.Count} kart bulundu)");

                        // Sayfa geçişlerinde nazik bekleme
                        await Task.Delay(new Random().Next(1500, 2500));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ❌ Hata (Sayfa {cp}): {ex.Message}");
                        await Task.Delay(3000);
                    }
                }
            }

            Console.WriteLine($"\n💾 KariyerNet Yüzeysel Kazıma Bitti: {totalAdded} YENİ ilan eklendi.");
        }

        private async Task DeepScrapeAsync()
        {
            Console.WriteLine("\n>>> Aşama 2: Detaylar (Şirket/Şehir/Açıklama) Çekiliyor...");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jobsToUpdate = db.JobPostings
                .Where(j => j.CompanyName == "Daha Sonra Çekilecek" && j.Source == ScraperName)
                .ToList();

            if (jobsToUpdate.Count == 0)
            {
                Console.WriteLine("   ℹ️ Detaylandırılacak ilan yok.");
                return;
            }

            Console.WriteLine($"   🔄 {jobsToUpdate.Count} ilanın detayı çekilecek...");

            var launchOptions = new LaunchOptions
            {
                Headless = false,
                DefaultViewport = null,
                Args = new[] { "--disable-blink-features=AutomationControlled" }
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            Random rnd = new Random();

            foreach (var job in jobsToUpdate)
            {
                try
                {
                    await page.GoToAsync(job.Url, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                        Timeout = 20000
                    });
                    await Task.Delay(rnd.Next(1500, 3000));

                    string htmlContent = await page.GetContentAsync();
                    HtmlDocument detailDoc = new HtmlDocument();
                    detailDoc.LoadHtml(htmlContent);

                    var companyNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//a[contains(@class,'company-name')] | " +
                        "//div[contains(@class,'company-name')] | " +
                        "//span[contains(@class,'company-name')] | " +
                        "//h2[contains(@class,'company')]"
                    );
                    var locationNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//li[contains(@class,'location')] | " +
                        "//span[contains(@class,'location')] | " +
                        "//span[contains(@class,'city')]"
                    );
                    var descNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'job-detail-content')] | " +
                        "//div[contains(@class,'description')] | " +
                        "//section[contains(@class,'detail')]"
                    );

                    if (companyNode != null)
                        job.CompanyName = companyNode.InnerText.Trim().Substring(0, Math.Min(companyNode.InnerText.Trim().Length, 100));

                    if (locationNode != null)
                        job.Location = locationNode.InnerText.Trim().Replace("Şehir", "").Trim().Substring(0, Math.Min(locationNode.InnerText.Trim().Length, 100));

                    if (descNode != null)
                        job.Description = System.Text.RegularExpressions.Regex.Replace(descNode.InnerText.Trim(), @"\s+", " ");

                    db.SaveChanges();
                    Console.WriteLine($"   ✅ {job.CompanyName} | {job.Title.Substring(0, Math.Min(job.Title.Length, 40))}");
                }
                catch
                {
                    // Sessizce atla
                }
            }
        }
    }
}