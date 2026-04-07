using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class LinkedInScraper : IJobScraper
    {
        public string ScraperName => "LinkedIn (Public)";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        private static readonly string[] _softwareKeywords = {
            "software", "developer", "geliştirici", "yazılım", "backend", "frontend",
            "fullstack", "full stack", "web", "mobile", "mobil", "android", "ios",
            "devops", "cloud", "data", "python", "java", "react", "angular", "node",
            ".net", "php", "engineer", "mühendis", "tech", "qa", "test",
            "typescript", "kotlin", "swift", "flutter", "c#", "golang", "api", "ai"
        };
        private static bool IsSoftwareRelated(string title) =>
            _softwareKeywords.Any(kw => title.ToLowerInvariant().Contains(kw));

        // Her keyword ayrı bir LinkedIn araması = ~60 ilan × 20 keyword = ~1200 ilan
        private readonly string[] _searchKeywords = {
            // Türkçe
            "Yazılım Geliştirici",
            "Yazılım Mühendisi",
            "Web Geliştirici",
            "Mobil Uygulama Geliştirici",
            "Veri Bilimci",
            "Veri Mühendisi",
            // İngilizce — Genel
            "Software Developer",
            "Software Engineer",
            "Backend Developer",
            "Frontend Developer",
            "Full Stack Developer",
            "Mobile Developer",
            // İngilizce — Rol bazlı
            "DevOps Engineer",
            "Cloud Engineer",
            "Data Engineer",
            "Data Scientist",
            "Machine Learning Engineer",
            "Android Developer",
            "iOS Developer",
            "QA Engineer",
            // Teknoloji bazlı
            "React Developer",
            "Python Developer",
            ".NET Developer",
            "Java Developer",
        };

        public async Task RunAsync()
        {
            await ShallowScrapeAsync();
            await DeepScrapeAsync();
        }

        private async Task ShallowScrapeAsync()
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            var launchOptions = new LaunchOptions
            {
                Headless = false,
                DefaultViewport = null,
                Args = new[] {
                    "--disable-blink-features=AutomationControlled",
                    "--window-size=1920,1080"
                }
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                { "Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7" }
            });

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            HashSet<string> processedUrls = new();
            int totalAdded = 0;

            foreach (var keyword in _searchKeywords)
            {
                Console.WriteLine($"\n🔍 LinkedIn'de '{keyword}' aranıyor...");

                // Orijinal çalışan URL formatı — tr.linkedin.com + location=Turkey
                // start= parametresi KULLANILMIYOR (o Galler'e götürüyordu)
                string encodedKeyword = Uri.EscapeDataString(keyword);
                string targetUrl = $"https://tr.linkedin.com/jobs/search?keywords={encodedKeyword}&location=Turkey";

                try
                {
                    await page.GoToAsync(targetUrl, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                        Timeout = 30000
                    });

                    Console.WriteLine("⏳ Sayfa yüklendi, scroll ve 'Daha Fazla Göster' eziliyor...");
                    await Task.Delay(4000);

                    // Orijinal çalışan yöntem: Scroll + popup kapat + "Daha Fazla Göster" ez
                    await page.EvaluateFunctionAsync(@"async () => {
                        for (let i = 0; i < 30; i++) {
                            // Pop-up öldür
                            let closeSelectors = [
                                'button.modal__dismiss',
                                'button.dismiss',
                                '[data-tracking-control-name=""public_jobs_sign-up-modal_modal_dismiss""]',
                                'button[aria-label=""Kapat""]',
                                'button[aria-label=""Close""]'
                            ];
                            for (let sel of closeSelectors) {
                                let btn = document.querySelector(sel);
                                if (btn) { btn.click(); }
                            }

                            // Sayfayı aşağı kaydır
                            window.scrollBy({ top: 800, behavior: 'smooth' });
                            await new Promise(r => setTimeout(r, 1500));

                            // En alta git
                            window.scrollTo(0, document.body.scrollHeight);
                            await new Promise(r => setTimeout(r, 1000));

                            // 'Daha Fazla Göster' butonunu ez
                            let btn = document.querySelector('button.infinite-scroller__show-more-button');
                            if (btn && btn.offsetParent !== null) {
                                btn.click();
                                await new Promise(r => setTimeout(r, 3000));
                            }
                        }
                    }");

                    Console.WriteLine("✅ Scroll tamamlandı, ilanlar toplanıyor...");

                    string htmlContent = await page.GetContentAsync();

                    // Auth wall var mı?
                    if (htmlContent.Contains("authwall") || htmlContent.Contains("uas/authenticate"))
                    {
                        Console.WriteLine("⚠️ Auth duvarı tespit edildi, bu keyword atlanıyor.");
                        continue;
                    }

                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(htmlContent);

                    var jobNodes = document.DocumentNode.SelectNodes(
                        "//a[contains(@class,'base-card__full-link')] | " +
                        "//a[contains(@class,'base-job-card__full-link')] | " +
                        "//a[contains(@href,'/jobs/view/')]"
                    );

                    if (jobNodes == null || jobNodes.Count == 0)
                    {
                        Console.WriteLine("⚠️ Bu keyword için ilan bulunamadı.");
                        continue;
                    }

                    int keywordAdded = 0;
                    foreach (var node in jobNodes)
                    {
                        string jobUrl = node.GetAttributeValue("href", "");
                        if (string.IsNullOrWhiteSpace(jobUrl)) continue;

                        // Tracking parametrelerini temizle
                        int qIdx = jobUrl.IndexOf('?');
                        if (qIdx > 0) jobUrl = jobUrl.Substring(0, qIdx);

                        if (jobUrl.Length < 20) continue;
                        if (!jobUrl.StartsWith("http")) jobUrl = "https://www.linkedin.com" + jobUrl;
                        if (processedUrls.Contains(jobUrl)) continue;
                        processedUrls.Add(jobUrl);

                        // Başlık, şirket, lokasyon — üst card'dan al
                        var card = node.ParentNode?.ParentNode;
                        string title = "LinkedIn İlanı";
                        string company = "LinkedIn Üzerinden Çekildi";
                        string location = "Türkiye";

                        if (card != null)
                        {
                            var titleNode = card.SelectSingleNode(".//span[contains(@class,'sr-only')] | .//h3 | .//h4");
                            var companyNode = card.SelectSingleNode(".//h4 | .//a[contains(@class,'hidden-nested-link')]");
                            var locNode = card.SelectSingleNode(".//span[contains(@class,'job-search-card__location')]");
                            if (titleNode != null) title = titleNode.InnerText.Trim();
                            if (companyNode != null && companyNode != titleNode) company = companyNode.InnerText.Trim();
                            if (locNode != null) location = locNode.InnerText.Trim();
                        }

                        // Yazılım filtresi
                        if (!IsSoftwareRelated(title))
                        {
                            Console.WriteLine($"⏭️ Alakasız, atlandı: {title}");
                            continue;
                        }

                        if (db.JobPostings.Any(j => j.Url == jobUrl)) continue;

                        db.JobPostings.Add(new JobPosting
                        {
                            Title = title.Length > 100 ? title.Substring(0, 100) : title,
                            CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                            Location = location.Length > 100 ? location.Substring(0, 100) : location,
                            Description = "Detaylar için: " + jobUrl,
                            Url = jobUrl,
                            Source = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.Now,
                            DatePosted = DateTime.Now
                        });

                        keywordAdded++;
                        totalAdded++;
                        Console.WriteLine($"💾 {company} | {title}");
                    }

                    db.SaveChanges();
                    Console.WriteLine($"✅ '{keyword}': {keywordAdded} yeni ilan (Toplam URL işlendi: {jobNodes.Count})");

                    // Keyword'ler arası bekleme
                    await Task.Delay(new Random().Next(3000, 5000));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Hata ({keyword}): {ex.Message}");
                }
            }

            Console.WriteLine($"\n💾 LinkedIn Toplam: {totalAdded} YENİ ilan ({processedUrls.Count} URL işlendi)");
        }

        private async Task DeepScrapeAsync()
        {
            Console.WriteLine("\n>>> Aşama 2: LinkedIn İlan Detayları Çekiliyor...");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jobsToUpdate = db.JobPostings
                .Where(j => j.CompanyName == "LinkedIn Üzerinden Çekildi" && j.Source == ScraperName)
                .ToList();

            if (jobsToUpdate.Count == 0)
            {
                Console.WriteLine("   ℹ️ Detaylandırılacak ilan yok.");
                return;
            }

            Console.WriteLine($"   🔄 {jobsToUpdate.Count} ilanın detayı çekilecek...");

            using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = false, DefaultViewport = null });
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
                    await Task.Delay(rnd.Next(3000, 5000));

                    string html = await page.GetContentAsync();
                    if (html.Contains("authwall")) { Console.WriteLine("⚠️ Auth duvarı, durduruldu."); break; }

                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var titleNode = doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'top-card-layout__title')] | //h1[contains(@class,'topcard__title')]");
                    var companyNode = doc.DocumentNode.SelectSingleNode("//a[contains(@class,'topcard__org-name-link')] | //span[contains(@class,'topcard__flavor--black-link')]");
                    var locNode = doc.DocumentNode.SelectSingleNode("//span[contains(@class,'topcard__flavor--bullet')]");
                    var descNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'description__text')] | //div[contains(@class,'show-more-less-html__markup')]");

                    if (titleNode != null) job.Title = titleNode.InnerText.Trim().Substring(0, Math.Min(titleNode.InnerText.Trim().Length, 100));
                    if (companyNode != null) job.CompanyName = companyNode.InnerText.Trim().Substring(0, Math.Min(companyNode.InnerText.Trim().Length, 100));
                    if (locNode != null) job.Location = locNode.InnerText.Trim().Substring(0, Math.Min(locNode.InnerText.Trim().Length, 100));
                    if (descNode != null) job.Description = System.Text.RegularExpressions.Regex.Replace(descNode.InnerText.Trim(), @"\s+", " ");

                    db.SaveChanges();
                    Console.WriteLine($"   ✅ {job.CompanyName} | {job.Title}");
                }
                catch (Exception ex) { Console.WriteLine($"   ❌ {ex.Message}"); }
            }
        }
    }
}