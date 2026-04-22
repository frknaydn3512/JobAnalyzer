using System.Threading.Tasks;

namespace JobAnalyzer.Scraper.Scrapers
{
    // Bütün botlarýmýzýn uymasý gereken zorunlu kurallar (Sözleþme)
    public interface IJobScraper
    {
        string ScraperName { get; } // Botun adý (Örn: Kariyer.net Botu)
        Task RunAsync();            // Botu çalýþtýracak ana metod
    }
}