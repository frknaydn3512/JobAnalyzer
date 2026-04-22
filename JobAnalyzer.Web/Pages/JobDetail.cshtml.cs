using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using JobAnalyzer.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;

namespace JobAnalyzer.Web.Pages
{
    public class JobDetailModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UserManager<AppUser> _userManager;
        private readonly PlanService _planService;
        private readonly string _groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";

        public JobDetailModel(AppDbContext context, IHttpClientFactory httpClientFactory, UserManager<AppUser> userManager, PlanService planService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
            _planService = planService;
        }

        public JobPosting? Job { get; set; }
        public bool IsSaved { get; set; }
        public string? LimitMessage { get; set; }
        public bool CanGenerateLetter { get; set; } = true;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Job = await _context.JobPostings.FindAsync(id);
            if (Job == null) return RedirectToPage("/Listings");

            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User)!;
                IsSaved = await _context.SavedJobs.AnyAsync(s => s.UserId == userId && s.JobPostingId == id);

                var (letterAllowed, _) = await _planService.CanGenerateCoverLetterAsync(userId);
                CanGenerateLetter = letterAllowed;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostToggleSaveAsync(int id)
        {
            if (User.Identity?.IsAuthenticated != true) return RedirectToPage("/Account/Login");

            var userId = _userManager.GetUserId(User)!;
            var existing = await _context.SavedJobs.FirstOrDefaultAsync(s => s.UserId == userId && s.JobPostingId == id);

            if (existing != null)
            {
                _context.SavedJobs.Remove(existing);
            }
            else
            {
                var (allowed, reason) = await _planService.CanSaveJobAsync(userId);
                if (!allowed)
                    return RedirectToPage("/Pricing", new { msg = "limit" });

                _context.SavedJobs.Add(new SavedJob { UserId = userId, JobPostingId = id });
            }

            await _context.SaveChangesAsync();
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostGenerateLetterAsync(int id)
        {
            var job = await _context.JobPostings.FindAsync(id);
            if (job == null) return BadRequest("İlan bulunamadı.");

            string userSkills = "C#, .NET Core, SQL Server, Entity Framework, JavaScript, HTML, CSS";
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User)!;

                // Plan limiti kontrolü
                var (allowed, reason) = await _planService.CanGenerateCoverLetterAsync(userId);
                if (!allowed)
                    return Content($"__LIMIT__:{reason}");

                var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                if (!string.IsNullOrEmpty(profile?.ExtractedSkills))
                    userSkills = profile.ExtractedSkills;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_groqKey}");

                var prompt = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = new[] {
                        new { role = "system", content = "Sen profesyonel bir İK uzmanısın. İlana ve adayın yeteneklerine %100 uyumlu, ikna edici önyazılar yazarsın." },
                        new { role = "user", content = $"Şu iş ilanı için profesyonel bir önyazı hazırla. \n\nİlan: {job.Title} \nDetay: {job.Description} \n\nBenim Yeteneklerim: {userSkills} \n\nLütfen [Ad Soyad], [Telefon] gibi yerleri boş bırak ve sadece mektup içeriğini Türkçe olarak dön." }
                    },
                    temperature = 0.7
                };

                var content = new StringContent(JsonConvert.SerializeObject(prompt), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);

                if (response.IsSuccessStatusCode)
                {
                    var resBody = await response.Content.ReadAsStringAsync();
                    dynamic? jsonRes = JsonConvert.DeserializeObject(resBody);
                    string aiText = jsonRes?.choices[0].message.content ?? "";

                    // Başarılıysa sayacı artır
                    if (User.Identity?.IsAuthenticated == true)
                        await _planService.IncrementCoverLetterAsync(_userManager.GetUserId(User)!);

                    return Content(aiText);
                }
                return BadRequest("AI yanıt vermedi.");
            }
            catch (Exception ex)
            {
                return BadRequest("Hata: " + ex.Message);
            }
        }
    }
}