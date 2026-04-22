using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class TechcareerScraper : ScraperBase
    {
        public override string ScraperName => "Techcareer";

        public override async Task RunAsync()
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
            try { await browserFetcher.DownloadAsync(); }
            catch (Exception fetchEx)
            {
                Console.WriteLine($"  ⚠️ Chromium indirme hatası: {fetchEx.Message}");
                Console.WriteLine("  💡 İnternet bağlantısını kontrol edin veya chromium'u manuel yükleyin.");
                return;
            }

            var launchOptions = new LaunchOptions { Headless = false, DefaultViewport = null, Args = new[] { "--disable-blink-features=AutomationControlled" } };
            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            int totalAdded = 0;
            int consecutiveEmptyPages = 0;
            HashSet<string> processedUrls = new HashSet<string>();

            for (int pageNum = 1; pageNum <= 50; pageNum++)
            {
                string pageUrl = pageNum == 1
                    ? "https://www.techcareer.net/jobs"
                    : $"https://www.techcareer.net/jobs?jobs[isCompleted]=false&jobs[page]={pageNum}&searchIdIsFiltered=false";

                Console.WriteLine($"\n  📄 Sayfa {pageNum} yükleniyor: {pageUrl}");

                try
                {
                    await page.GoToAsync(pageUrl, WaitUntilNavigation.Networkidle2);
                    await Task.Delay(2500);

                    // Lazy-load içerik için aşağı scroll
                    await page.EvaluateFunctionAsync(@"() => window.scrollTo(0, document.body.scrollHeight)");
                    await Task.Delay(1500);
                    await page.EvaluateFunctionAsync(@"() => window.scrollTo(0, document.body.scrollHeight)");
                    await Task.Delay(1000);

                    string html = await page.GetContentAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // Sayfa 404 / yönlendirme kontrolü — içerik yoksa dur
                    string currentUrl = page.Url;
                    if (!currentUrl.Contains("techcareer.net/jobs")) break;

                    // Techcareer ilan linki formatı: /jobs/detail/{slug}-{id}
                    var jobNodes = doc.DocumentNode.SelectNodes(
                        "//a[contains(@href, '/jobs/detail/')]"
                    );

                    Console.WriteLine($"  🔗 Bulunan ilan node sayısı: {jobNodes?.Count ?? 0}");

                    if (jobNodes == null || jobNodes.Count == 0)
                    {
                        Console.WriteLine($"  📭 Sayfa {pageNum}: İlan node'u bulunamadı — sayfalama bitti.");
                        consecutiveEmptyPages++;
                        if (consecutiveEmptyPages >= 2) break;
                        continue;
                    }

                    int pageAdded = 0;
                    foreach (var node in jobNodes)
                    {
                        string href = node.GetAttributeValue("href", "");
                        if (href.TrimEnd('/') == "/jobs" || href.Length < 15) continue;

                        string fullUrl = href.StartsWith("http") ? href : $"https://www.techcareer.net{href}";
                        if (processedUrls.Contains(fullUrl)) continue;
                        processedUrls.Add(fullUrl);

                        // İlanın başlığını node'un InnerText'inden çıkar
                        var lines = node.InnerText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(l => l.Trim())
                                                  .Where(l => l.Length > 2)
                                                  .ToList();
                        string jobTitle = lines.FirstOrDefault() ?? "";
                        jobTitle = System.Text.RegularExpressions.Regex.Replace(jobTitle, @"\s+", " ").Trim();

                        if (jobTitle.Length < 5) continue;
                        if (jobTitle.Contains("ilanları", StringComparison.OrdinalIgnoreCase) ||
                            jobTitle.Contains("tümünü gör", StringComparison.OrdinalIgnoreCase) ||
                            System.Text.RegularExpressions.Regex.IsMatch(jobTitle, @"\(\d+\)$"))
                            continue;

                        if (db.JobPostings.Any(j => j.Url == fullUrl)) continue;

                        var newJob = new JobPosting
                        {
                            Title = jobTitle.Length > 100 ? jobTitle.Substring(0, 100) : jobTitle,
                            CompanyName = "Daha Sonra Çekilecek",
                            Location = "Daha Sonra Çekilecek",
                            Description = "",
                            Url = fullUrl.Length > 500 ? fullUrl.Substring(0, 500) : fullUrl,
                            Source = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.UtcNow,
                            DatePosted = DateTime.UtcNow
                        };
                        try
                        {
                            db.JobPostings.Add(newJob);
                            db.SaveChanges();
                            pageAdded++;
                            totalAdded++;
                            Console.WriteLine($"    💾 {newJob.Title!.Substring(0, Math.Min(newJob.Title.Length, 50))}");
                        }
                        catch (Exception saveEx)
                        {
                            db.ChangeTracker.Clear();
                            Console.WriteLine($"    ⚠️ Kayıt hatası: {saveEx.InnerException?.Message ?? saveEx.Message}");
                        }
                    }

                    Console.WriteLine($"  ✅ Sayfa {pageNum}: {pageAdded} YENİ ilan eklendi.");

                    // Bu sayfada hiç yeni ilan yoksa sayacı artır
                    if (pageAdded == 0)
                        consecutiveEmptyPages++;
                    else
                        consecutiveEmptyPages = 0;

                    // 2 sayfa üst üste boşsa dur (ilanlar bitti ya da hepsi zaten DB'de)
                    if (consecutiveEmptyPages >= 2)
                    {
                        Console.WriteLine("  🏁 Art arda 2 boş sayfa — tarama tamamlandı.");
                        break;
                    }

                    await Task.Delay(1000); // Sunucuya nazik ol
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Sayfa {pageNum} hata: {ex.Message}");
                    consecutiveEmptyPages++;
                    if (consecutiveEmptyPages >= 3) break;
                }
            }

            Console.WriteLine($"\n💾 Yüzeysel Kazıma Bitti: Toplam {totalAdded} YENİ ilan eklendi.");
        }

        private async Task DeepScrapeAsync()
        {
            Console.WriteLine(">>> Aşama 2: Detaylar (Şirket/Şehir/Açıklama) Çekiliyor...");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            var jobsToUpdate = db.JobPostings
                .Where(j => j.CompanyName == "Daha Sonra Çekilecek" && j.Source == ScraperName)
                .ToList();

            if (jobsToUpdate.Count == 0)
            {
                Console.WriteLine("   ℹ️ Güncellenecek ilan yok.");
                return;
            }

            Console.WriteLine($"   📋 {jobsToUpdate.Count} ilan güncelleniyor (HttpClient ile)...");

            // Detay sayfaları SSR — Puppeteer'a gerek yok, HttpClient çok daha hızlı
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9");
            client.Timeout = TimeSpan.FromSeconds(20);

            int updatedCount = 0;
            int failCount = 0;

            foreach (var job in jobsToUpdate)
            {
                try
                {
                    var response = await client.GetAsync(job.Url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"   ⚠️ HTTP {(int)response.StatusCode}: {job.Url?[..Math.Min(job.Url?.Length ?? 0, 60)]}");
                        failCount++;
                        continue;
                    }

                    string html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // Firma: /sirketler/ linkinde her zaman var
                    var companyNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href, '/sirketler/')]");

                    // Konum: "İstanbul / Türkiye" formatındaki metin — şehir + ülke pattern'i ara
                    string locText = "Türkiye";
                    var allText = doc.DocumentNode.InnerText;
                    var locMatch = System.Text.RegularExpressions.Regex.Match(
                        allText, @"([A-ZÇĞİÖŞÜa-zçğışöşü]+(?:\s+[A-ZÇĞİÖŞÜa-zçğışöşü]+)?)\s*/\s*Türkiye");
                    if (locMatch.Success)
                        locText = locMatch.Value.Trim();

                    // Açıklama: main içeriği al, nav/footer sil
                    var mainNode = doc.DocumentNode.SelectSingleNode("//main") ??
                                  doc.DocumentNode.SelectSingleNode("//article") ??
                                  doc.DocumentNode.SelectSingleNode("//div[contains(@class,'detail')]") ??
                                  doc.DocumentNode.SelectSingleNode("//div[contains(@class,'content')]");

                    string description = "";
                    if (mainNode != null)
                    {
                        description = System.Text.RegularExpressions.Regex.Replace(
                            mainNode.InnerText.Trim(), @"\s+", " ");
                    }

                    string companyName = companyNode != null
                        ? System.Net.WebUtility.HtmlDecode(companyNode.InnerText.Trim())
                        : "Firma Çekilemedi";

                    job.CompanyName = companyName.Length > 100 ? companyName[..100] : companyName;
                    job.Location    = locText.Length > 100 ? locText[..100] : locText;
                    job.Description = description.Length > 8000 ? description[..8000] : description;

                    db.SaveChanges();
                    updatedCount++;
                    Console.WriteLine($"   ✅ [{updatedCount}/{jobsToUpdate.Count}] {job.CompanyName} — {job.Location}");

                    await Task.Delay(300); // Sunucuya nazik ol
                }
                catch (Exception ex)
                {
                    db.ChangeTracker.Clear();
                    failCount++;
                    Console.WriteLine($"   ⚠️ Hata: {ex.Message}");
                }
            }

            Console.WriteLine($"\n   📊 Tamamlandı: {updatedCount} güncellendi, {failCount} hata.");
        }
    }
}
