using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using JobAnalyzer.Data; 
using JobAnalyzer.Data.Models; 

namespace JobAnalyzer.Web.Pages
{
    public class ListingsModel : PageModel
    {
        private readonly AppDbContext _context;

        public ListingsModel(AppDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        /// <summary>Filtre: "" = Hepsi | "Şirket İlanı" | "Freelance İlanı"</summary>
        [BindProperty(SupportsGet = true)]
        public string? JobTypeFilter { get; set; }

        public List<JobPosting> Jobs { get; set; } = new List<JobPosting>();

        [BindProperty(SupportsGet = true)]
        public int P { get; set; } = 1;
        public int TotalPages { get; set; }
        public int TotalItems { get; set; }
        public int TotalSirketCount { get; set; }
        public int TotalFreelanceCount { get; set; }
        private const int PageSize = 25;

        public async Task OnGetAsync()
        {
            var query = _context.JobPostings.AsQueryable();

            // Metin araması
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(j =>
                    (j.Title != null && j.Title.ToLower().Contains(term)) ||
                    (j.ExtractedSkills != null && j.ExtractedSkills.ToLower().Contains(term)) ||
                    (j.CompanyName != null && j.CompanyName.ToLower().Contains(term)));
            }

            // İlan türü filtresi
            if (!string.IsNullOrWhiteSpace(JobTypeFilter))
            {
                if (JobTypeFilter == "Freelance İlanı")
                    query = query.Where(j => j.JobType == "Freelance İlanı");
                else if (JobTypeFilter == "Şirket İlanı")
                    query = query.Where(j => j.JobType != "Freelance İlanı");
            }

            TotalItems = await query.CountAsync();
            TotalSirketCount = await query.CountAsync(j => j.JobType != "Freelance İlanı");
            TotalFreelanceCount = await query.CountAsync(j => j.JobType == "Freelance İlanı");
            
            TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
            
            if (P < 1) P = 1;
            if (P > TotalPages && TotalPages > 0) P = TotalPages;

            // Sayfalama uygula
            Jobs = await query
                .OrderByDescending(j => j.DateScraped)
                .Skip((P - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }
    }
}