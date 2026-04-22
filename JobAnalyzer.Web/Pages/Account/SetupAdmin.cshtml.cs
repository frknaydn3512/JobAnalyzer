using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace JobAnalyzer.Web.Pages.Account
{
    public class SetupAdminModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public SetupAdminModel(UserManager<AppUser> userManager, IWebHostEnvironment env)
        {
            _userManager = userManager;
            _env = env;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool AlreadyHasAdmin { get; set; }
        public string? Message { get; set; }
        public bool IsSuccess { get; set; }

        public class InputModel
        {
            [Required, EmailAddress]
            public string Email { get; set; } = "";

            [Required, MinLength(8)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count > 0)
            {
                return RedirectToPage("/Index");
            }

            if (!_env.IsDevelopment())
            {
                return Forbid();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count > 0 || !_env.IsDevelopment())
            {
                return Forbid();
            }

            if (!ModelState.IsValid) return Page();

            // Var olan kullanıcıya Admin rolü ver, yoksa oluştur
            var existing = await _userManager.FindByEmailAsync(Input.Email);
            if (existing != null)
            {
                await _userManager.AddToRoleAsync(existing, "Admin");
                IsSuccess = true;
                Message = $"{Input.Email} hesabına Admin rolü verildi. Artık /Admin sayfasına girebilirsin.";
                return Page();
            }

            var user = new AppUser
            {
                UserName = Input.Email,
                Email = Input.Email,
                FullName = "Admin"
            };

            var result = await _userManager.CreateAsync(user, Input.Password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, "Admin");
                IsSuccess = true;
                Message = $"Admin hesabı oluşturuldu: {Input.Email}. Şimdi giriş yapabilirsin.";
            }
            else
            {
                Message = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return Page();
        }
    }
}
