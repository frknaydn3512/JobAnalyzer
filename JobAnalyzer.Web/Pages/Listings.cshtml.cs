using System.ComponentModel.DataAnnotations;
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

        public ListingsModel(AppDbContext context) => _context = context;

        [BindProperty(SupportsGet = true), StringLength(200)] public string? SearchTerm { get; set; }
        [BindProperty(SupportsGet = true), StringLength(50)] public string? JobTypeFilter { get; set; }
        [BindProperty(SupportsGet = true), StringLength(50)] public string? SourceFilter { get; set; }
        [BindProperty(SupportsGet = true), StringLength(100)] public string? LocationFilter { get; set; }
        [BindProperty(SupportsGet = true)] public string? ViewMode { get; set; } = "table";
        [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;

        public List<JobPosting> Jobs { get; set; } = new();
        public int TotalPages { get; set; }
        public int TotalItems { get; set; }
        public int TotalSirketCount { get; set; }
        public int TotalFreelanceCount { get; set; }
        public List<string> AvailableSources { get; set; } = new();
        private const int PageSize = 25;

        public async Task OnGetAsync()
        {
            if (!ModelState.IsValid) return;

            AvailableSources = await _context.JobPostings
                .Where(j => j.Source != null)
                .Select(j => j.Source!)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            var query = _context.JobPostings.AsQueryable();

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.ToLower();
                query = query.Where(j =>
                    (j.Title != null && j.Title.ToLower().Contains(term)) ||
                    (j.ExtractedSkills != null && j.ExtractedSkills.ToLower().Contains(term)) ||
                    (j.CompanyName != null && j.CompanyName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(JobTypeFilter))
            {
                if (JobTypeFilter == "Freelance İlanı")
                    query = query.Where(j => j.JobType == "Freelance İlanı");
                else if (JobTypeFilter == "Şirket İlanı")
                    query = query.Where(j => j.JobType != "Freelance İlanı");
            }

            if (!string.IsNullOrWhiteSpace(SourceFilter))
                query = query.Where(j => j.Source == SourceFilter);

            if (!string.IsNullOrWhiteSpace(LocationFilter))
            {
                if (LocationFilter == "Remote")
                    query = query.Where(j => j.Location != null && (j.Location.ToLower().Contains("remote") || j.Location.ToLower().Contains("uzaktan")));
                else if (LocationFilter == "Hybrid")
                    query = query.Where(j => j.Location != null && (j.Location.ToLower().Contains("hybrid") || j.Location.ToLower().Contains("hibrit")));
                else
                    query = query.Where(j => j.Location != null && j.Location.ToLower().Contains(LocationFilter.ToLower()));
            }

            TotalItems = await query.CountAsync();
            TotalSirketCount = await query.CountAsync(j => j.JobType != "Freelance İlanı");
            TotalFreelanceCount = await query.CountAsync(j => j.JobType == "Freelance İlanı");
            TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);

            if (P < 1) P = 1;
            if (P > TotalPages && TotalPages > 0) P = TotalPages;

            Jobs = await query
                .OrderByDescending(j => j.DateScraped)
                .Skip((P - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }
    }
}
