using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace JobAnalyzer.Web.Pages.Account
{
    [EnableRateLimiting("login")]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;

        public LoginModel(SignInManager<AppUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "E-posta gerekli")]
            [EmailAddress]
            public string Email { get; set; } = "";

            [Required(ErrorMessage = "Şifre gerekli")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [Display(Name = "Beni hatırla")]
            public bool RememberMe { get; set; }
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            var result = await _signInManager.PasswordSignInAsync(
                Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
                return LocalRedirect(returnUrl ?? "/");

            if (result.IsLockedOut)
            {
                ErrorMessage = "Çok fazla başarısız deneme. Hesabınız 15 dakika kilitlendi.";
                return Page();
            }

            ErrorMessage = "E-posta veya şifre hatalı.";
            return Page();
        }
    }
}
