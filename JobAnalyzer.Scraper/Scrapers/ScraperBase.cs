namespace JobAnalyzer.Scraper.Scrapers
{
    /// <summary>
    /// Tüm scraper'ların türetileceği temel sınıf.
    /// Connection string'i merkezi olarak yönetir.
    /// </summary>
    public abstract class ScraperBase : IJobScraper
    {
        protected readonly string ConnectionString;

        private static readonly string _defaultConnectionString =
            Environment.GetEnvironmentVariable("DEFAULT_CONNECTION")
            ?? throw new InvalidOperationException("DEFAULT_CONNECTION ortam değişkeni ayarlanmamış.");

        protected ScraperBase(string? connectionString = null)
        {
            ConnectionString = connectionString ?? _defaultConnectionString;
        }

        public abstract string ScraperName { get; }
        public abstract Task RunAsync();
    }
}
