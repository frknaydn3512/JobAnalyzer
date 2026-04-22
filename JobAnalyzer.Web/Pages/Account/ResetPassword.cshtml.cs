using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace JobAnalyzer.Web.Pages.Account
{
    [EnableRateLimiting("login")]
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;

        public ResetPasswordModel(UserManager<AppUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool ResetSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = "";

            [Required]
            public string Token { get; set; } = "";

            [Required(ErrorMessage = "Yeni şifre gerekli")]
            [MinLength(8, ErrorMessage = "Şifre en az 8 karakter olmalı")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [Required(ErrorMessage = "Şifre tekrarı gerekli")]
            [Compare("Password", ErrorMessage = "Şifreler eşleşmiyor")]
            [DataType(DataType.Password)]
            public string ConfirmPassword { get; set; } = "";
        }

        public void OnGet(string? email, string? token)
        {
            Input.Email = email ?? "";
            Input.Token = token ?? "";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                // Kullanıcı enumeration'ı önlemek için aynı mesaj
                ResetSuccess = true;
                return Page();
            }

            var result = await _userManager.ResetPasswordAsync(user, Input.Token, Input.Password);
            if (result.Succeeded)
            {
                ResetSuccess = true;
            }
            else
            {
                ErrorMessage = "Şifre sıfırlama başarısız. Bağlantı süresi dolmuş olabilir. Lütfen tekrar deneyin.";
            }

            return Page();
        }
    }
}
