using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class JoobleScraper : IJobScraper
    {
        public string ScraperName => "Jooble";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Başlatılıyor...");
            await ShallowScrapeAsync();
            await DeepScrapeAsync();
            Console.WriteLine($"✅ [{ScraperName}] Bütün görevlerini tamamladı!\n");
        }

        private async Task ShallowScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 1: Jooble İlanları Taranıyor...");
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

            string targetUrl = "https://tr.jooble.org/SearchResult?ukw=yaz%C4%B1l%C4%B1m%20geli%C5%9Ftirici";

            try
            {
                await page.GoToAsync(targetUrl, WaitUntilNavigation.Networkidle2);
                await Task.Delay(3000);

                // Pop-up ve çerez ekranlarını kapat
                await page.EvaluateFunctionAsync(@"async () => {
                    // 'Hayır' butonunu tıkla (modal'ı kapat)
                    let btns = [...document.querySelectorAll('button')];
                    let noBtn = btns.find(b => b.innerText.trim() === 'Hayır' || b.innerText.trim() === 'Reddet');
                    if (noBtn) noBtn.click();
                    await new Promise(r => setTimeout(r, 500));
                    // Çerez reddet
                    let cookieBtn = document.querySelector('[class*=""reject""], [class*=""decline""]');
                    if (cookieBtn) cookieBtn.click();
                }");
                await Task.Delay(2000);

                string htmlContent = await page.GetContentAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                // Yeni Jooble selector: job_card_link class içeren linkler
                var jobNodes = doc.DocumentNode.SelectNodes(
                    "//a[contains(@class,'job_card_link')] | //a[contains(@href,'/desc/')] | //a[contains(@href,'/away/')]"
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
                        string hrefUrl = node.GetAttributeValue("href", "");
                        if (string.IsNullOrWhiteSpace(hrefUrl) || hrefUrl.Length < 5) continue;

                        string fullUrl = hrefUrl.StartsWith("http") ? hrefUrl : $"https://tr.jooble.org{hrefUrl}";
                        if (processedUrls.Contains(fullUrl)) continue;
                        processedUrls.Add(fullUrl);

                        // Başlığı bul: önce h2'yi dene, yoksa link metni
                        string title = node.SelectSingleNode(".//h2")?.InnerText.Trim()
                                    ?? node.InnerText.Trim();
                        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
                        if (string.IsNullOrWhiteSpace(title) || title.Length < 5) continue;

                        if (!db.JobPostings.Any(j => j.Url == fullUrl))
                        {
                            db.JobPostings.Add(new JobPosting
                            {
                                Title = title.Length > 100 ? title.Substring(0, 100) : title,
                                CompanyName = "Daha Sonra Çekilecek",
                                Location = "Türkiye",
                                Description = "Detaylar daha sonra çekilecek.",
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
                    Console.WriteLine($"💾 Jooble Aşama 1 Bitti: {addedCount} yeni link eklendi!");
                }
                else
                {
                    Console.WriteLine("⚠️ Jooble'da ilan bulunamadı (Belki bot tespiti oldu).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Hata: {ex.Message}");
            }
        }

        private async Task DeepScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 2: Jooble İlan Detayları Çekiliyor...");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jobsToUpdate = db.JobPostings
                .Where(j => j.CompanyName == "Daha Sonra Çekilecek" && j.Source == ScraperName)
                .ToList();
            if (jobsToUpdate.Count == 0) return;

            Console.WriteLine($"   → {jobsToUpdate.Count} ilan detaylandırılacak.");

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();
            using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = false, DefaultViewport = null });
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            Random rnd = new Random();

            foreach (var job in jobsToUpdate)
            {
                try
                {
                    await page.GoToAsync(job.Url, new NavigationOptions { Timeout = 30000, WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                    await Task.Delay(rnd.Next(2000, 4000));

                    string htmlContent = await page.GetContentAsync();
                    var detailDoc = new HtmlDocument();
                    detailDoc.LoadHtml(htmlContent);

                    // Geniş hedeflerle şirket ve açıklama çek
                    var companyNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'company')]//a | //span[contains(@class,'company')] | //p[contains(@class,'company')] | //div[contains(@class,'job-company')]"
                    );
                    var descNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'vacancy-desc')] | //div[contains(@class,'description')] | //article | //div[contains(@class,'job-description')]"
                    );

                    job.CompanyName = companyNode != null
                        ? companyNode.InnerText.Trim().Substring(0, Math.Min(companyNode.InnerText.Trim().Length, 100))
                        : "Firma Çekilemedi";
                    job.Description = descNode != null
                        ? System.Text.RegularExpressions.Regex.Replace(descNode.InnerText.Trim(), @"\s+", " ")
                        : job.Description;

                    db.SaveChanges();
                    Console.WriteLine($"   - ✅ Güncellendi: {job.CompanyName.Substring(0, Math.Min(job.CompanyName.Length, 30))}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   - ❌ Hata ({job.Title?.Substring(0, Math.Min(job.Title?.Length ?? 0, 20))}): {ex.Message}");
                }
            }
        }
    }
}