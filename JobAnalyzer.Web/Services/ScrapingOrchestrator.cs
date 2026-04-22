using JobAnalyzer.Data;
using JobAnalyzer.Scraper.Scrapers;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Web.Services
{
    /// <summary>
    /// Hangfire tarafından her gece 03:00'da çağrılan otomasyon servisi.
    /// API tabanlı scraper'ları çalıştırır, eski ilanları temizler, GroqAnalyzer'ı başlatır.
    /// </summary>
    public class ScrapingOrchestrator
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ScrapingOrchestrator> _logger;

        public ScrapingOrchestrator(IConfiguration config, ILogger<ScrapingOrchestrator> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task RunFullCycleAsync()
        {
            _logger.LogInformation("🚀 Otomatik scraping döngüsü başladı: {Time}", DateTime.Now);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            string connectionString =
                Environment.GetEnvironmentVariable("DEFAULT_CONNECTION")
                ?? _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Veritabanı bağlantı dizesi bulunamadı.");

            // ── 1. Eski ilanları temizle ────────────────────────────────────
            int cleanupDays = _config.GetValue<int>("ScrapeSchedule:CleanupDays", 30);
            int deleted = await CleanupOldJobsAsync(connectionString, cleanupDays);
            _logger.LogInformation("🗑️ {Deleted} eski ilan silindi ({Days} gün)", deleted, cleanupDays);

            // ── 2. API tabanlı scraper'ları çalıştır ────────────────────────
            // Puppeteer scraper'lar (KariyerNet, TechCareer) otomasyona dahil değil
            var scrapers = new List<IJobScraper>
            {
                // Türkiye
                new LinkedInScraper(),

                // Avrupa
                new ArbeitnowScraper(),
                new LandingJobsScraper(),
                new DjinniScraper(),

                // API key gerekli (env var yoksa sessizce atlanır)
                new AdzunaScraper(),
                new IndeedScraper(),
                new JoobleScraper(),
                new FreelancerComScraper(),

                // Ücretsiz Global Remote
                new RemotiveScraper(),
                new WeWorkRemotelyScraper(),
                new HimalayasScraper(),
                new WorkingNomadsScraper(),
                new RemoteOKScraper(),
                new HackerNewsScraper(),
                new TheMuseScraper(),
                new MyCareersFutureScraper(),
            };

            foreach (var scraper in scrapers)
            {
                try
                {
                    _logger.LogInformation("▶️ {ScraperName} başlatıldı", scraper.ScraperName);
                    await scraper.RunAsync();
                    _logger.LogInformation("✅ {ScraperName} tamamlandı", scraper.ScraperName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ {ScraperName} hatası", scraper.ScraperName);
                }
            }

            // ── 3. GroqAnalyzer — analiz edilmemiş ilanları işle ───────────
            try
            {
                _logger.LogInformation("🧠 GroqAnalyzer başlatıldı");
                var analyzer = new JobAnalyzer.Scraper.GroqAnalyzer();
                await analyzer.RunAsync();
                _logger.LogInformation("✅ GroqAnalyzer tamamlandı");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ GroqAnalyzer hatası");
            }

            sw.Stop();
            _logger.LogInformation("🏁 Döngü tamamlandı. Süre: {Elapsed:mm\\:ss}", sw.Elapsed);
        }

        private async Task<int> CleanupOldJobsAsync(string connectionString, int days)
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseNpgsql(connectionString);
                using var db = new AppDbContext(optionsBuilder.Options);

                var cutoff = DateTime.UtcNow.AddDays(-days);
                var oldJobs = db.JobPostings.Where(j => j.DateScraped < cutoff);
                int count = await oldJobs.CountAsync();
                db.JobPostings.RemoveRange(oldJobs);
                await db.SaveChangesAsync();
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup hatası");
                return 0;
            }
        }
    }
}
