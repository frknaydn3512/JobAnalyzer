using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace JobAnalyzer.Web.Pages.Account
{
    [Authorize]
    public class AlertsModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;
        private readonly PlanService _planService;

        public AlertsModel(AppDbContext db, UserManager<AppUser> userManager, PlanService planService)
        {
            _db = db;
            _userManager = userManager;
            _planService = planService;
        }

        public List<SavedSearch> Alerts { get; set; } = new();
        public string? SuccessMessage { get; set; }
        public string? LimitMessage { get; set; }

        [BindProperty]
        public NewAlertInput NewAlert { get; set; } = new();

        public class NewAlertInput
        {
            [Required(ErrorMessage = "Arama kelimesi gerekli")]
            [MaxLength(100)]
            public string Keyword { get; set; } = "";
            public string? LocationFilter { get; set; }
            public string Frequency { get; set; } = "daily";
            public bool EmailEnabled { get; set; } = true;
        }

        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User)!;
            Alerts = await _db.SavedSearches
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            var userId = _userManager.GetUserId(User)!;

            var (allowed, reason) = await _planService.CanSaveSearchAsync(userId);
            if (!allowed)
            {
                LimitMessage = reason;
                await OnGetAsync();
                return Page();
            }

            _db.SavedSearches.Add(new SavedSearch
            {
                UserId = userId,
                Keyword = NewAlert.Keyword,
                LocationFilter = NewAlert.LocationFilter,
                Frequency = NewAlert.Frequency,
                EmailEnabled = NewAlert.EmailEnabled,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            SuccessMessage = $"Alert oluşturuldu: \"{NewAlert.Keyword}\"";
            NewAlert = new();
            await OnGetAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int alertId)
        {
            var userId = _userManager.GetUserId(User)!;
            var alert = await _db.SavedSearches
                .FirstOrDefaultAsync(s => s.Id == alertId && s.UserId == userId);

            if (alert != null)
            {
                _db.SavedSearches.Remove(alert);
                await _db.SaveChangesAsync();
            }

            return RedirectToPage();
        }
    }
}
