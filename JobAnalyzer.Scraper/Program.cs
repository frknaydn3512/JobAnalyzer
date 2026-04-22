using JobAnalyzer.Scraper.Scrapers;

DotNetEnv.Env.TraversePath().Load();
Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║         💼 JOB ANALYZER - SCRAPER MENÜSİ                ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine($"  🕐 {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
Console.WriteLine();
Console.WriteLine("  ── Türkiye ───────────────────────────────────────────────");
Console.WriteLine("   1. LinkedIn           (Türkiye, HttpClient guest)");
Console.WriteLine("   2. Kariyer.net        (Türkiye, Puppeteer)");
Console.WriteLine("   3. TechCareer         (techcareer.net, Puppeteer)");
Console.WriteLine();
Console.WriteLine("  ── Avrupa ────────────────────────────────────────────────");
Console.WriteLine("   4. Arbeitnow          (Avrupa, ücretsiz API)");
Console.WriteLine("   5. LandingJobs        (Avrupa startup API)");
Console.WriteLine("   6. Djinni.co          (Ukrayna/Doğu Avrupa API)");
Console.WriteLine();
Console.WriteLine("  ── API Key Gerektiren ─────────────────────────────────────");
Console.WriteLine("   7. Adzuna             (20 ülke × API key)");
Console.WriteLine("   8. Indeed             (RapidAPI)");
Console.WriteLine("   9. Jooble             (global, API key)");
Console.WriteLine("  10. Freelancer.com     (API)");
Console.WriteLine();
Console.WriteLine("  ── Ücretsiz Global Remote ────────────────────────────────");
Console.WriteLine("  11. Remotive           (~1800 ilan, ücretsiz)");
Console.WriteLine("  12. WeWorkRemotely     (5 RSS kategorisi)");
Console.WriteLine("  13. Himalayas          (~700 ilan, ücretsiz)");
Console.WriteLine("  14. WorkingNomads      (global remote API)");
Console.WriteLine("  15. RemoteOK           (global remote API)");
Console.WriteLine("  16. HackerNews         (Who's Hiring, Algolia)");
Console.WriteLine("  17. TheMuse            (ABD/Global API)");
Console.WriteLine("  18. MyCareersFuture    (Singapur resmi API)");
Console.WriteLine();
Console.WriteLine("  ── AI Analiz ─────────────────────────────────────────────");
Console.WriteLine("  19. GroqLlama3 Yetenek Analizi");
Console.WriteLine();
Console.WriteLine("  ── Toplu Çalıştır ────────────────────────────────────────");
Console.WriteLine("   0. HEPSİNİ ÇALIŞTIR  (18 aktif scraper)");
Console.WriteLine();
Console.Write("  Seçiminiz: ");

string? input = Console.ReadLine()?.Trim();

if (input == "19")
{
    var analyzer = new JobAnalyzer.Scraper.GroqAnalyzer();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { await analyzer.RunAsync(); sw.Stop(); Console.WriteLine($"\n✅ TAMAMLANDI! ⏱️ Süre: {sw.Elapsed:mm\\:ss}"); }
    catch (Exception ex) { Console.WriteLine($"\n💀 HATA: {ex.Message}"); }
    Console.WriteLine("\nDevam etmek için Enter'a bas...");
    Console.ReadLine();
    return;
}

IJobScraper? selected = input switch
{
    "1"  => new LinkedInScraper(),
    "2"  => new KariyerNetScraper(),
    "3"  => new TechcareerScraper(),
    "4"  => new ArbeitnowScraper(),
    "5"  => new LandingJobsScraper(),
    "6"  => new DjinniScraper(),
    "7"  => new AdzunaScraper(),
    "8"  => new IndeedScraper(),
    "9"  => new JoobleScraper(),
    "10" => new FreelancerComScraper(),
    "11" => new RemotiveScraper(),
    "12" => new WeWorkRemotelyScraper(),
    "13" => new HimalayasScraper(),
    "14" => new WorkingNomadsScraper(),
    "15" => new RemoteOKScraper(),
    "16" => new HackerNewsScraper(),
    "17" => new TheMuseScraper(),
    "18" => new MyCareersFutureScraper(),
    _    => null
};

if (input == "0")
{
    List<IJobScraper> allScrapers = new()
    {
        // ─── Türkiye ─────────────────────────────────────────
        new LinkedInScraper(),
        //new KariyerNetScraper(),
        new TechcareerScraper(),

        // ─── Avrupa ──────────────────────────────────────────
        new ArbeitnowScraper(),
        new LandingJobsScraper(),
        new DjinniScraper(),

        // ─── API Key Gerektiren ───────────────────────────────
        new AdzunaScraper(),
        new IndeedScraper(),
        new JoobleScraper(),
        new FreelancerComScraper(),

        // ─── Ücretsiz Global Remote ───────────────────────────
        new RemotiveScraper(),
        new WeWorkRemotelyScraper(),
        new HimalayasScraper(),
        new WorkingNomadsScraper(),
        new RemoteOKScraper(),
        new HackerNewsScraper(),
        new TheMuseScraper(),
        new MyCareersFutureScraper(),
    };

    int ok = 0, fail = 0;
    Console.WriteLine($"\n🚀 {allScrapers.Count} AKTİF SCRAPER BAŞLATILIYOR...\n");
    foreach (var s in allScrapers)
    {
        Console.WriteLine($"\n{"=",60}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await s.RunAsync(); ok++; sw.Stop(); Console.WriteLine($"  ⏱️ {sw.Elapsed:mm\\:ss}"); }
        catch (Exception ex) { fail++; Console.WriteLine($"💀 [{s.ScraperName}] HATA: {ex.Message}"); }
    }
    Console.WriteLine($"\n🏁 TAMAMLANDI! ✅ {ok} başarılı  ❌ {fail} hatalı");
}
else if (selected != null)
{
    Console.WriteLine($"\n🎯 [{selected.ScraperName}] başlatıldı...\n");
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
