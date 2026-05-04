using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;

namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// WeWorkRemotely - RSS feed tabanlı (Puppeteer gerektirmez, Cloudflare sorunu yok)
    /// RSS URL: https://weworkremotely.com/categories/remote-programming-jobs.rss
    /// </summary>
    public class WeWorkRemotelyScraper : ScraperBase
    {
        public override string ScraperName => "WeWorkRemotely";

        // WWR'ın herkese açık RSS feed'leri
        private readonly (string url, string label)[] _feeds =
        {
            ("https://weworkremotely.com/categories/remote-programming-jobs.rss",         "Programming"),
            ("https://weworkremotely.com/categories/remote-devops-sysadmin-jobs.rss",     "DevOps"),
            ("https://weworkremotely.com/categories/remote-full-stack-programming-jobs.rss", "Full Stack"),
            ("https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss", "Back-End"),
            ("https://weworkremotely.com/categories/remote-front-end-programming-jobs.rss","Front-End"),
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] RSS Feed Modu Başlatıldı...");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; JobAnalyzerBot/1.0)");
            client.Timeout = TimeSpan.FromSeconds(30);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);
            var existingUrls = LoadExistingUrls(db);

            int totalAdded = 0;

            foreach (var (feedUrl, label) in _feeds)
            {
                Console.WriteLine($"\n  📡 [{label}] RSS çekiliyor...");
                try
                {
                    string xml = await client.GetStringAsync(feedUrl);
                    var rss = XDocument.Parse(xml);
                    XNamespace content = "http://purl.org/rss/1.0/modules/content/";

                    var items = rss.Descendants("item").ToList();
                    if (items.Count == 0)
                    {
                        Console.WriteLine($"  📭 Sonuç yok.");
                        continue;
                    }

                    Console.WriteLine($"  🎉 {items.Count} ilan bulundu!");
                    int feedAdded = 0;

                    foreach (var item in items)
                    {
                        // RSS link: <link> elemanı
                        string jobUrl = item.Element("link")?.Value?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(jobUrl)) continue;

                        // Başlık formatı: "Company | Job Title"
                        string rawTitle = item.Element("title")?.Value?.Trim() ?? "";
                        rawTitle = System.Net.WebUtility.HtmlDecode(rawTitle);

                        string company = "WeWorkRemotely İlanı";
                        string title = rawTitle;

                        if (rawTitle.Contains(" | "))
                        {
                            int sep = rawTitle.IndexOf(" | ");
                            company = rawTitle.Substring(0, sep).Trim();
                            title = rawTitle.Substring(sep + 3).Trim();
                        }
                        else if (rawTitle.Contains(": "))
                        {
                            // Bazen "Company: Title" formatı da kullanılıyor
                            int sep = rawTitle.IndexOf(": ");
                            company = rawTitle.Substring(0, sep).Trim();
                            title = rawTitle.Substring(sep + 2).Trim();
                        }

                        if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                        // Açıklama: <description> veya <content:encoded>
                        string rawDesc = item.Element(content + "encoded")?.Value
                                      ?? item.Element("description")?.Value
                                      ?? "";
                        string cleanDesc = System.Text.RegularExpressions.Regex.Replace(rawDesc, "<.*?>", "");
                        cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ").Trim();
                        if (cleanDesc.Length > 4000) cleanDesc = cleanDesc.Substring(0, 4000);

                        // Yayın tarihi — RSS pubDate offset içerir (+0000), DateTimeOffset ile UTC'ye çevir
                        string pubDateStr = item.Element("pubDate")?.Value ?? "";
                        DateTime datePosted = DateTimeOffset.TryParse(pubDateStr, out var dto) ? dto.UtcDateTime : DateTime.UtcNow;

                        if (!existingUrls.Add(jobUrl)) continue;

                        db.JobPostings.Add(new JobPosting
                        {
                            Title = title.Length > 100 ? title.Substring(0, 100) : title,
                            CompanyName = company.Length > 100 ? company.Substring(0, 100) : company,
                            Location = "Global / Remote",
                            Description = cleanDesc,
                            Url = jobUrl,
                            Source = ScraperName,
                            ExtractedSkills = "",
                            DateScraped = DateTime.UtcNow,
                            DatePosted = datePosted
                        });
                        feedAdded++;
                        totalAdded++;
                        Console.WriteLine($"  💾 {company} | {title.Substring(0, Math.Min(title.Length, 50))}");
                    }

                    db.SaveChanges();
                    Console.WriteLine($"  ✅ [{label}]: {feedAdded} YENİ ilan eklendi.");
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Hata ({label}): {ex.Message}");
                }
            }

            Console.WriteLine($"\n✅ [{ScraperName}] Tamamlandı! Toplam {totalAdded} YENİ ilan eklendi.");
        }
    }
}

