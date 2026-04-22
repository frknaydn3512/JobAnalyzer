using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;

namespace JobAnalyzer.Web.Pages.Account
{
    [EnableRateLimiting("login")]
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IConfiguration _config;

        public ForgotPasswordModel(UserManager<AppUser> userManager, IConfiguration config)
        {
            _userManager = userManager;
            _config = config;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool EmailSent { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "E-posta gerekli")]
            [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin")]
            public string Email { get; set; } = "";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var encodedToken = WebUtility.UrlEncode(token);
                var resetUrl = $"{Request.Scheme}://{Request.Host}/Account/ResetPassword?email={WebUtility.UrlEncode(Input.Email)}&token={encodedToken}";

                await SendResetEmailAsync(Input.Email, resetUrl);
            }

            // Her durumda aynı mesajı göster (kullanıcı enumeration'ı önle)
            EmailSent = true;
            return Page();
        }

        private async Task SendResetEmailAsync(string toEmail, string resetUrl)
        {
            var smtpHost = _config["Email:SmtpHost"];
            if (string.IsNullOrEmpty(smtpHost)) return;

            var smtpPort = _config.GetValue<int>("Email:SmtpPort", 587);
            var user = _config["Email:Username"] ?? "";
            var pass = _config["Email:Password"] ?? "";
            var from = _config["Email:From"] ?? user;

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(user, pass),
                EnableSsl = true
            };

            string body = $@"
                <h3>Şifre Sıfırlama</h3>
                <p>Şifrenizi sıfırlamak için aşağıdaki bağlantıya tıklayın:</p>
                <a href='{WebUtility.HtmlEncode(resetUrl)}'
                   style='background:#0d6efd;color:white;padding:10px 20px;border-radius:8px;text-decoration:none'>
                   Şifremi Sıfırla
                </a>
                <p style='color:#888;font-size:12px;margin-top:20px'>
                    Bu işlemi siz yapmadıysanız bu e-postayı görmezden gelebilirsiniz.
                </p>";

            var msg = new MailMessage(from, toEmail)
            {
                Subject = "[JobAnalyzer] Şifre Sıfırlama",
                Body = body,
                IsBodyHtml = true
            };

            await client.SendMailAsync(msg);
        }
    }
}
