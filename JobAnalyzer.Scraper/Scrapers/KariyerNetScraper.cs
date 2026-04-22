using HtmlAgilityPack;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;

namespace JobAnalyzer.Scraper.Scrapers
{
    public class KariyerNetScraper : ScraperBase
    {
        public override string ScraperName => "Kariyer.net";

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
            ("yazılım",               50),  // ~2500 ilan hedefi
            ("yazılım geliştirici",   10),
            ("backend geliştirici",    8),
            ("frontend geliştirici",   8),
            ("mobil geliştirici",      5),
            ("software developer",    10),  // İngilizce aramalar da Kariyer.net'te çalışıyor
            ("backend developer",      8),
            ("frontend developer",     8),
            ("veri bilimi",            5),
            ("devops mühendisi",       5),
        };

        public override async Task RunAsync()
        {
            Console.WriteLine($"\n🤖 [{ScraperName}] Botu Çalıştırılıyor...");

            await ShallowScrapeAsync();
            await DeepScrapeAsync();

            Console.WriteLine($"✅ [{ScraperName}] Tamamlandı!\n");
        }

        /// <summary>
        /// Cloudflare "Basılı Tut" CAPTCHA'sını otomatik çözer.
        /// Sayfada doğrulama butonu bulursa mouse ile 6 saniye basılı tutar.
        /// </summary>
        private async Task SolvePressAndHoldAsync(IPage page)
        {
            // Cloudflare Turnstile / "Basılı Tut" CAPTCHA tespiti
            string html = await page.GetContentAsync();
            bool hasCaptcha = html.Contains("Doğrulamak için basılı tutun", StringComparison.OrdinalIgnoreCase)
                          || html.Contains("press and hold", StringComparison.OrdinalIgnoreCase)
                          || html.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase)
                          || html.Contains("turnstile", StringComparison.OrdinalIgnoreCase)
                          || html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase);

            if (!hasCaptcha) return;

            // Tarayıcıyı ön plana getir — kullanıcı CAPTCHA'yı görebilsin
            try { await page.BringToFrontAsync(); } catch { }
            Console.WriteLine("   🔒 'Basılı Tut' CAPTCHA tespit edildi, çözülüyor...");

            // Olası buton selector'ları — Cloudflare farklı versiyonlar kullanabiliyor
            string[] buttonSelectors = new[]
            {
                "#challenge-stage button",
                "button[type='button']",
                ".challenge-solver button",
                "input[type='button']",
                "[data-action='managed-challenge']",
                ".cf-turnstile",
                "iframe[src*='challenges']"
            };

            for (int attempt = 0; attempt < 3; attempt++)
            {
                // Iframe içinde olabilir — önce iframe kontrol et
                bool solvedViaIframe = false;
                try
                {
                    var frames = page.Frames;
                    foreach (var frame in frames)
                    {
                        if (frame == page.MainFrame) continue;
                        try
                        {
                            var iframeBtn = await frame.QuerySelectorAsync("input[type='button'], button, .cb-i");
                            if (iframeBtn != null)
                            {
                                var box = await iframeBtn.BoundingBoxAsync();
                                if (box != null)
                                {
                                    Console.WriteLine($"   🖱️ iframe buton bulundu ({box.X},{box.Y}), 6 saniye basılı tutuluyor...");
                                    await page.Mouse.MoveAsync((decimal)(box.X + box.Width / 2), (decimal)(box.Y + box.Height / 2));
                                    await Task.Delay(200);
                                    await page.Mouse.DownAsync();
                                    await Task.Delay(6500); // 6.5 saniye basılı tut
                                    await page.Mouse.UpAsync();
                                    solvedViaIframe = true;
                                    break;
                                }
                            }
                        }
                        catch { /* iframe erişim hatası, devam et */ }
                    }
                }
                catch { /* frame listesi hatası */ }

                if (!solvedViaIframe)
                {
                    // Ana sayfada buton ara
                    foreach (var selector in buttonSelectors)
                    {
                        try
                        {
                            var btn = await page.QuerySelectorAsync(selector);
                            if (btn != null)
                            {
                                var box = await btn.BoundingBoxAsync();
                                if (box != null && box.Width > 10 && box.Height > 10)
                                {
                                    Console.WriteLine($"   🖱️ Buton bulundu ({selector}), 6 saniye basılı tutuluyor...");
                                    await page.Mouse.MoveAsync((decimal)(box.X + box.Width / 2), (decimal)(box.Y + box.Height / 2));
                                    await Task.Delay(200);
                                    await page.Mouse.DownAsync();
                                    await Task.Delay(6500);
                                    await page.Mouse.UpAsync();
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Başarılı mı kontrol et
                await Task.Delay(3000);
                html = await page.GetContentAsync();
                if (html.Contains("k-ad-card") || html.Contains("is-ilanlari"))
                {
                    Console.WriteLine("   ✅ CAPTCHA başarıyla çözüldü!");
                    return;
                }

                Console.WriteLine($"   ⏳ Deneme {attempt + 1}/3 — henüz çözülemedi, tekrar deneniyor...");
                await Task.Delay(2000);
            }

            Console.WriteLine();
            Console.WriteLine("   ⚠️  CAPTCHA otomatik çözülemedi!");
            Console.WriteLine("   👉  Tarayıcı penceresine geçip CAPTCHA'yı manuel olarak çözün.");
            Console.WriteLine("   ⏎   Çözdükten sonra ENTER'a basın...");
            Console.ReadLine();

            // Kullanıcı Enter'a bastı, sayfa durumunu kontrol et
            html = await page.GetContentAsync();
            if (html.Contains("k-ad-card") || html.Contains("is-ilanlari") || html.Contains("is-ilani"))
            {
                Console.WriteLine("   ✅ CAPTCHA manuel olarak çözüldü!");
            }
            else
            {
                Console.WriteLine("   ⚠️  Sayfa hâlâ hazır değil, devam ediliyor...");
            }
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
                Args = new[] {
                    "--disable-blink-features=AutomationControlled",
                    "--window-size=1920,1080",
                    "--disable-features=IsolateOrigins,site-per-process"
                }
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            bool shallowBrowserClosed = false;
            browser.Disconnected += (_, _) =>
            {
                shallowBrowserClosed = true;
                Console.WriteLine();
                Console.WriteLine("  ⚠️  TARAYICI PENCERESİ KAPANDI!");
                Console.WriteLine("  👉  Tarayıcıyı yeniden açın, sayfaya bir kez tıklayın.");
                Console.WriteLine("  ⏎   Hazır olduğunuzda Enter'a basın...");
                Console.ReadLine();
                shallowBrowserClosed = false;
            };

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            int totalAdded = 0;

            foreach (var (keyword, maxPages) in _searches)
            {
                Console.WriteLine($"\n🔍 '{keyword}' aranıyor (max {maxPages} sayfa)...");

                for (int cp = 1; cp <= maxPages; cp++)
                {
                    if (shallowBrowserClosed) break;
                    // DOĞRU URL FORMATI: cp= parametresi (pg= değil!)
                    string encodedKw = Uri.EscapeDataString(keyword);
                    string targetUrl = $"https://www.kariyer.net/is-ilanlari?kw={encodedKw}&cp={cp}";

                    try
                    {
                        await page.GoToAsync(targetUrl, new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                            Timeout = 60000
                        });

                        // "Basılı Tut" CAPTCHA varsa otomatik çöz
                        await SolvePressAndHoldAsync(page);

                        // Popup / cookie kapat — her sayfada dene
                        await Task.Delay(1500);
                        await page.Keyboard.PressAsync("Escape"); // Popup kapat
                        await Task.Delay(500);
                        await page.EvaluateFunctionAsync(@"() => {
                            // Cookie banner
                            let cookieBtn = document.querySelector('button.kabul-et, [data-test=""accept""], button[class*=""cookie""]');
                            if (cookieBtn) cookieBtn.click();
                            // Popup X butonu — çeşitli selector'lar dene
                            let closeBtn = document.querySelector(
                                'button[aria-label=""Kapat""], button[aria-label=""Close""], ' +
                                '.modal-close, [class*=""close-btn""], [class*=""closeBtn""], ' +
                                'svg[class*=""close""], button svg'
                            );
                            if (closeBtn) {
                                let btn = closeBtn.closest('button') || closeBtn;
                                btn.click();
                            }
                            // Overlay varsa kaldır
                            let overlay = document.querySelector('[class*=""overlay""], [class*=""backdrop""]');
                            if (overlay) overlay.remove();
                        }");
                        await Task.Delay(1000);

                        // Sayfa boyunca scroll — lazy load ilanları yükle
                        await page.EvaluateFunctionAsync(@"async () => {
                            window.scrollTo(0, document.body.scrollHeight / 2);
                            await new Promise(r => setTimeout(r, 600));
                            window.scrollTo(0, document.body.scrollHeight);
                            await new Promise(r => setTimeout(r, 600));
                        }");
                        await Task.Delay(1200);

                        // İş kartlarının yüklenmesini bekle (max 20sn)
                        string htmlContent = "";
                        for (int i = 0; i < 10; i++)
                        {
                            htmlContent = await page.GetContentAsync();
                            // Gerçek ilan varsa: sayısal ID içeren /is-ilanlari/ linki
                            bool hasRealJobs = System.Text.RegularExpressions.Regex.IsMatch(
                                htmlContent, @"/is-ilan[a-z]*/[^""']+\d{5,}");
                            if (hasRealJobs) break;
                            await SolvePressAndHoldAsync(page);
                            await Task.Delay(2000);
                        }

                        HtmlDocument document = new HtmlDocument();
                        document.LoadHtml(htmlContent);

                        // DEBUG: İlk sayfada tüm linkleri göster
                        if (cp == 1)
                        {
                            var allLinks = document.DocumentNode.SelectNodes("//a[@href]");
                            if (allLinks != null)
                            {
                                var sample = allLinks
                                    .Select(n => n.GetAttributeValue("href", ""))
                                    .Where(h => h.Contains("ilani") || h.Contains("ilan"))
                                    .Distinct().Take(20).ToList();
                                //Console.WriteLine($"   🔍 'ilan' içeren örnek linkler:");
                                //foreach (var s in sample) Console.WriteLine($"      {s}");
                            }
                        }

                        // Kariyer.net tekil ilan URL formatı: /is-ilani/{slug}-{ID}
                        // Örnek: /is-ilani/sbt-saglik-yazilim-muhendisi-4416867
                        var jobLinks = document.DocumentNode.SelectNodes(
                            "//a[contains(@href, '/is-ilani/')]"
                        );

                        // Sadece sayısal ID içerenleri al (kategori linklerini ele)
                        var filteredLinks = jobLinks?
                            .Where(n => System.Text.RegularExpressions.Regex.IsMatch(
                                n.GetAttributeValue("href", ""), @"\d{5,}"))
                            .ToList();

                        if (filteredLinks == null || filteredLinks.Count == 0)
                        {
                            Console.WriteLine($"   📭 Sayfa {cp}: Geçerli ilan linki bulunamadı → Son sayfa.");
                            break;
                        }

                        Console.WriteLine($"   🔗 {filteredLinks.Count} ilan linki bulundu.");

                        HashSet<string> seenUrls = new();
                        int pageAdded = 0, skippedDuplicate = 0, skippedIrrelevant = 0;

                        foreach (var link in filteredLinks)
                        {
                            string href = link.GetAttributeValue("href", "");
                            if (string.IsNullOrWhiteSpace(href)) continue;

                            // Tracking parametrelerini temizle
                            int qIdx = href.IndexOf('?');
                            if (qIdx > 0) href = href.Substring(0, qIdx);

                            string jobUrl = href.StartsWith("http")
                                ? href
                                : "https://www.kariyer.net" + href;

                            if (seenUrls.Contains(jobUrl)) continue;
                            seenUrls.Add(jobUrl);

                            // Önce link içindeki başlık elementini ara
                            string title = "";
                            var titleNode = link.SelectSingleNode(".//h3 | .//h2 | .//span[contains(@class,'title')] | .//p[contains(@class,'title')]");
                            if (titleNode != null)
                                title = System.Net.WebUtility.HtmlDecode(titleNode.InnerText.Trim());

                            // Yoksa tüm InnerText (ilk satır)
                            if (string.IsNullOrWhiteSpace(title))
                            {
                                var lines = link.InnerText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(l => l.Trim()).Where(l => l.Length > 2).ToList();
                                title = lines.FirstOrDefault() ?? "";
                            }

                            // Hâlâ yoksa URL slug'dan türet
                            if (string.IsNullOrWhiteSpace(title) || title.Length < 3)
                            {
                                var parts = jobUrl.TrimEnd('/').Split('/');
                                title = System.Text.RegularExpressions.Regex.Replace(
                                    parts.Last(), @"-\d+$", "").Replace("-", " ").Trim();
                            }

                            title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
                            if (title.Length > 100) title = title.Substring(0, 100);
                            if (string.IsNullOrWhiteSpace(title) || title.Length < 3) continue;

                            if (title.Contains("Sponsorlu", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Tümünü Gör", StringComparison.OrdinalIgnoreCase)) continue;

                            // ==========================================
                            // BURASI YENİ EKLENEN FİLTRELEME KONTROLÜ
                            // ==========================================
                            if (!IsSoftwareRelated(title))
                            {
                                skippedIrrelevant++;
                                continue;
                            }
                            // ==========================================

                            if (db.JobPostings.Any(j => j.Url == jobUrl))
                            { skippedDuplicate++; continue; }

                            db.JobPostings.Add(new JobPosting
                            {
                                Title = title,
                                CompanyName = "Daha Sonra Çekilecek",
                                Location = "Türkiye",
                                Description = "",
                                Url = jobUrl,
                                Source = ScraperName,
                                ExtractedSkills = "",
                                DateScraped = DateTime.UtcNow,
                                DatePosted = DateTime.UtcNow
                            });

                            pageAdded++;
                            totalAdded++;
                            Console.WriteLine($"   💾 {title}");
                        }

                        if (skippedDuplicate > 0)
                            Console.WriteLine($"   ℹ️ {skippedDuplicate} ilan zaten DB'de.");
                        if (skippedIrrelevant > 0)
                            Console.WriteLine($"   🗑️ {skippedIrrelevant} alakasız ilan (örn: Muhasebe Uzmanı) filtrelendi.");

                        db.SaveChanges();
                        Console.WriteLine($"   ✅ Sayfa {cp}: {pageAdded} yeni ilan eklendi.");

                        // Sayfa geçişlerinde nazik bekleme
                        await Task.Delay(new Random().Next(1500, 2500));
                    }
                    catch (Exception ex) when (ex.Message.Contains("Target closed") ||
                                                   ex.Message.Contains("Session closed") ||
                                                   ex.Message.Contains("Connection is closed") ||
                                                   ex.Message.Contains("Target.createTarget"))
                    {
                        Console.WriteLine($"   ⚠️  Tarayıcı bağlantısı kesildi (Sayfa {cp}). Pencere açık mı kontrol edin.");
                        await Task.Delay(3000);
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
            Console.WriteLine("\n>>> Aşama 2: Detaylar (Şirket/Şehir/Açıklama) HIZLANDIRILMIŞ MODDA Çekiliyor...");
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);

            List<JobPosting> jobsToUpdate;
            using (var db = new AppDbContext(optionsBuilder.Options))
            {
                jobsToUpdate = db.JobPostings
                    .AsNoTracking()
                    .Where(j => j.CompanyName == "Daha Sonra Çekilecek" && j.Source == ScraperName)
                    .ToList();
            }

            if (jobsToUpdate.Count == 0)
            {
                Console.WriteLine("   ℹ️ Detaylandırılacak ilan yok.");
                return;
            }

            Console.WriteLine($"   🔄 {jobsToUpdate.Count} ilanın detayı paralel olarak çekilecek...");

            var launchOptions = new LaunchOptions
            {
                Headless = false, 
                DefaultViewport = null,
                Args = new[] {
                    "--disable-blink-features=AutomationControlled",
                    "--disable-features=IsolateOrigins,site-per-process"
                }
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);

            browser.Disconnected += (_, _) =>
            {
                Console.WriteLine();
                Console.WriteLine("  ⚠️  TARAYICI PENCERESİ KAPANDI! (Detay aşaması)");
                Console.WriteLine("  👉  Tarayıcıyı yeniden açın, sayfaya bir kez tıklayın.");
                Console.WriteLine("  ⏎   Hazır olduğunuzda Enter'a basın...");
                Console.ReadLine();
            };

            var scrapedData = new List<JobPosting>();
            int processedCount = 0;
            int totalJobs = jobsToUpdate.Count;

            // Sıralı işleme: CAPTCHA çıkınca kullanıcı müdahalesi gerektiğinden
            // paralel tab açmak yerine tek tab sırayla geziyor.
            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            await page.SetRequestInterceptionAsync(true);
            page.Request += (sender, e) =>
            {
                var resType = e.Request.ResourceType;
                if (resType == ResourceType.Image || resType == ResourceType.StyleSheet || resType == ResourceType.Font || resType == ResourceType.Media)
                    e.Request.AbortAsync();
                else
                    e.Request.ContinueAsync();
            };

            foreach (var job in jobsToUpdate)
            {
                try
                {
                    await page.GoToAsync(job.Url, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                        Timeout = 40000
                    });

                    await SolvePressAndHoldAsync(page);
                    await Task.Delay(800);

                    string htmlContent = await page.GetContentAsync();

                    if (htmlContent.Contains("challenge-platform") || htmlContent.Contains("basılı tutun"))
                    {
                        string shortTitle = (job.Title ?? "")[..Math.Min((job.Title ?? "").Length, 20)];
                        Console.WriteLine($"   ⚠️ Cloudflare aşılamadı — atlanıyor ({shortTitle}...)");
                        continue;
                    }

                    HtmlDocument detailDoc = new HtmlDocument();
                    detailDoc.LoadHtml(htmlContent);

                    var companyNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//a[contains(@class,'company-name')] | //div[contains(@class,'company-name')] | //span[contains(@class,'company-name')] | //h2[contains(@class,'company')]"
                    );
                    var locationNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//li[contains(@class,'location')] | //span[contains(@class,'location')] | //span[contains(@class,'city')]"
                    );
                    var descNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//div[contains(@class,'job-detail-content')] | //div[contains(@class,'description')] | //section[contains(@class,'detail')]"
                    );

                    if (companyNode != null)
                        job.CompanyName = companyNode.InnerText.Trim()[..Math.Min(companyNode.InnerText.Trim().Length, 100)];
                    else
                        job.CompanyName = "Gizli Firma";

                    if (locationNode != null)
                    {
                        string locText = locationNode.InnerText.Trim().Replace("Şehir", "").Trim();
                        job.Location = locText.Length > 100 ? locText[..100] : locText;
                    }

                    if (descNode != null)
                        job.Description = System.Text.RegularExpressions.Regex.Replace(descNode.InnerText.Trim(), @"\s+", " ");

                    scrapedData.Add(job);

                    processedCount++;
                    Console.WriteLine($"   [{processedCount}/{totalJobs}] ✅ {job.CompanyName} | {(job.Title ?? "")[..Math.Min((job.Title ?? "").Length, 35)]}");

                    await Task.Delay(new Random().Next(800, 1500));
                }
                catch (Exception ex) when (ex.Message.Contains("Target closed") ||
                                               ex.Message.Contains("Session closed") ||
                                               ex.Message.Contains("Connection is closed"))
                {
                    string shortTitle = (job.Title ?? "")[..Math.Min((job.Title ?? "").Length, 20)];
                    Console.WriteLine($"   ⚠️  Tarayıcı bağlantısı kesildi ({shortTitle}...) — pencere kapandı.");
                    break; // Tarayıcı kapandı, döngüden çık
                }
                catch (Exception ex)
                {
                    string shortTitle = (job.Title ?? "")[..Math.Min((job.Title ?? "").Length, 20)];
                    Console.WriteLine($"   ⚠️ Atlandı ({shortTitle}...): {ex.Message}");
                }
            }

            Console.WriteLine("\n   💾 Başarıyla çekilen ilanlar veritabanına güncelleniyor...");
            using (var dbBatch = new AppDbContext(optionsBuilder.Options))
            {
                dbBatch.JobPostings.UpdateRange(scrapedData);
                await dbBatch.SaveChangesAsync();
            }

            Console.WriteLine($"   🚀 Kariyer.net detay çekme işlemi tamamlandı!");
        }
    }
}