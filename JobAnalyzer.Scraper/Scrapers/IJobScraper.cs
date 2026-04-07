using System.Threading.Tasks;

namespace JobAnalyzer.Scraper.Scrapers
{
    // Bütün botlarımızın uyması gereken zorunlu kurallar (Sözleşme)
    public interface IJobScraper
    {
        string ScraperName { get; } // Botun adı (Örn: Kariyer.net Botu)
        Task RunAsync();            // Botu çalıştıracak ana metod
    }
}