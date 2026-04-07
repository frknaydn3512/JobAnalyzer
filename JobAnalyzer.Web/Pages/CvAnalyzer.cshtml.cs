using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using JobAnalyzer.Data;
using JobAnalyzer.Data.Models;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using Newtonsoft.Json;
using System.Text;
using Microsoft.AspNetCore.Http.Features;


namespace JobAnalyzer.Web.Pages
{
    [RequestSizeLimit(10 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    public class CvAnalyzerModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";

        public CvAnalyzerModel(AppDbContext context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty]
        public IFormFile? UploadedCv { get; set; }

        public CvResult? AnalysisResult { get; set; }
        public string? ErrorMessage { get; set; }
        
        // 🌟 Eşleşen ilanları tutacağımız liste (Sınıfın İÇİNDE olmalı)
        public List<JobPosting> RecommendedJobs { get; set; } = new List<JobPosting>();

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (UploadedCv == null || UploadedCv.Length == 0)
            {
                ErrorMessage = "Lütfen geçerli bir PDF dosyası seçin.";
                return Page();
            }

            if (!UploadedCv.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                && !UploadedCv.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "Yalnızca PDF dosyaları kabul edilmektedir.";
                return Page();
            }

            try
            {
                string cvText = "";
                byte[] pdfBytes;
                using (var ms = new MemoryStream())
                {
                    await UploadedCv.CopyToAsync(ms);
                    pdfBytes = ms.ToArray();
                }

                using (var document = PdfDocument.Open(pdfBytes))
                {
                    foreach (var page in document.GetPages())
                    {
                        cvText += page.Text + " ";
                    }
                }

                if (string.IsNullOrWhiteSpace(cvText))
                    throw new Exception("PDF metni okunamadı. Dosya taranmış bir resim olabilir.");

                var rawSkills = await _context.JobPostings
                    .Where(j => !string.IsNullOrEmpty(j.ExtractedSkills))
                    .Select(j => j.ExtractedSkills)
                    .ToListAsync();

                var topSkills = rawSkills
                    .Where(s => s != null)
                    .SelectMany(s => s!.Split(','))
                    .GroupBy(s => s.Trim())
                    .OrderByDescending(g => g.Count())
                    .Take(25)
                    .Select(g => g.Key)
                    .ToList();

                string marketTrendStr = string.Join(", ", topSkills);

                // 6. AI Analizini Başlat
                AnalysisResult = await GetAiAnalysis(cvText, marketTrendStr);

                // 🌟 7. EŞLEŞTİRME SİSTEMİNİ ÇALIŞTIR (Analiz bittikten sonra)
                if (AnalysisResult != null && !string.IsNullOrEmpty(AnalysisResult.MySkills))
                {
                    await FindMatches(AnalysisResult.MySkills);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Analiz başarısız: {ex.Message}";
                Console.WriteLine($"!!! KRİTİK HATA: {ex}");
            }

            return Page();
        }

        // 🌟 Eşleştirme Metodu (Yine sınıfın İÇİNDE)
        private async Task FindMatches(string userSkills)
        {
            var skillList = userSkills.Split(',').Select(s => s.Trim().ToLower()).ToList();
            
            var allJobs = await _context.JobPostings.ToListAsync();
            
            RecommendedJobs = allJobs
                .Select(j => new { 
                    Job = j, 
                    Score = j.ExtractedSkills?.Split(',')
                             .Select(s => s.Trim().ToLower())
                             .Intersect(skillList).Count() ?? 0 
                })
                .Where(x => x.Score > 0) // Hiç eşleşme olmayanları ele
                .OrderByDescending(x => x.Score)
                .Take(5) // En iyi 5 ilanı al
                .Select(x => x.Job)
                .ToList();
        }

        private async Task<CvResult> GetAiAnalysis(string cvText, string topMarketSkills)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_groqKey}");

            var requestObj = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[] {
                    new { role = "system", content = "Sen profesyonel bir İK ve teknik yetenek analistisin. Sadece JSON döneceksin." },
                    new { role = "user", content = $"Aşağıdaki CV'yi piyasa trendleriyle kıyasla. CV'deki projelere bakarak tecrübe seviyesini (Junior, Mid veya Senior) tahmin et. Ayrıca 2026 Türkiye yazılım piyasası şartlarına göre bu adayın hak ettiği tahmini aylık net maaş aralığını belirle. \nTrendler: {topMarketSkills} \nCV: {cvText} \n\nYanıt formatı tam olarak şu JSON olmalı: {{ \"MySkills\": \"Yetenekler\", \"MissingSkills\": [\"Eksik1\"], \"Advice\": \"Öneri\", \"Level\": \"Tahmin Edilen Seviye\", \"EstimatedSalary\": \"Örn: 80.000 TL - 110.000 TL\" }}" }
                },
                temperature = 0.2
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestObj), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);

            if (response.IsSuccessStatusCode)
            {
                var resBody = await response.Content.ReadAsStringAsync();
                dynamic? jsonRes = JsonConvert.DeserializeObject(resBody);

                if (jsonRes == null)
                    throw new Exception("Groq API'den boş yanıt geldi.");

                string rawAiText = jsonRes.choices[0].message.content;

                if (rawAiText.Contains("{"))
                {
                    int start = rawAiText.IndexOf('{');
                    int end = rawAiText.LastIndexOf('}');
                    if (end > start)
                        rawAiText = rawAiText.Substring(start, end - start + 1);
                }

                return JsonConvert.DeserializeObject<CvResult>(rawAiText)
                    ?? throw new Exception("AI yanıtı parse edilemedi.");
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq API hatası: {response.StatusCode} - {errorBody}");
        }
    }

    public class CvResult
    {
        public string? MySkills { get; set; }
        public List<string>? MissingSkills { get; set; }
        public string? Advice { get; set; }
        public string? Level { get; set; }
        public string? EstimatedSalary { get; set; }
    }
}