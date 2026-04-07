using JobAnalyzer.Scraper.Scrapers;

DotNetEnv.Env.TraversePath().Load();
Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║     💼 JOB ANALYZER - SCRAPER MENÜSİ        ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine($"  🕐 {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
Console.WriteLine();
Console.WriteLine("  ── API Tabanlı (Hızlı, tarayıcı açmaz) ─────");
Console.WriteLine("   1. Remotive         (10 kategori × 200 ilan)");
Console.WriteLine("   2. RemoteOK         (ücretsiz JSON API)");
Console.WriteLine("   3. Himalayas        (8 kategori × 100 ilan)");
Console.WriteLine("   4. Adzuna           (7 ülke × çoklu sayfa)");
Console.WriteLine();
Console.WriteLine("  ── Puppeteer Tabanlı (Tarayıcı açar) ────────");
Console.WriteLine("   5. TechCareer       (techcareer.net)");
Console.WriteLine("   6. Kariyer.net      (916+ ilan, 20 sayfa)");
Console.WriteLine("   7. Yenibiris        (yenibiris.com)");
Console.WriteLine("   8. SecretCV         (secretcv.com)");
Console.WriteLine("   9. WeWorkRemotely   (weworkremotely.com)");
Console.WriteLine("  10. LinkedIn         (24 keyword × ~60 ilan)");
Console.WriteLine("  11. Indeed           (indeed.com)");
Console.WriteLine();
Console.WriteLine("  ── Freelance ─────────────────────────────────");
Console.WriteLine("  12. Freelancer.com   (API tabanlı)");
Console.WriteLine("  13. Bionluk          (bionluk.com)");
Console.WriteLine();
Console.WriteLine("  ── AI Analiz Aracı ───────────────────────────");
Console.WriteLine("  14. GroqLlama3 Yetenek Analizi");
Console.WriteLine();
Console.WriteLine("  ── Toplu Çalıştır ────────────────────────────");
Console.WriteLine("   0. HEPSİNİ ÇALIŞTIR");
Console.WriteLine();
Console.Write("  Seçiminiz: ");

string? input = Console.ReadLine()?.Trim();

if (input == "14")
{
    var analyzer = new JobAnalyzer.Scraper.GroqAnalyzer();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { await analyzer.RunAsync(); sw.Stop(); Console.WriteLine($"\n✅ TAMAMLANDI! ⏱️ Süre: {sw.Elapsed:mm\\:ss}"); }
    catch (Exception ex) { Console.WriteLine($"\n💀 HATA: {ex.Message}"); }
    
    Console.WriteLine("\nDevam etmek için Enter'a bas...");
    Console.ReadLine();
    return; // Programdan tamamen çık, veya istersen ana menü de dönebilir.
}

IJobScraper? selected = input switch
{
    "1"  => new RemotiveScraper(),
    "2"  => new RemoteOKScraper(),
    "3"  => new HimalayasScraper(),
    "4"  => new AdzunaScraper(),
    "5"  => new TechcareerScraper(),
    "6"  => new KariyerNetScraper(),
    "7"  => new YenibirisComScraper(),
    "8"  => new SecretCVScraper(),
    "9"  => new WeWorkRemotelyScraper(),
    "10" => new LinkedInScraper(),
    "11" => new IndeedScraper(),
    "12" => new FreelancerComScraper(),
    "13" => new BionlukScraper(),
    _    => null
};

if (input == "0")
{
    List<IJobScraper> allScrapers = new()
    {
        // ─── API TABANLI (önce bunlar — hızlı) ────────────
        new RemotiveScraper(),       // 10 kategori × 200 = 2000+
        new RemoteOKScraper(),       // ~150 remote
        new HimalayasScraper(),      // 8 kategori × 100 = 800+
        new AdzunaScraper(),         // 7 ülke × 3 keyword × 4 sayfa = 800+
        new FreelancerComScraper(),  // API

        // ─── PUPPETEER TABANLI ─────────────────────────────
        new TechcareerScraper(),
        new KariyerNetScraper(),     // 916+ ilan, 20 sayfa
        new YenibirisComScraper(),
        new SecretCVScraper(),
        new WeWorkRemotelyScraper(),
        new LinkedInScraper(),       // 24 keyword × ~60 = 1440
        new IndeedScraper(),

        // ─── FREELANCE ─────────────────────────────────────
        new BionlukScraper(),
    };

    int ok = 0, fail = 0;
    Console.WriteLine("\n🚀 TÜM SCRAPERLAR BAŞLATILIYOR...\n");
    foreach (var s in allScrapers)
    {
        Console.WriteLine($"\n{'=',46}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await s.RunAsync(); ok++; sw.Stop(); Console.WriteLine($"  ⏱️ {sw.Elapsed:mm\\:ss}"); }
        catch (Exception ex) { fail++; Console.WriteLine($"💀 [{s.ScraperName}] HATA: {ex.Message}"); }
    }
    Console.WriteLine($"\n🏁 TAMAMLANDI! ✅ {ok} başarılı  ❌ {fail} hatalı");
}
else if (selected != null)
{
    Console.WriteLine($"\n🎯 [{selected.ScraperName}] test ediliyor...\n");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { await selected.RunAsync(); sw.Stop(); Console.WriteLine($"\n✅ TAMAMLANDI! ⏱️ Süre: {sw.Elapsed:mm\\:ss}"); }
    catch (Exception ex) { Console.WriteLine($"\n💀 HATA: {ex.Message}"); }
}
else
{
    Console.WriteLine("\n⚠️ Geçersiz seçim.");
}

Console.WriteLine("\nDevam etmek için Enter'a bas...");
Console.ReadLine();