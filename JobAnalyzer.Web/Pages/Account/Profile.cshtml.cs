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
    public class ProfileModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly AppDbContext _db;

        public ProfileModel(UserManager<AppUser> userManager, AppDbContext db)
        {
            _userManager = userManager;
            _db = db;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? Email { get; set; }
        public string? Plan { get; set; }
        public string? SuccessMessage { get; set; }
        public string? LastAnalysis { get; set; }
        public DateTime? LastAnalyzedAt { get; set; }

        public class InputModel
        {
            public string? FullName { get; set; }
            public string? Level { get; set; }
            public string? TargetLocation { get; set; }
            public int? ExpectedMinSalary { get; set; }
            public int? ExpectedMaxSalary { get; set; }
            public string? Skills { get; set; }
            public string? PreferredJobType { get; set; }
        }

        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            Email = user.Email;
            var roles = await _userManager.GetRolesAsync(user);
            Plan = roles.Contains("Admin") ? "Admin" : roles.Contains("Pro") ? "Pro" : "Free";

            var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile != null)
            {
                Input.FullName = user.FullName;
                Input.Level = profile.Level;
                Input.TargetLocation = profile.TargetLocation;
                Input.ExpectedMinSalary = profile.ExpectedMinSalary;
                Input.ExpectedMaxSalary = profile.ExpectedMaxSalary;
                Input.Skills = profile.ExtractedSkills;
                Input.PreferredJobType = profile.PreferredJobType;
                LastAnalysis = profile.LastCvAnalysis;
                LastAnalyzedAt = profile.LastAnalyzedAt;
            }
            else
            {
                Input.FullName = user.FullName;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Account/Login");

            user.FullName = Input.FullName;
            await _userManager.UpdateAsync(user);

            var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile == null)
            {
                profile = new UserProfile { UserId = user.Id };
                _db.UserProfiles.Add(profile);
            }

            profile.Level = Input.Level;
            profile.TargetLocation = Input.TargetLocation;
            profile.ExpectedMinSalary = Input.ExpectedMinSalary;
            profile.ExpectedMaxSalary = Input.ExpectedMaxSalary;
            profile.ExtractedSkills = Input.Skills;
            profile.PreferredJobType = Input.PreferredJobType;

            await _db.SaveChangesAsync();

            Email = user.Email;
            var roles = await _userManager.GetRolesAsync(user);
            Plan = roles.Contains("Admin") ? "Admin" : roles.Contains("Pro") ? "Pro" : "Free";
            SuccessMessage = "Profil güncellendi.";
            return Page();
        }
    }
}
