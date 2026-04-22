using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace JobAnalyzer.Web.Pages.Account
{
    [Authorize]
    public class SavedJobsModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;

        public SavedJobsModel(AppDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public List<SavedJob> SavedJobs { get; set; } = new();

        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User)!;
            SavedJobs = await _db.SavedJobs
                .Include(s => s.JobPosting)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.SavedAt)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostRemoveAsync(int jobId)
        {
            var userId = _userManager.GetUserId(User)!;
            var saved = await _db.SavedJobs
                .FirstOrDefaultAsync(s => s.UserId == userId && s.JobPostingId == jobId);

            if (saved != null)
            {
                _db.SavedJobs.Remove(saved);
                await _db.SaveChangesAsync();
            }

            return RedirectToPage();
        }
    }
}
