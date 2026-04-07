using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class WeWorkRemotelyScraper : IJobScraper
    {
        public string ScraperName => "WeWorkRemotely";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor... (Hedef: Global Remote İlanlar)");
            await ShallowScrapeAsync();
            await DeepScrapeAsync();
            Console.WriteLine($"✅ [{ScraperName}] Bütün görevlerini tamamladı!\n");
        }

        private async Task ShallowScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 1: Global Programlama İlanları Taranıyor...");

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

            string targetUrl = "https://weworkremotely.com/categories/remote-programming-jobs";

            try
            {
                await page.GoToAsync(targetUrl, WaitUntilNavigation.Networkidle2);
                await Task.Delay(3000);

                string htmlContent = await page.GetContentAsync();
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(htmlContent);

                // Güncel WWR selectors (inspect ile doğrulandı):
                // Linkler: a.listing-link veya a.listing-link--unlocked
                // Başlık: h3 içindeki span
                var jobNodes = document.DocumentNode.SelectNodes(
                    "//a[contains(@class,'listing-link')] | //article//ul//li//a[contains(@href,'/remote-jobs/')]"
                );

                if (jobNodes != null && jobNodes.Count > 0)
                {
                    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                    optionsBuilder.UseSqlServer(_connectionString);
                    using var db = new AppDbContext(optionsBuilder.Options);

                    int addedCount = 0;
                    HashSet<string> processedUrls = new HashSet<string>();

                    foreach (var node in jobNodes)
                    {
                        string jobUrl = node.GetAttributeValue("href", "");
                        if (jobUrl.Length < 10) continue;

                        // Sadece tekil ilan sayfaları al (/remote-jobs/ içermeli)
                        if (!jobUrl.Contains("/remote-jobs/") && !jobUrl.Contains("/job/")) continue;

                        string fullUrl = jobUrl.StartsWith("http") ? jobUrl : $"https://weworkremotely.com{jobUrl}";
                        if (processedUrls.Contains(fullUrl)) continue;
                        processedUrls.Add(fullUrl);

                        // Başlık: h3 > span içinden al
                        var titleSpan = node.SelectSingleNode(".//h3//span[not(contains(@class,'region'))] | .//span[contains(@class,'title')]");
                        string jobTitle = titleSpan?.InnerText.Trim() ?? node.InnerText.Replace("\n", " ").Replace("\r", "").Trim();
                        jobTitle = System.Text.RegularExpressions.Regex.Replace(jobTitle, @"\s+", " ").Trim();

                        if (string.IsNullOrWhiteSpace(jobTitle) || jobTitle.Length < 5) continue;

                        // Şirket adı: p tag (linkin direkt çocuğu)
                        var companyPara = node.SelectSingleNode(".//p[not(contains(@class,'tag'))]");
                        string companyName = companyPara?.InnerText.Trim() ?? "WWR İlanı";

                        if (!db.JobPostings.Any(j => j.Url == fullUrl))
                        {
                            db.JobPostings.Add(new JobPosting
                            {
                                Title = jobTitle.Length > 100 ? jobTitle.Substring(0, 100) : jobTitle,
                                CompanyName = companyName.Length > 100 ? companyName.Substring(0, 100) : companyName,
                                Location = "Global / Remote",
                                Description = "Detaylar çekilecek...",
                                Url = fullUrl,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.Now,
                                DatePosted = DateTime.Now
                            });
                            addedCount++;
                        }
                    }
                    db.SaveChanges();
                    Console.WriteLine($"💾 Yüzeysel Kazıma Bitti: {addedCount} YENİ ilan eklendi.");
                }
                else
                {
                    Console.WriteLine("⚠️ WeWorkRemotely'de ilan bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Hata: {ex.Message}");
            }
        }

        private async Task DeepScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 2: Detaylar (Açıklamalar) Çekiliyor...");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jobsToUpdate = db.JobPostings
                .Where(j => j.Description == "Detaylar çekilecek..." && j.Source == ScraperName)
                .ToList();
            if (jobsToUpdate.Count == 0) return;

            Console.WriteLine($"   → {jobsToUpdate.Count} ilan detaylandırılacak.");

            using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = false, DefaultViewport = null });
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            Random rnd = new Random();

            foreach (var job in jobsToUpdate)
            {
                try
                {
                    await page.GoToAsync(job.Url, new NavigationOptions { Timeout = 30000, WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                    await Task.Delay(rnd.Next(3000, 6000));

                    // Cloudflare kontrolü — başlık elementini bekle
                    try
                    {
                        await page.WaitForSelectorAsync("h1, .listing-header, .job-listing", new WaitForSelectorOptions { Timeout = 15000 });
                    }
                    catch
                    {
                        Console.WriteLine($"⚠️ Sayfa yüklenemedi, atlanıyor: {job.Title}");
                        continue;
                    }

                    string htmlContent = await page.GetContentAsync();
                    HtmlDocument detailDoc = new HtmlDocument();
                    detailDoc.LoadHtml(htmlContent);

                    var companyNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'company-card')]//h2 | //h2[contains(@class,'company')] | //header//h2"
                    );
                    var descNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'listing-container')] | //div[contains(@id,'job-listing-show-container')] | //div[contains(@class,'job-description')]"
                    );

                    string companyName = companyNode != null ? companyNode.InnerText.Trim() : job.CompanyName ?? "Firma Çekilemedi";
                    string fullDescription = descNode != null
                        ? System.Text.RegularExpressions.Regex.Replace(descNode.InnerText.Trim(), @"\s+", " ")
                        : job.Description;

                    job.CompanyName = companyName.Length > 100 ? companyName.Substring(0, 100) : companyName;
                    job.Description = fullDescription;

                    db.SaveChanges();
                    Console.WriteLine($"   - ✅ Güncellendi: {job.CompanyName?.Substring(0, Math.Min(job.CompanyName?.Length ?? 0, 25))} | {job.Title?.Substring(0, Math.Min(job.Title?.Length ?? 0, 25))}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   - ❌ Hata: {ex.Message}");
                }
            }
        }
    }
}