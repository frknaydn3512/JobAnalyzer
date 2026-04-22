using JobAnalyzer.Data.Models;
using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace JobAnalyzer.Web.Pages.Account
{
    [EnableRateLimiting("login")]
    public class RegisterModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly PlanService _planService;

        public RegisterModel(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, PlanService planService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _planService = planService;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Ad soyad gerekli")]
            [Display(Name = "Ad Soyad")]
            public string FullName { get; set; } = "";

            [Required(ErrorMessage = "E-posta gerekli")]
            [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin")]
            public string Email { get; set; } = "";

            [Required(ErrorMessage = "Şifre gerekli")]
            [MinLength(8, ErrorMessage = "Şifre en az 8 karakter olmalı")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [Required(ErrorMessage = "Şifre tekrarı gerekli")]
            [Compare("Password", ErrorMessage = "Şifreler eşleşmiyor")]
            [DataType(DataType.Password)]
            public string ConfirmPassword { get; set; } = "";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = new AppUser
            {
                UserName = Input.Email,
                Email = Input.Email,
                FullName = Input.FullName
            };

            var result = await _userManager.CreateAsync(user, Input.Password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, "Free");
                await _planService.CreateFreeSubscriptionAsync(user.Id);
                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToPage("/Index");
            }

            ErrorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }
    }
}
