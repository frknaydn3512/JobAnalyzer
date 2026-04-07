using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class BionlukScraper : IJobScraper
    {
        public string ScraperName => "Bionluk";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor...");
            await ShallowScrapeAsync();
            Console.WriteLine($"✅ [{ScraperName}] Bütün görevlerini tamamladı!\n");
        }

        private async Task ShallowScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 1: Bionluk Yazılım İşleri Taranıyor...");

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            var launchOptions = new LaunchOptions { Headless = false, DefaultViewport = null };
            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // Bionluk Yazılım & Teknoloji kategorisi
            string targetUrl = "https://bionluk.com/kategoriler/yazilim-teknoloji";

            try
            {
                await page.GoToAsync(targetUrl, WaitUntilNavigation.Networkidle2);
                await Task.Delay(3000); // Sayfanın toparlanması için kısa bir bekleme

                string htmlContent = await page.GetContentAsync();
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(htmlContent);

                // Bionluk'taki ilan kartları genelde "freelancer-sub-category" veya "freelancer-card" gibi class'lar kullanır
                // Not: Bionluk HTML yapısı değişkendir, buradaki XPath'i siteyi F12 ile inceleyip gerekirse güncelleyeceğiz.
                var jobNodes = document.DocumentNode.SelectNodes("//a[contains(@class, 'freelancer-card')] | //div[contains(@class, 'service-card')]//a");

                if (jobNodes != null)
                {
                    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                    optionsBuilder.UseSqlServer(_connectionString);
                    using var db = new AppDbContext(optionsBuilder.Options);

                    int addedCount = 0;
                    foreach (var node in jobNodes)
                    {
                        string jobUrl = node.GetAttributeValue("href", "");
                        string jobTitle = node.InnerText.Replace("\n", "").Replace("\r", "").Trim();
                        jobTitle = System.Text.RegularExpressions.Regex.Replace(jobTitle, @"\s+", " ");

                        if (string.IsNullOrWhiteSpace(jobTitle) || jobTitle.Length < 5) continue;

                        string fullUrl = jobUrl.StartsWith("http") ? jobUrl : $"https://bionluk.com{jobUrl}";

                        if (!db.JobPostings.Any(j => j.Url == fullUrl))
                        {
                            var newJob = new JobPosting
                            {
                                Title = jobTitle.Length > 100 ? jobTitle.Substring(0, 100) : jobTitle,
                                CompanyName = "Bionluk İşvereni/Freelancer",
                                Location = "Uzaktan / Freelance",
                                Description = "Bionluk yazılım/teknoloji ilanı. Detaylar eklenecek.",
                                Url = fullUrl,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                JobType = "Freelance İlanı",
                                DateScraped = DateTime.Now,
                                DatePosted = DateTime.Now
                            };
                            db.JobPostings.Add(newJob);
                            addedCount++;
                            Console.WriteLine($"💾 Bionluk'tan Bulundu: {newJob.Title.Substring(0, Math.Min(newJob.Title.Length, 40))}...");
                        }
                    }
                    db.SaveChanges();
                    Console.WriteLine($"💾 Bionluk Kazıma Bitti: {addedCount} YENİ ilan eklendi.");
                }
                else
                {
                    Console.WriteLine("⚠️ Bionluk'ta ilan bulunamadı (HTML yapısı farklı olabilir).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Bionluk Botu Hata Aldı: {ex.Message}");
            }
        }
    }
}