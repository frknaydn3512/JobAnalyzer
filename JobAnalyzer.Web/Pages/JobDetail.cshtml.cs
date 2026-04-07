using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using System.Text;

namespace JobAnalyzer.Web.Pages
{
    public class JobDetailModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";

        public JobDetailModel(AppDbContext context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        public JobPosting? Job { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Job = await _context.JobPostings.FindAsync(id);
            if (Job == null) return RedirectToPage("/Listings");
            return Page();
        }

        // 🌟 AJAX İsteğini Karşılayan Metod
        public async Task<IActionResult> OnPostGenerateLetterAsync(int id)
        {
            var job = await _context.JobPostings.FindAsync(id);
            if (job == null) return BadRequest("İlan bulunamadı.");

            // Şimdilik analiz ettiğimiz yetenekleri statik alıyoruz, 
            // ileride Identity sistemine geçince bunu profilden çekeceğiz.
            string userSkills = "C#, .NET Core, SQL Server, Entity Framework, JavaScript, HTML, CSS";

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
                    dynamic jsonRes = JsonConvert.DeserializeObject(resBody);
                    string aiText = jsonRes.choices[0].message.content;
                    return Content(aiText); // Sadece metni döndür
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