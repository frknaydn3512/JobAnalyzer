using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class TechcareerScraper : IJobScraper
    {
        public string ScraperName => "Techcareer";
        private readonly string _connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JobAnalyzerDb;Trusted_Connection=True;TrustServerCertificate=True;";

        public async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor...");
            await ShallowScrapeAsync();
            await DeepScrapeAsync();
            Console.WriteLine($"✅ [{ScraperName}] Bütün görevlerini tamamladı!\n");
        }

        private async Task ShallowScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 1: Techcareer İlanları Taranıyor...");

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            var launchOptions = new LaunchOptions { Headless = false, DefaultViewport = null, Args = new[] { "--disable-blink-features=AutomationControlled" } };
            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            string targetUrl = "https://www.techcareer.net/jobs";

            try
            {
                await page.GoToAsync(targetUrl, WaitUntilNavigation.Networkidle2);
                Console.WriteLine("⏳ Sayfa yükleniyor ve scroll yapılıyor...");
                await Task.Delay(3000);

                // Lazy-load ilanları yüklemek için sayfayı aşağı kaydır
                await page.EvaluateFunctionAsync(@"async () => {
                    for (let i = 0; i < 30; i++) {
                        window.scrollBy({ top: 600, behavior: 'smooth' });
                        await new Promise(r => setTimeout(r, 1200));
                    }
                    window.scrollTo(0, document.body.scrollHeight);
                    await new Promise(r => setTimeout(r, 1500));
                }");
                await Task.Delay(2000);

                string htmlContent = await page.GetContentAsync();
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(htmlContent);

                var jobNodes = document.DocumentNode.SelectNodes("//a[contains(@href, '/jobs/detail/')]");

                if (jobNodes != null)
                {
                    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                    optionsBuilder.UseSqlServer(_connectionString);
                    using var db = new AppDbContext(optionsBuilder.Options);

                    int addedCount = 0;
                    HashSet<string> processedUrls = new HashSet<string>();

                    foreach (var node in jobNodes)
                    {
                        string jobUrl = node.GetAttributeValue("href", "");

                        // Kategori/Ana sayfa linklerini baştan ele
                        if (jobUrl == "/jobs" || jobUrl.Length < 15 || jobUrl.Contains("is-ilanlari") || jobUrl.Contains("ilanlari")) continue;

                        string fullUrl = jobUrl.StartsWith("http") ? jobUrl : $"https://www.techcareer.net{jobUrl}";

                        if (processedUrls.Contains(fullUrl)) continue;
                        processedUrls.Add(fullUrl);

                        string jobTitle = node.InnerText.Replace("\n", "").Replace("\r", "").Trim();
                        jobTitle = System.Text.RegularExpressions.Regex.Replace(jobTitle, @"\s+", " ");

                        if (string.IsNullOrWhiteSpace(jobTitle) || jobTitle.Length < 5) continue;

                        // ==========================================
                        // YENİ ÇÖP FİLTRESİ (Anti-Garbage System)
                        // ==========================================
                        string lowerTitle = jobTitle.ToLower();

                        // Başlığında bu kelimeler geçenleri çöpe at
                        if (lowerTitle.Contains("ilanları") ||
                            lowerTitle.Contains("tümünü gör") ||
                            lowerTitle.Contains("tıklayın") ||
                            lowerTitle.Contains("iş yerinde") ||
                            lowerTitle.Contains("sözleşmeli"))
                        {
                            continue;
                        }

                        // Regex ile sonu "(5)" gibi rakamla biten kategori isimlerini engelle
                        if (System.Text.RegularExpressions.Regex.IsMatch(jobTitle, @"\(\d+\)$"))
                        {
                            continue;
                        }
                        // ==========================================

                        if (!db.JobPostings.Any(j => j.Url == fullUrl))
                        {
                            var newJob = new JobPosting
                            {
                                Title = jobTitle.Length > 100 ? jobTitle.Substring(0, 100) : jobTitle,
                                CompanyName = "Daha Sonra Çekilecek",
                                Location = "Daha Sonra Çekilecek",
                                Description = "Detaylar çekilecek...",
                                Url = fullUrl,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.Now,
                                DatePosted = DateTime.Now
                            };
                            db.JobPostings.Add(newJob);
                            addedCount++;
                            Console.WriteLine($"💾 Temiz İlan Bulundu: {newJob.Title.Substring(0, Math.Min(newJob.Title.Length, 40))}...");
                        }
                    }
                    db.SaveChanges();
                    Console.WriteLine($"💾 Yüzeysel Kazıma Bitti: {addedCount} YENİ ilan eklendi.");
                }
                else
                {
                    Console.WriteLine("⚠️ Techcareer'da ilan bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Hata: {ex.Message}");
            }
        }

        private async Task DeepScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 2: Detaylar (Şirket/Şehir/Açıklama) Çekiliyor...");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jobsToUpdate = db.JobPostings.Where(j => j.CompanyName == "Daha Sonra Çekilecek" && j.Source == ScraperName).ToList();
            if (jobsToUpdate.Count == 0) return;

            var launchOptions = new LaunchOptions { Headless = false, DefaultViewport = null, Args = new[] { "--disable-blink-features=AutomationControlled" } };
            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            foreach (var job in jobsToUpdate)
            {
                try
                {
                    await page.GoToAsync(job.Url, WaitUntilNavigation.Networkidle2);
                    Thread.Sleep(2000);

                    string htmlContent = await page.GetContentAsync();
                    HtmlDocument detailDoc = new HtmlDocument();
                    detailDoc.LoadHtml(htmlContent);

                    var companyNode = detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'company')] | //h2[contains(@class, 'title')] | //span[contains(@class, 'company')]");
                    var descNode = detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'description')] | //div[contains(@class, 'content')] | //div[contains(@class, 'text-content')]");

                    string companyName = companyNode != null ? companyNode.InnerText.Trim() : "Firma Çekilemedi";
                    string fullDescription = descNode != null ? System.Text.RegularExpressions.Regex.Replace(descNode.InnerText.Trim(), @"\s+", " ") : job.Description;

                    job.CompanyName = companyName.Length > 100 ? companyName.Substring(0, 100) : companyName;
                    job.Location = "Belirtilmemiş / Remote";
                    job.Description = fullDescription;

                    db.SaveChanges();
                    Console.WriteLine($"   - Güncellendi: {job.Title.Substring(0, Math.Min(job.Title.Length, 20))}...");
                }
                catch { /* Hata olursa atla */ }
            }
        }
    }
}